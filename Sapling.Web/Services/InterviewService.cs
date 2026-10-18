using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Sapling.Shared.Contracts;
using Sapling.Web.Ai;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>
/// Voice mock interview. The browser does speech in both directions and measures delivery; this service keeps the
/// transcript, asks the model for each interviewer line, and builds the report at the end.
/// </summary>
public sealed partial class InterviewService(
    SaplingDbContext db,
    StudentContext ctx,
    IChatClient chat,
    ICareerService careers,
    IPdfTextExtractor pdf,
    IInterviewVoice voice,
    ILogger<InterviewService> log) : IInterviewService
{
    private const int MaxResumeBytes = 5 * 1024 * 1024;
    private const int MaxResumeChars = 12_000;
    private const int MaxInstructionChars = 1_000;
    private const int MaxAnswerChars = 6_000;

    /// <summary>Short, medium, long. The interviewer may stretch to 1.3x; the server closes at 1.5x regardless.</summary>
    private static readonly int[] Lengths = [10, 20, 30];
    private const double HardStopFactor = 1.5;

    /// <summary>Gaps longer than this (leaving and coming back) don't count towards the interview's length.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(4);

    private static readonly string[] Phases = ["Intro", "Background", "Technical", "Behavioural", "Closing"];

    private const string ClosingLine =
        "That's everything I wanted to cover today. Thank you for your time; your detailed feedback is on the next screen.";

    /// <summary>Used only when the model returns something unusable, so a session never stalls.</summary>
    private static readonly Dictionary<string, string> FallbackQuestions = new()
    {
        ["Intro"] = "To start, could you tell me a little about yourself?",
        ["Background"] = "Tell me about a project you worked on. What exactly was your part in it?",
        ["Technical"] = "What is one technical concept from your course you find really useful, and where have you used it?",
        ["Behavioural"] = "Tell me about a time you had to meet a tight deadline. What did you do?",
    };

    public async Task<IReadOnlyList<InterviewSummaryDto>> GetHistoryAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var interviews = await db.MockInterviews
            .Include(i => i.Turns)
            .Where(i => i.StudentProfileId == profile.Id)
            .OrderByDescending(i => i.Id)
            .AsNoTracking()
            .ToListAsync(ct);

        return interviews
            .Select(i => new InterviewSummaryDto(
                i.Id, i.RoleTitle, i.Status, i.CreatedAt, i.StartedAt, i.EndedAt, i.OverallScore,
                i.Turns.Count(t => t.Speaker == "Interviewer"), i.ResumeText != null,
                i.TargetMinutes, (int)ActiveTime(i.Turns, null).TotalSeconds))
            .ToList();
    }

    public async Task<InterviewDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var interview = await LoadAsync(id, ct);
        return interview is null ? null : Map(interview);
    }

    public async Task<InterviewDto> CreateAsync(CreateInterviewRequest request, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);

        var title = request.RoleTitle.Trim();
        int? roleId = null;
        if (request.CareerRoleId is { } id && await careers.GetPathAsync(id, ct) is { } path)
        {
            roleId = path.Id;
            title = path.Title;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InterviewProblemException("Choose a role to be interviewed for.");
        }

        var minutes = Lengths.Contains(request.TargetMinutes) ? request.TargetMinutes : 20;
        var interview = new MockInterview
        {
            StudentProfileId = profile.Id,
            CareerRoleId = roleId,
            RoleTitle = Truncate(title, 120),
            Instructions = Truncate(request.Instructions?.Trim() ?? "", MaxInstructionChars),
            TargetMinutes = minutes,

            // Safety net only: about one exchange a minute, plus room for a longer run.
            QuestionLimit = (int)Math.Ceiling(minutes * HardStopFactor) + 2,
        };

        db.MockInterviews.Add(interview);
        await db.SaveChangesAsync(ct);
        return Map(interview);
    }

    public async Task<InterviewDto> AttachResumeAsync(int id, string fileName, byte[] pdfBytes, CancellationToken ct = default)
    {
        var interview = await RequireAsync(id, ct);
        if (interview.Status is not ("Setup" or "Ready"))
        {
            throw new InterviewProblemException("This interview has already started, so its resume can't be changed.");
        }

        if (pdfBytes.Length > MaxResumeBytes)
        {
            throw new InterviewProblemException("That file is over 5 MB. Upload a smaller PDF.");
        }

        if (pdfBytes.Length < 5 || Encoding.ASCII.GetString(pdfBytes, 0, 5) != "%PDF-")
        {
            throw new InterviewProblemException("That file isn't a PDF. Export your resume as a PDF and try again.");
        }

        var text = pdf.Extract(pdfBytes);
        interview.ResumeFileName = Truncate(Path.GetFileName(fileName), 200);
        interview.ResumeText = text.Length < 50 ? null : Truncate(text, MaxResumeChars);
        interview.Status = "Ready";
        await db.SaveChangesAsync(ct);
        return Map(interview);
    }

    public async Task<InterviewDto> SkipResumeAsync(int id, CancellationToken ct = default)
    {
        var interview = await RequireAsync(id, ct);
        if (interview.Status is "Setup" or "Ready")
        {
            // Skipping after an upload means going without it.
            interview.ResumeText = null;
            interview.ResumeFileName = null;
            interview.Status = "Ready";
            await db.SaveChangesAsync(ct);
        }

        return Map(interview);
    }

    public async Task<InterviewerLineDto> BeginAsync(int id, CancellationToken ct = default)
    {
        var interview = await RequireAsync(id, ct);
        if (interview.Status == "Complete")
        {
            throw new InterviewProblemException("This interview is already finished.");
        }

        var last = interview.Turns.OrderBy(t => t.Order).LastOrDefault();

        // Reload or retry: repeat the question still waiting for an answer.
        if (last is { Speaker: "Interviewer" })
        {
            return Line(interview, last, done: interview.EndedAt is not null);
        }

        return await NextLineAsync(interview, ct);
    }

    public async Task<InterviewerLineDto> AnswerAsync(int id, SubmitAnswerRequest request, CancellationToken ct = default)
    {
        var interview = await RequireAsync(id, ct);
        var last = interview.Turns.OrderBy(t => t.Order).LastOrDefault();

        if (interview.Status != "Live" || last is null)
        {
            throw new InterviewProblemException("This interview isn't running.");
        }

        if (interview.EndedAt is not null)
        {
            return Line(interview, last, done: true);
        }

        // A second tab or a double click: the answer is already saved, so just produce the next line.
        if (last.Speaker == "Interviewer")
        {
            var text = Truncate(request.Text?.Trim() ?? "", MaxAnswerChars);
            var m = request.Metrics;
            var words = WordCount(text);
            interview.Turns.Add(new MockInterviewTurn
            {
                Order = last.Order + 1,
                Speaker = "Candidate",
                Phase = interview.Phase,
                Text = text.Length == 0 ? "(no answer)" : text,
                ResponseDelayMs = Math.Clamp(m.ResponseDelayMs, 0, 600_000),
                SpeakingMs = Math.Clamp(m.SpeakingMs, 0, 3_600_000),
                LongPauses = Math.Clamp(m.LongPauses, 0, 1_000),
                LongestPauseMs = Math.Clamp(m.LongestPauseMs, 0, 600_000),
                WordCount = words,
                FillerCount = Math.Max(Math.Clamp(m.FillerCount, 0, 10_000), CountFillers(text)),
                Typed = m.Typed,
            });

            // Saved before the model call, so a model failure never loses the answer.
            await db.SaveChangesAsync(ct);
        }

        return await NextLineAsync(interview, ct);
    }

    public async Task<InterviewDto> FinishAsync(int id, CancellationToken ct = default)
    {
        var interview = await RequireAsync(id, ct);
        if (interview.Status == "Complete")
        {
            return Map(interview);
        }

        var turns = interview.Turns.OrderBy(t => t.Order).ToList();
        var answers = turns.Where(t => t.Speaker == "Candidate").ToList();
        var delivery = Delivery(answers);

        InterviewReportDto report;
        if (answers.Count == 0)
        {
            report = FallbackReport(delivery, "You ended the interview before answering any questions, so there is nothing to assess yet. Start a new interview when you're ready.");
        }
        else
        {
            report = await ReportAsync(interview, turns, delivery, ct)
                ?? FallbackReport(delivery, "The AI reviewer couldn't produce a report this time, so only your delivery numbers are shown. Your transcript is saved below.");
        }

        interview.Status = "Complete";
        interview.EndedAt ??= DateTimeOffset.UtcNow;
        interview.StartedAt ??= interview.CreatedAt;
        interview.ReportJson = JsonOutput.Serialize(report);
        interview.OverallScore = report.Overall;
        await db.SaveChangesAsync(ct);
        return Map(interview);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var interview = await LoadAsync(id, ct);
        if (interview is not null)
        {
            db.MockInterviews.Remove(interview);
            await db.SaveChangesAsync(ct);
        }
    }

    // ---------- Interviewer turns ----------

    private sealed record ModelAssessment(int Score, string? Note);

    private sealed record ModelLine(ModelAssessment? Assessment, string? Say, string? Phase, bool Done);

    private async Task<InterviewerLineDto> NextLineAsync(MockInterview interview, CancellationToken ct)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var turns = interview.Turns.OrderBy(t => t.Order).ToList();
        var spoken = turns.Count(t => t.Speaker == "Interviewer");
        var now = DateTimeOffset.UtcNow;
        var elapsed = ActiveTime(turns, now);
        var mustClose = spoken + 1 >= interview.QuestionLimit
            || elapsed.TotalMinutes >= interview.TargetMinutes * HardStopFactor;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.Interviewer(
                interview.RoleTitle, interview.Instructions, ProfileFacts(profile), interview.ResumeText, interview.TargetMinutes)),
            new(ChatRole.User, Prompts.InterviewTurn(
                CompactTranscript(turns), spoken, elapsed.TotalMinutes, interview.TargetMinutes, mustClose)),
        };

        ModelLine? line = null;
        for (var attempt = 0; attempt < 2 && line is null; attempt++)
        {
            try
            {
                // Low reasoning effort keeps the pause before each question short, which matters in a spoken interview.
                var text = await JsonOutput.AskAsync(
                    chat, messages, temperature: 0.7f, maxOutputTokens: 2000, JsonOutput.Reasoning.Low, ct);
                if (JsonOutput.TryParse<ModelLine>(text, out var parsed) && !string.IsNullOrWhiteSpace(parsed.Say))
                {
                    line = parsed;
                }
                else
                {
                    log.LogWarning("Interviewer reply was not usable JSON: {Reply}", text);
                }
            }
            catch (AiUnavailableException e)
            {
                throw new InterviewProblemException(e.Message + " Your progress is saved.", retryable: true);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Interviewer model call failed (attempt {Attempt}).", attempt + 1);
            }
        }

        var currentIndex = Math.Max(0, Array.IndexOf(Phases, interview.Phase));
        var proposedIndex = Array.IndexOf(Phases, line?.Phase ?? "");
        var phase = Phases[Math.Max(currentIndex, proposedIndex)];   // phases only move forward

        string say;
        bool done;
        if (line is null)
        {
            done = mustClose;
            phase = done ? "Closing" : phase;
            say = done ? ClosingLine : FallbackQuestions[phase == "Closing" ? "Behavioural" : phase];
        }
        else
        {
            say = Clean(line.Say!);
            done = line.Done || mustClose;
            if (done && !line.Done)
            {
                // Out of time or turns but the model asked another question: close politely instead.
                say = ClosingLine;
            }

            if (done)
            {
                phase = "Closing";
            }
        }

        // The judgement belongs to the answer it was made about.
        if (line?.Assessment is { } assessment && turns.LastOrDefault() is { Speaker: "Candidate" } answered)
        {
            answered.AssessmentScore = Score(assessment.Score);
            answered.AssessmentNote = string.IsNullOrWhiteSpace(assessment.Note) ? null : Truncate(assessment.Note.Trim(), 400);
        }

        var turn = new MockInterviewTurn
        {
            Order = turns.Count == 0 ? 1 : turns[^1].Order + 1,
            Speaker = "Interviewer",
            Phase = phase,
            Text = say,
            At = now,
        };
        interview.Turns.Add(turn);

        interview.Phase = phase;
        if (interview.Status != "Live")
        {
            interview.Status = "Live";
            interview.StartedAt = now;
        }

        if (done)
        {
            interview.EndedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Line(interview, turn, done);
    }

    /// <summary>
    /// Recent turns verbatim, older answers shortened (keeps each call fast and cheap), with the interviewer's own
    /// notes so it can see which gaps it has already found.
    /// </summary>
    private static string CompactTranscript(List<MockInterviewTurn> turns)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < turns.Count; i++)
        {
            var t = turns[i];
            var old = i < turns.Count - 4;
            var text = old && t.Speaker == "Candidate" && t.Text.Length > 300 ? t.Text[..300] + " […]" : t.Text;
            sb.Append(t.Speaker == "Interviewer" ? "I: " : "C: ").AppendLine(text);
            if (t.AssessmentScore is { } score)
            {
                sb.Append(CultureInfo.InvariantCulture, $"[note: {score}/100. {t.AssessmentNote}]").AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Time spent in the interview: the gaps between turns, each capped, so leaving and coming back later doesn't
    /// count. Pass <paramref name="now"/> to include the time since the last turn.
    /// </summary>
    private static TimeSpan ActiveTime(IEnumerable<MockInterviewTurn> turns, DateTimeOffset? now)
    {
        var ordered = turns.OrderBy(t => t.Order).ToList();
        var total = TimeSpan.Zero;
        for (var i = 1; i < ordered.Count; i++)
        {
            total += Cap(ordered[i].At - ordered[i - 1].At);
        }

        if (now is { } n && ordered.Count > 0)
        {
            total += Cap(n - ordered[^1].At);
        }

        return total;

        static TimeSpan Cap(TimeSpan gap) => gap < TimeSpan.Zero ? TimeSpan.Zero : gap > MaxGap ? MaxGap : gap;
    }

    // ---------- Report ----------

    private sealed record ModelReport(
        int Overall,
        string? Summary,
        List<AreaScoreDto>? Areas,
        List<string>? Strengths,
        List<string>? StuckPoints,
        List<string>? TopicsToImprove,
        List<QuestionFeedbackDto>? Questions,
        List<string>? NextSteps);

    private static readonly string[] AreaNames = ["Communication", "Technical depth", "Structure", "Confidence", "Role fit"];

    private async Task<InterviewReportDto?> ReportAsync(
        MockInterview interview, List<MockInterviewTurn> turns, DeliveryStatsDto delivery, CancellationToken ct)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var transcript = new StringBuilder();
        var question = 0;
        foreach (var t in turns)
        {
            if (t.Speaker == "Interviewer")
            {
                question++;
                transcript.Append(CultureInfo.InvariantCulture, $"I{question}: ").AppendLine(t.Text);
            }
            else
            {
                var how = t.Typed
                    ? "typed"
                    : string.Create(CultureInfo.InvariantCulture,
                        $"delay {t.ResponseDelayMs / 1000.0:0.0}s, {t.WordCount} words, {t.LongPauses} long pauses, {t.FillerCount} fillers");
                transcript.Append(CultureInfo.InvariantCulture, $"C [{how}]: ").AppendLine(t.Text);
                if (t.AssessmentScore is { } score)
                {
                    transcript.Append(CultureInfo.InvariantCulture, $"Interviewer's note: {score}/100. {t.AssessmentNote}").AppendLine();
                }
            }
        }

        var stats = string.Create(CultureInfo.InvariantCulture, $"""
            Answers: {delivery.Answers} ({delivery.TypedAnswers} typed instead of spoken)
            Average delay before starting to answer: {delivery.AverageResponseDelayMs / 1000.0:0.0} s
            Long pauses (over 1.5 s) while answering: {delivery.TotalLongPauses}, longest {delivery.LongestPauseMs / 1000.0:0.0} s
            Speaking pace: {delivery.WordsPerMinute:0} words per minute
            Filler words: {delivery.FillerCount} ({delivery.FillersPerMinute:0.0} per minute)
            Measured confidence score: {(delivery.ConfidenceScore > 0 ? $"{delivery.ConfidenceScore}/100" : "not measured, every answer was typed; judge confidence from the wording only")}
            Length: {ActiveTime(turns, null).TotalMinutes:0} minutes, planned about {interview.TargetMinutes}
            Candidate ended the interview before the interviewer closed it: {(interview.EndedAt is null ? "yes" : "no")}
            """);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.System),
            new(ChatRole.User, Prompts.InterviewReport(interview.RoleTitle, ProfileFacts(profile), transcript.ToString(), stats)),
        };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var text = await JsonOutput.AskAsync(
                    chat, messages, temperature: 0.2f, maxOutputTokens: 8000, JsonOutput.Reasoning.Medium, ct);
                if (JsonOutput.TryParse<ModelReport>(text, out var r) && r.Areas is { Count: > 0 })
                {
                    return Normalise(r, delivery);
                }

                log.LogWarning("Interview report was not usable JSON: {Reply}", text);
            }
            catch (AiUnavailableException e)
            {
                throw new InterviewProblemException(e.Message + " Your interview is saved; try finishing it again later.", retryable: true);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Interview report call failed (attempt {Attempt}).", attempt + 1);
            }
        }

        return null;
    }

    private static InterviewReportDto Normalise(ModelReport r, DeliveryStatsDto delivery)
    {
        var areas = AreaNames
            .Select(name => r.Areas!.FirstOrDefault(a => string.Equals(a.Area, name, StringComparison.OrdinalIgnoreCase)))
            .Where(a => a is not null)
            .Select(a => a! with { Score = Score(a.Score), Comment = a.Comment ?? "" })
            .ToList();

        return new InterviewReportDto(
            Score(r.Overall),
            r.Summary?.Trim() ?? "",
            ConfidenceLevel(delivery.ConfidenceScore),
            areas,
            Clean(r.Strengths),
            Clean(r.StuckPoints),
            Clean(r.TopicsToImprove),
            (r.Questions ?? [])
                .Where(q => !string.IsNullOrWhiteSpace(q.Question))
                .Select(q => q with
                {
                    Score = Score(q.Score),
                    AnswerSummary = q.AnswerSummary ?? "",
                    WhatWorked = q.WhatWorked ?? "",
                    WhatToFix = q.WhatToFix ?? "",
                })
                .ToList(),
            Clean(r.NextSteps),
            delivery);

        static List<string> Clean(List<string>? items) =>
            items?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
    }

    private static InterviewReportDto FallbackReport(DeliveryStatsDto delivery, string summary) => new(
        delivery.ConfidenceScore,
        summary,
        ConfidenceLevel(delivery.ConfidenceScore),
        delivery.ConfidenceScore == 0 ? [] : [new AreaScoreDto("Confidence", delivery.ConfidenceScore, "Measured from your response delays, pauses, filler words and pace.")],
        [], [], [], [], [], delivery);

    /// <summary>Delivery measured in the browser. Deterministic, so it is the same every time the report is read.</summary>
    private static DeliveryStatsDto Delivery(List<MockInterviewTurn> answers)
    {
        var spoken = answers.Where(a => !a.Typed && a.SpeakingMs > 0).ToList();
        var typed = answers.Count(a => a.Typed);
        var delays = spoken.Where(a => a.ResponseDelayMs > 0).Select(a => a.ResponseDelayMs).ToList();
        var avgDelay = delays.Count == 0 ? 0 : (int)delays.Average();
        var speakingMs = spoken.Sum(a => a.SpeakingMs);
        var minutes = speakingMs / 60_000.0;
        var wpm = minutes > 0 ? Math.Round(spoken.Sum(a => a.WordCount) / minutes, 0) : 0;
        var spokenFillers = spoken.Sum(a => a.FillerCount);
        var fillersPerMin = minutes > 0 ? Math.Round(spokenFillers / minutes, 1) : 0;
        var longPauses = spoken.Sum(a => a.LongPauses);
        var longest = spoken.Count == 0 ? 0 : spoken.Max(a => a.LongestPauseMs);

        // Typed answers carry no voice data, so there is nothing to measure delivery from.
        double confidence = 100;
        if (spoken.Count == 0)
        {
            confidence = 0;
        }
        else
        {
            confidence -= 25 * Clamp01((avgDelay - 1500) / 4000.0);
            confidence -= 20 * Clamp01(spoken.Count == 0 ? 0 : longPauses / (double)spoken.Count / 3);
            confidence -= 15 * Clamp01(fillersPerMin / 6);
            confidence -= 10 * (typed / (double)answers.Count);
            confidence -= wpm > 0 && (wpm < 90 || wpm > 190) ? 8 : 0;
            confidence -= 15 * Clamp01(answers.Count(a => a.WordCount < 12) / (double)answers.Count);
        }

        return new DeliveryStatsDto(
            answers.Count,
            avgDelay,
            longPauses,
            longest,
            wpm,
            answers.Sum(a => a.FillerCount),
            fillersPerMin,
            (int)(speakingMs / 1000),
            typed,
            spoken.Count == 0 ? 0 : Math.Clamp((int)Math.Round(confidence), 5, 98));

        static double Clamp01(double v) => Math.Clamp(v, 0, 1);
    }

    private static string ConfidenceLevel(int score) => score switch
    {
        <= 0 => "Not measured",
        < 45 => "Low",
        < 70 => "Medium",
        _ => "High",
    };

    // ---------- Helpers ----------

    private async Task<MockInterview?> LoadAsync(int id, CancellationToken ct)
    {
        var profile = await ctx.GetProfileAsync(ct);
        return await db.MockInterviews
            .Include(i => i.Turns)
            .FirstOrDefaultAsync(i => i.Id == id && i.StudentProfileId == profile.Id, ct);
    }

    private async Task<MockInterview> RequireAsync(int id, CancellationToken ct) =>
        await LoadAsync(id, ct) ?? throw new InterviewProblemException("That interview doesn't exist.");

    private static string ProfileFacts(StudentProfile p) => ProfileText.Facts(p);

    private static InterviewerLineDto Line(MockInterview i, MockInterviewTurn turn, bool done) =>
        new(turn.Text, i.Phase, done, turn.Order, i.Turns.Count(t => t.Speaker == "Interviewer"),
            (int)ActiveTime(i.Turns, null).TotalSeconds, i.TargetMinutes);

    private InterviewDto Map(MockInterview i)
    {
        InterviewReportDto? report = null;
        if (i.ReportJson is { } json && JsonOutput.TryParse<InterviewReportDto>(json, out var parsed))
        {
            report = parsed;
        }

        return new InterviewDto(
            i.Id,
            i.CareerRoleId,
            i.RoleTitle,
            i.Instructions,
            i.Status,
            i.Phase,
            i.TargetMinutes,
            (int)ActiveTime(i.Turns, null).TotalSeconds,
            voice.Available,
            i.Turns.Count(t => t.Speaker == "Interviewer"),
            i.ResumeText is not null,
            i.ResumeFileName,
            i.ResumeText?.Length ?? 0,
            i.ResumeFileName is not null && i.ResumeText is null
                ? "We couldn't read any text from that PDF (it may be a scanned image). The interview will use your profile instead."
                : null,
            i.CreatedAt,
            i.StartedAt,
            i.EndedAt,
            i.Turns.OrderBy(t => t.Order).Select(t => new InterviewTurnDto(
                t.Id, t.Order, t.Speaker, t.Phase, t.Text, t.At, t.ResponseDelayMs, t.SpeakingMs,
                t.LongPauses, t.LongestPauseMs, t.WordCount, t.FillerCount, t.Typed,
                t.AssessmentScore, t.AssessmentNote)).ToList(),
            report);
    }

    private static int Score(int value) => Math.Clamp(value, 0, 100);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static int CountFillers(string text) => Fillers().Count(text);

    /// <summary>Strips markdown and stage directions the model sometimes adds; the text is read aloud.</summary>
    private static string Clean(string say) =>
        Whitespace().Replace(StageDirections().Replace(say.Replace("*", "").Replace("#", "").Replace("`", ""), " "), " ").Trim();

    [GeneratedRegex(@"\b(um+|uh+|erm+|hmm+|basically|actually|you know|so yeah|i mean)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Fillers();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*(smiles|pauses|laughs|nods)[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex StageDirections();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
