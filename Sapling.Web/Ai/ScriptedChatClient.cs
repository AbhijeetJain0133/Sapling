using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Sapling.Web.Ai;

/// <summary>
/// Stands in for a hosted model when no API key is configured. Responses are deterministic, which keeps demos
/// honest and lets the whole interview flow, including the report, run offline. The interview questions here are a
/// fixed list and ignore the answers; with Ai:HuggingFace:ApiKey set, the model chooses every question.
/// </summary>
public sealed partial class ScriptedChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("scripted", new Uri("inproc://sapling-scripted"));

    private static readonly (string Say, string Phase)[] Script =
    [
        ("Hi, I'm Sapling AI, and I'll be running your interview today. To start, could you tell me a little about yourself?", "Intro"),
        ("Thanks. Pick one project you are proud of. What exactly did you build, and what was your part in it?", "Background"),
        ("What was the hardest problem you hit on that project, and how did you get past it?", "Background"),
        ("Let's get a bit technical. Can you explain the difference between a process and a thread?", "Technical"),
        ("Say a web page you built is loading slowly. How would you work out why?", "Technical"),
        ("How would you design a simple database table structure for a college library?", "Technical"),
        ("Tell me about a time you disagreed with a teammate. What happened, and how did it end?", "Behavioural"),
        ("That's everything I wanted to cover today. Thank you for your time; you'll see your detailed feedback on the next screen.", "Closing"),
    ];

    private const string Report = """
        {
          "overall": 62,
          "summary": "This report was produced by the offline scripted reviewer because no AI key is set, so its wording is generic. Your delivery numbers below are real. Add Ai:HuggingFace:ApiKey for a report based on what you actually said.",
          "areas": [
            {"area": "Communication", "score": 64, "comment": "Answers were understandable. Check the delivery numbers for pauses and filler words."},
            {"area": "Technical depth", "score": 58, "comment": "Name the concept, then give a concrete example from your own work."},
            {"area": "Structure", "score": 60, "comment": "Lead with a one-line answer, then explain, then give an example."},
            {"area": "Confidence", "score": 63, "comment": "Based on how quickly you started answering and how often you paused."},
            {"area": "Role fit", "score": 65, "comment": "Link each answer back to what the role needs day to day."}
          ],
          "strengths": ["You completed the full interview.", "You attempted every question instead of skipping."],
          "stuckPoints": ["The scripted reviewer cannot tell where you got stuck. With an AI key set, this lists each question where you stalled."],
          "topicsToImprove": ["Operating system basics: processes and threads", "Web performance debugging", "Database table design"],
          "questions": [],
          "nextSteps": ["Record yourself answering 'tell me about yourself' in under 90 seconds.", "Write a STAR story for one team conflict.", "Revise processes vs threads with one real example."]
        }
        """;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Join('\n', messages.Select(m => m.Text));
        var reply = Respond(prompt);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static string Respond(string prompt)
    {
        if (prompt.Contains(Prompts.InterviewTurnTag, StringComparison.Ordinal))
        {
            var match = TurnsSoFar().Match(prompt);
            var spoken = match.Success ? int.Parse(match.Groups[1].Value) : 0;
            var mustClose = prompt.Contains("You MUST close", StringComparison.Ordinal);
            var index = mustClose ? Script.Length - 1 : Math.Min(spoken, Script.Length - 1);
            var (say, phase) = Script[index];
            var assessment = spoken == 0 ? null : new { score = 60, note = "Scripted interviewer: no AI key is set, so answers are not assessed." };
            return JsonOutput.Serialize(new { assessment, say, phase, done = index == Script.Length - 1 });
        }

        if (prompt.Contains(Prompts.InterviewReportTag, StringComparison.Ordinal))
        {
            return Report;
        }

        if (prompt.Contains(Prompts.ResumeExtractTag, StringComparison.Ordinal)
            || prompt.Contains(Prompts.ResumeDraftTag, StringComparison.Ordinal))
        {
            return ResumeData;
        }

        if (prompt.Contains(Prompts.ResumeAnalyseTag, StringComparison.Ordinal))
        {
            return ResumeAnalysis;
        }

        if (prompt.Contains(Prompts.ResumeEditTag, StringComparison.Ordinal))
        {
            var match = LatexBlock().Match(prompt);
            var latex = match.Success ? match.Groups[1].Value : "";
            const string marker = "\\begin{document}";
            var at = latex.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0 && !latex.Contains("% Scripted edit", StringComparison.Ordinal))
            {
                latex = latex.Insert(at + marker.Length, "\n% Scripted edit: no AI key is set, so nothing was changed.");
            }

            return JsonOutput.Serialize(new
            {
                latex,
                note = "The offline scripted editor can't change your resume. Set Ai:HuggingFace:ApiKey to use AI edits.",
            });
        }

        if (prompt.Contains(Prompts.ExplainTag, StringComparison.Ordinal))
        {
            return "This came out ahead because your verified skills already cover most of what the role asks for, "
                 + "the remaining gaps are the cheapest ones on your list to close, and demand for the role in your region is rising.";
        }

        return "A hosted model is not configured, so this is the scripted response. "
             + "Set Ai:HuggingFace:ApiKey to use a live model.";
    }

    [GeneratedRegex(@"turns so far: (\d+)")]
    private static partial Regex TurnsSoFar();

    [GeneratedRegex(@"<latex>\s*(.*?)\s*</latex>", RegexOptions.Singleline)]
    private static partial Regex LatexBlock();

    // Contact fields are left empty: the resume service fills name and email from the student's profile.
    private const string ResumeData = """
        {
          "contact": {"fullName": "", "email": "", "phone": "", "location": "", "linkedIn": "", "gitHub": "", "website": ""},
          "summary": "Sample content from the offline scripted assistant, because no AI key is set. Replace every line with your own details.",
          "education": [{"institution": "Your college", "degree": "B.Tech", "field": "Computer Science & Engineering", "start": "2022", "end": "2026", "grade": "CGPA 7.5/10", "highlights": []}],
          "experience": [{"organisation": "Example Company", "role": "Software Intern", "location": "Indore", "start": "May 2025", "end": "Jul 2025", "bullets": ["Built an internal dashboard page in React used by the support team.", "Wrote SQL queries for weekly reports."]}],
          "projects": [
            {"name": "Attendance tracker", "link": "", "technologies": "Flutter, Firebase", "start": "", "end": "", "bullets": ["Built a mobile app that records class attendance with QR codes."]},
            {"name": "Library management system", "link": "", "technologies": "Java, MySQL", "start": "", "end": "", "bullets": ["Built issue and return flows with fine calculation."]}
          ],
          "skills": [
            {"category": "Languages", "items": ["Java", "Python", "SQL"]},
            {"category": "Frameworks", "items": ["React", "Flutter"]},
            {"category": "Tools", "items": ["Git", "Linux"]}
          ],
          "achievements": [{"title": "Example certification", "issuer": "NPTEL", "date": "2024", "detail": ""}]
        }
        """;

    private const string ResumeAnalysis = """
        {
          "overall": 64,
          "summary": "This review comes from the offline scripted reviewer because no AI key is set, so it is generic. Set Ai:HuggingFace:ApiKey for a review of what your resume actually says.",
          "scores": [
            {"area": "ATS parseability", "score": 78, "comment": "Standard headings and a single text layer parse well."},
            {"area": "Impact and metrics", "score": 48, "comment": "Most bullets describe tasks, not results."},
            {"area": "Role keywords", "score": 62, "comment": "Core languages are present; tools the role asks for are thin."},
            {"area": "Structure", "score": 70, "comment": "Sections are in a sensible order."},
            {"area": "Clarity", "score": 66, "comment": "Some bullets are long and start with weak verbs."}
          ],
          "suggestions": [
            {"section": "Experience", "issue": "Bullets say what you worked on but not what changed because of it.", "fix": "End each bullet with the result, for example how many people used it or how much time it saved. Add the real number.", "severity": "High"},
            {"section": "Projects", "issue": "Projects have no link to code or a demo.", "fix": "Add a GitHub link to each project you can share.", "severity": "Medium"},
            {"section": "Summary", "issue": "The summary is generic.", "fix": "Name the role you want, your strongest two skills and your best project in two sentences.", "severity": "Medium"},
            {"section": "Skills", "issue": "Skills are listed without grouping.", "fix": "Group them into Languages, Frameworks and Tools.", "severity": "Low"},
            {"section": "Contact", "issue": "No LinkedIn or GitHub profile.", "fix": "Add your LinkedIn and GitHub URLs to the contact line.", "severity": "Low"}
          ]
        }
        """;
}
