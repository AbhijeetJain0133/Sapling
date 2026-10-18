using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Sapling.Shared.Content;
using Sapling.Shared.Contracts;
using Sapling.Web.Ai;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>
/// Saved LaTeX resumes. Uploads and the wizard both end in structured data rendered by <see cref="LatexTemplates"/>;
/// after that the LaTeX is the source of truth and the AI edits it directly.
/// </summary>
public sealed partial class ResumeService(
    SaplingDbContext db,
    StudentContext ctx,
    IChatClient chat,
    IPdfTextExtractor pdf,
    ILatexCompiler compiler,
    ILogger<ResumeService> log) : IResumeService
{
    private const int MaxPdfBytes = 5 * 1024 * 1024;
    private const int MaxSourceChars = 12_000;
    private const int MaxLatexChars = TectonicCompiler.MaxLatexChars;
    private const int MaxInstructionChars = 1_000;
    private const int MaxDescriptionChars = 4_000;
    private const int MaxTitleChars = 80;
    private const int MaxResumes = 20;
    private const int MaxEditAttempts = 3;

    private static readonly string[] AreaNames = ["ATS parseability", "Impact and metrics", "Role keywords", "Structure", "Clarity"];
    private static readonly string[] Severities = ["High", "Medium", "Low"];

    public async Task<IReadOnlyList<ResumeSummaryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var rows = await db.StudentResumes
            .Where(r => r.StudentProfileId == profile.Id)
            .OrderByDescending(r => r.UpdatedAtUtc)
            .Select(r => new { r.Id, r.Title, r.Template, r.Source, r.Score, r.AnalysedAtUtc, r.CreatedAtUtc, r.UpdatedAtUtc })
            .ToListAsync(ct);

        return rows.Select(r => new ResumeSummaryDto(
            r.Id, r.Title, r.Template, r.Source, r.Score, Stale(r.AnalysedAtUtc, r.UpdatedAtUtc), Utc(r.CreatedAtUtc), Utc(r.UpdatedAtUtc))).ToList();
    }

    public async Task<ResumeDto?> GetAsync(int id, CancellationToken ct = default) =>
        await LoadAsync(id, ct) is { } resume ? Map(resume) : null;

    public async Task<ResumeDto> ImportPdfAsync(string fileName, byte[] pdfBytes, string template, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        await EnsureRoomAsync(profile, ct);

        if (pdfBytes.Length > MaxPdfBytes)
        {
            throw new ResumeProblemException("That file is over 5 MB. Upload a smaller PDF.");
        }

        if (pdfBytes.Length < 5 || Encoding.ASCII.GetString(pdfBytes, 0, 5) != "%PDF-")
        {
            throw new ResumeProblemException("That file isn't a PDF. Export your resume as a PDF and try again.");
        }

        var text = pdf.Extract(pdfBytes);
        if (text.Length < 50)
        {
            throw new ResumeProblemException(
                "We couldn't read any text from that PDF. It may be a scanned image. Export it as a text PDF, or build one from scratch.");
        }

        text = Truncate(text, MaxSourceChars);
        var facts = ProfileText.Facts(profile);
        var role = await TargetRoleAsync(profile, ct);

        // Both calls work only on text, never on the DbContext, so they can run side by side.
        var extractTask = ExtractAsync(Prompts.ResumeExtract(text, facts), ct);
        var analyseTask = TryAnalyseAsync(text, role, facts, ct);
        await Task.WhenAll(extractTask, analyseTask);

        var data = FillContact(Normalise(extractTask.Result), profile);
        template = ValidTemplate(template);
        var now = DateTime.UtcNow;
        var resume = new StudentResume
        {
            StudentProfileId = profile.Id,
            Title = Truncate(Path.GetFileNameWithoutExtension(fileName).Trim() is { Length: > 0 } stem ? stem : "Uploaded resume", MaxTitleChars),
            Template = template,
            Source = "upload",
            SourceFileName = Truncate(Path.GetFileName(fileName), 200),
            SourceText = text,
            Latex = LatexTemplates.Render(template, data),
            DataJson = JsonOutput.Serialize(data),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        if (analyseTask.Result is { } analysis)
        {
            SetAnalysis(resume, analysis, now);
        }

        db.StudentResumes.Add(resume);
        await db.SaveChangesAsync(ct);
        return Map(resume);
    }

    public async Task<ResumeDataDto> GetStarterDataAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var (course, branch) = CourseCatalog.Parse(profile.Branch);
        var education = string.IsNullOrWhiteSpace(profile.College)
            ? new List<ResumeEducationDto>()
            :
            [
                new(profile.College, course is CourseCatalog.OtherCourse ? "" : course,
                    branch is CourseCatalog.GeneralBranch ? "" : branch, "",
                    profile.GraduationYear > 0 ? profile.GraduationYear.ToString(CultureInfo.InvariantCulture) : "",
                    profile.Cgpa > 0 ? string.Create(CultureInfo.InvariantCulture, $"CGPA {profile.Cgpa:0.0#}/10") : "", []),
            ];

        var skills = profile.Skills
            .Where(s => s.Skill is not null)
            .GroupBy(s => s.Skill!.Category)
            .Select(g => new ResumeSkillGroupDto(g.Key, g.Select(s => s.Skill!.Name).ToList()))
            .ToList();

        return new ResumeDataDto(
            Contact(profile), "", education, [], [], skills, []);
    }

    public async Task<ResumeDataDto> DraftFromDescriptionAsync(DescribeYourselfRequest request, CancellationToken ct = default)
    {
        var description = request.Description?.Trim() ?? "";
        if (description.Length < 20)
        {
            throw new ResumeProblemException("Write a few sentences about yourself first: your course, projects, internships and skills.");
        }

        var profile = await ctx.GetProfileAsync(ct);
        var parsed = await ExtractAsync(Prompts.ResumeDraft(Truncate(description, MaxDescriptionChars), ProfileText.Facts(profile)), ct);
        return FillContact(Normalise(parsed), profile);
    }

    public async Task<ResumeDto> CreateFromDataAsync(CreateResumeRequest request, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        await EnsureRoomAsync(profile, ct);

        var data = Normalise(ToModel(request.Data));
        if (string.IsNullOrWhiteSpace(data.Contact.FullName))
        {
            throw new ResumeProblemException("Add your name before creating the resume.");
        }

        var template = ValidTemplate(request.Template);
        var now = DateTime.UtcNow;
        var resume = new StudentResume
        {
            StudentProfileId = profile.Id,
            Title = Title(request.Title),
            Template = template,
            Source = request.Source == "prompt" ? "prompt" : "wizard",
            Latex = LatexTemplates.Render(template, data),
            DataJson = JsonOutput.Serialize(data),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.StudentResumes.Add(resume);
        await db.SaveChangesAsync(ct);
        return Map(resume);
    }

    public async Task<ResumeDto> SaveAsync(int id, SaveResumeRequest request, CancellationToken ct = default)
    {
        var resume = await RequireAsync(id, ct);
        await ApplySaveAsync(resume, request, ct);
        return Map(resume);
    }

    public async Task<ResumeDto> AnalyseAsync(int id, SaveResumeRequest request, CancellationToken ct = default)
    {
        var resume = await RequireAsync(id, ct);
        await ApplySaveAsync(resume, request, ct);

        var profile = await ctx.GetProfileAsync(ct);
        var analysis = await TryAnalyseAsync(resume.Latex, await TargetRoleAsync(profile, ct), ProfileText.Facts(profile), ct)
            ?? throw new ResumeProblemException("The AI reviewer couldn't read this resume this time. Try again.", retryable: true);

        SetAnalysis(resume, analysis, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return Map(resume);
    }

    public async Task<ResumeEditResultDto> EditAsync(int id, ResumeInstructionRequest request, CancellationToken ct = default)
    {
        var instruction = request.Instruction?.Trim() ?? "";
        if (instruction.Length == 0)
        {
            throw new ResumeProblemException("Type what you'd like changed first.");
        }

        var resume = await RequireAsync(id, ct);
        await ApplySaveAsync(resume, new SaveResumeRequest(request.Latex), ct);

        var (latex, note) = await RunEditAsync(resume.Latex, Truncate(instruction, MaxInstructionChars), ct);
        SetLatex(resume, latex);
        await db.SaveChangesAsync(ct);
        return new ResumeEditResultDto(Map(resume), note);
    }

    public async Task<ResumeEditResultDto> ApplySuggestionAsync(int id, int suggestionId, SaveResumeRequest request, CancellationToken ct = default)
    {
        var resume = await RequireAsync(id, ct);
        var analysis = ReadAnalysis(resume)
            ?? throw new ResumeProblemException("This resume hasn't been reviewed yet. Check its score first.");
        var suggestion = analysis.Suggestions.FirstOrDefault(s => s.Id == suggestionId)
            ?? throw new ResumeProblemException("That suggestion no longer exists. Check the score again for fresh ones.");

        await ApplySaveAsync(resume, request, ct);

        var instruction = $"Apply this review suggestion to the {suggestion.Section} section. Issue: {suggestion.Issue} Fix: {suggestion.Fix}";
        var (latex, note) = await RunEditAsync(resume.Latex, instruction, ct);
        SetLatex(resume, latex);

        // The suggestion is marked applied without touching AnalysedAtUtc, so the score shows as out of date.
        var updated = analysis with
        {
            Suggestions = analysis.Suggestions.Select(s => s.Id == suggestionId ? s with { Applied = true } : s).ToList(),
        };
        resume.AnalysisJson = JsonOutput.Serialize(updated);

        await db.SaveChangesAsync(ct);
        return new ResumeEditResultDto(Map(resume), note);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var resume = await LoadAsync(id, ct);
        if (resume is not null)
        {
            db.StudentResumes.Remove(resume);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<ResumeCompileResultDto> CompileAsync(string latex, CancellationToken ct = default)
    {
        var result = await compiler.CompileAsync(latex ?? "", ct);
        return new ResumeCompileResultDto(result.Ok, result.Pdf, result.Log, compiler.IsAvailable);
    }

    // ── AI calls ────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<ModelData> ExtractAsync(string prompt, CancellationToken ct)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, Prompts.System), new(ChatRole.User, prompt) };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var text = await AskAsync(messages, 0.2f, 4000, JsonOutput.Reasoning.Low, ct);
            if (JsonOutput.TryParse<ModelData>(text, out var parsed))
            {
                return parsed;
            }

            log.LogWarning("Resume extraction returned unreadable JSON (attempt {Attempt}).", attempt + 1);
        }

        throw new ResumeProblemException("The AI couldn't turn this into resume sections this time. Try again.", retryable: true);
    }

    /// <summary>Null when the model's answer can't be read; the resume is still usable without a score.</summary>
    private async Task<ResumeAnalysisDto?> TryAnalyseAsync(string content, string role, string facts, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.System),
            new(ChatRole.User, Prompts.ResumeAnalyse(content, role, facts)),
        };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var text = await AskAsync(messages, 0.2f, 4000, JsonOutput.Reasoning.Medium, ct);
            if (JsonOutput.TryParse<ModelAnalysis>(text, out var parsed) && parsed.Suggestions is { Count: > 0 })
            {
                return Normalise(parsed);
            }

            log.LogWarning("Resume analysis returned unreadable JSON (attempt {Attempt}).", attempt + 1);
        }

        return null;
    }

    private async Task<(string Latex, string Note)> RunEditAsync(string latex, string instruction, CancellationToken ct)
    {
        // A stray & or _ left by an earlier edit would make every later edit fail to compile too.
        latex = LatexText.EscapeStrayCharacters(latex);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.System),
            new(ChatRole.User, Prompts.ResumeEdit(latex, instruction)),
        };

        (string Latex, string Note)? broken = null;
        for (var attempt = 0; attempt < MaxEditAttempts; attempt++)
        {
            var text = await AskAsync(messages, 0.2f, 8000, JsonOutput.Reasoning.Low, ct);
            string? candidate = null;
            string? note = null;
            if (JsonOutput.TryParse<ModelEdit>(text, out var parsed))
            {
                (candidate, note) = (parsed.Latex, parsed.Note);
            }
            else if (text is not null && WholeDocument().Match(text) is { Success: true } match)
            {
                // Models sometimes skip the JSON wrapper and return the document itself.
                candidate = match.Value;
            }

            // Some models return the document as one line with the line breaks written out as text; restore them.
            candidate = candidate is null ? null : LatexText.Unflatten(candidate);

            if (candidate is null || !IsDocument(candidate) || candidate.Length > MaxLatexChars)
            {
                log.LogWarning("Resume edit returned no usable document (attempt {Attempt}).", attempt + 1);
                continue;
            }

            var edited = (Latex: LatexText.EscapeStrayCharacters(candidate).Trim() + "\n",
                Note: string.IsNullOrWhiteSpace(note) ? "Updated your resume." : note.Trim());
            if (!compiler.IsAvailable)
            {
                return edited;
            }

            var compiled = await compiler.CompileAsync(edited.Latex, ct);
            if (compiled.Ok)
            {
                return edited;
            }

            // Show the model what the compiler said and let it correct its own document.
            log.LogWarning("Resume edit did not compile (attempt {Attempt}).", attempt + 1);
            broken = edited;
            messages.Add(new(ChatRole.Assistant, text ?? ""));
            messages.Add(new(ChatRole.User, Prompts.ResumeEditFix(LatexText.CompileErrors(compiled.Log))));
        }

        if (broken is { } stillBroken)
        {
            // If the student's own document doesn't compile either, the edit isn't what broke it, so keep the edit.
            if (!(await compiler.CompileAsync(latex, ct)).Ok)
            {
                return stillBroken;
            }

            throw new ResumeProblemException("The AI's edit didn't compile as LaTeX, so nothing was changed. Try again, or word the request differently.", retryable: true);
        }

        throw new ResumeProblemException("The AI's edit didn't come back as a complete document. Nothing was changed; try again.", retryable: true);
    }

    private async Task<string?> AskAsync(List<ChatMessage> messages, float temperature, int maxTokens, JsonOutput.Reasoning reasoning, CancellationToken ct)
    {
        try
        {
            return await JsonOutput.AskAsync(chat, messages, temperature, maxTokens, reasoning, ct);
        }
        catch (AiUnavailableException e)
        {
            throw new ResumeProblemException(e.Message, retryable: true);
        }
        catch (Exception e) when (e is HttpRequestException or System.ClientModel.ClientResultException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(e, "Resume AI call failed.");
            throw new ResumeProblemException("The AI service didn't respond. Try again in a moment.", retryable: true);
        }
    }

    // ── Persistence helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<StudentResume?> LoadAsync(int id, CancellationToken ct)
    {
        var profile = await ctx.GetProfileAsync(ct);
        return await db.StudentResumes.FirstOrDefaultAsync(r => r.Id == id && r.StudentProfileId == profile.Id, ct);
    }

    private async Task<StudentResume> RequireAsync(int id, CancellationToken ct) =>
        await LoadAsync(id, ct) ?? throw new ResumeProblemException("That resume doesn't exist.");

    private async Task EnsureRoomAsync(StudentProfile profile, CancellationToken ct)
    {
        if (await db.StudentResumes.CountAsync(r => r.StudentProfileId == profile.Id, ct) >= MaxResumes)
        {
            throw new ResumeProblemException($"You can keep up to {MaxResumes} resumes. Delete one you no longer need first.");
        }
    }

    private async Task ApplySaveAsync(StudentResume resume, SaveResumeRequest request, CancellationToken ct)
    {
        // Also repairs a resume that an earlier AI edit left flattened, the next time it is saved.
        var latex = LatexText.Unflatten(request.Latex ?? "");
        if (!IsDocument(latex))
        {
            throw new ResumeProblemException("The LaTeX needs a \\begin{document} and \\end{document}. Undo your last change or fix it, then save.");
        }

        if (latex.Length > MaxLatexChars)
        {
            throw new ResumeProblemException($"The LaTeX is longer than {MaxLatexChars:N0} characters.");
        }

        SetLatex(resume, latex);
        if (request.Title is { } title && Title(title) != resume.Title)
        {
            resume.Title = Title(title);
            resume.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Only a real change moves UpdatedAtUtc, so saving untouched text doesn't mark the score as stale.</summary>
    private static void SetLatex(StudentResume resume, string latex)
    {
        if (!string.Equals(resume.Latex, latex, StringComparison.Ordinal))
        {
            resume.Latex = latex;
            resume.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static void SetAnalysis(StudentResume resume, ResumeAnalysisDto analysis, DateTime now)
    {
        resume.AnalysisJson = JsonOutput.Serialize(analysis);
        resume.Score = analysis.Overall;
        resume.AnalysedAtUtc = now;
        if (resume.UpdatedAtUtc > now)
        {
            resume.UpdatedAtUtc = now;
        }
    }

    private async Task<string> TargetRoleAsync(StudentProfile profile, CancellationToken ct) =>
        await db.CareerRoles.Where(r => r.Id == profile.TargetRoleId).Select(r => r.Title).FirstOrDefaultAsync(ct)
        ?? "entry-level";

    private static ResumeAnalysisDto? ReadAnalysis(StudentResume r) =>
        r.AnalysisJson is { } json && JsonOutput.TryParse<ResumeAnalysisDto>(json, out var a) ? a : null;

    private static ResumeDto Map(StudentResume r)
    {
        ResumeDataDto? data = null;
        if (r.DataJson is { } json && JsonOutput.TryParse<ModelData>(json, out var parsed))
        {
            data = Normalise(parsed);
        }

        return new ResumeDto(
            r.Id, r.Title, r.Template, r.Source, r.SourceFileName, r.Latex, data, ReadAnalysis(r),
            Stale(r.AnalysedAtUtc, r.UpdatedAtUtc), Utc(r.CreatedAtUtc), Utc(r.UpdatedAtUtc));
    }

    private static bool Stale(DateTime? analysedAt, DateTime updatedAt) => analysedAt is { } at && at < updatedAt;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsDocument(string latex) =>
        latex.Contains("\\begin{document}", StringComparison.Ordinal) && latex.Contains("\\end{document}", StringComparison.Ordinal);

    private static string ValidTemplate(string? template) =>
        ResumeTemplates.All.Any(t => t.Key == template) ? template! : ResumeTemplates.SingleColumn;

    private static string Title(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "My resume" : Truncate(title.Trim(), MaxTitleChars);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static ResumeContactDto Contact(StudentProfile p) => new(
        p.User?.FullName ?? "",
        p.User?.Email ?? "",
        "",
        string.Join(", ", new[] { p.City, p.State }.Where(s => !string.IsNullOrWhiteSpace(s))),
        "", "", "");

    /// <summary>Name, email and location come from the profile only where the source left them empty.</summary>
    private static ResumeDataDto FillContact(ResumeDataDto data, StudentProfile profile)
    {
        var fallback = Contact(profile);
        var c = data.Contact;
        return data with
        {
            Contact = c with
            {
                FullName = Pick(c.FullName, fallback.FullName),
                Email = Pick(c.Email, fallback.Email),
                Location = Pick(c.Location, fallback.Location),
            },
        };

        static string Pick(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    // ── Normalisers: the model's JSON and the wizard's form both arrive with gaps, nulls and oversized lists ──

    private static ResumeDataDto Normalise(ModelData d)
    {
        var c = d.Contact ?? new ModelContact(null, null, null, null, null, null, null);
        return new ResumeDataDto(
            new ResumeContactDto(S(c.FullName, 80), S(c.Email, 120), S(c.Phone, 40), S(c.Location, 80), S(c.LinkedIn, 200), S(c.GitHub, 200), S(c.Website, 200)),
            S(d.Summary, 800),
            Take(d.Education, 6, e => new ResumeEducationDto(S(e.Institution, 150), S(e.Degree, 80), S(e.Field, 100), S(e.Start, 30), S(e.End, 30), S(e.Grade, 40), Lines(e.Highlights, 4)),
                e => e.Institution.Length > 0),
            Take(d.Experience, 10, e => new ResumeExperienceDto(S(e.Organisation, 150), S(e.Role, 100), S(e.Location, 80), S(e.Start, 30), S(e.End, 30), Lines(e.Bullets, 8)),
                e => e.Organisation.Length > 0 || e.Role.Length > 0),
            Take(d.Projects, 10, p => new ResumeProjectDto(S(p.Name, 120), S(p.Link, 200), S(p.Technologies, 150), S(p.Start, 30), S(p.End, 30), Lines(p.Bullets, 8)),
                p => p.Name.Length > 0),
            Take(d.Skills, 12, g => new ResumeSkillGroupDto(S(g.Category, 40), Lines(g.Items, 30, 40)),
                g => g.Items.Count > 0),
            Take(d.Achievements, 12, a => new ResumeAchievementDto(S(a.Title, 150), S(a.Issuer, 100), S(a.Date, 30), S(a.Detail, 300)),
                a => a.Title.Length > 0));
    }

    private static ResumeAnalysisDto Normalise(ModelAnalysis m)
    {
        var scores = AreaNames.Select(area =>
        {
            var found = m.Scores?.FirstOrDefault(s => string.Equals(s.Area?.Trim(), area, StringComparison.OrdinalIgnoreCase));
            return new ResumeScoreDto(area, Math.Clamp(found?.Score ?? 0, 0, 100), S(found?.Comment, 300));
        }).Where(s => s.Comment.Length > 0 || s.Score > 0).ToList();

        var suggestions = (m.Suggestions ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Issue) && !string.IsNullOrWhiteSpace(s.Fix))
            .Take(10)
            .Select((s, i) => new ResumeSuggestionDto(
                i + 1,
                S(s.Section, 40) is { Length: > 0 } section ? section : "General",
                S(s.Issue, 500),
                S(s.Fix, 600),
                Severities.FirstOrDefault(v => string.Equals(v, s.Severity?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Medium",
                false))
            .ToList();

        return new ResumeAnalysisDto(Math.Clamp(m.Overall, 0, 100), S(m.Summary, 800), scores, suggestions);
    }

    private static ModelData ToModel(ResumeDataDto? d) =>
        d is null
            ? new ModelData(null, null, null, null, null, null, null)
            : JsonOutput.TryParse<ModelData>(JsonOutput.Serialize(d), out var m) ? m : new ModelData(null, null, null, null, null, null, null);

    private static string S(string? value, int max) => Truncate(Regex.Replace(value ?? "", @"\s+", " ").Trim(), max);

    private static List<string> Lines(List<string?>? items, int max, int maxChars = 300) =>
        (items ?? []).Select(i => S(i, maxChars).TrimStart('•', '-', '*', '·', ' ')).Where(i => i.Length > 0).Take(max).ToList();

    private static List<TOut> Take<TIn, TOut>(List<TIn?>? items, int max, Func<TIn, TOut> map, Func<TOut, bool> keep) where TIn : class =>
        (items ?? []).OfType<TIn>().Select(map).Where(keep).Take(max).ToList();

    [GeneratedRegex(@"\\documentclass.*?\\end\{document\}", RegexOptions.Singleline)]
    private static partial Regex WholeDocument();

    // What the model returns. Everything is nullable because open models skip fields.
    private sealed record ModelData(
        ModelContact? Contact, string? Summary, List<ModelEducation?>? Education, List<ModelExperience?>? Experience,
        List<ModelProject?>? Projects, List<ModelSkillGroup?>? Skills, List<ModelAchievement?>? Achievements);

    private sealed record ModelContact(string? FullName, string? Email, string? Phone, string? Location, string? LinkedIn, string? GitHub, string? Website);

    private sealed record ModelEducation(string? Institution, string? Degree, string? Field, string? Start, string? End, string? Grade, List<string?>? Highlights);

    private sealed record ModelExperience(string? Organisation, string? Role, string? Location, string? Start, string? End, List<string?>? Bullets);

    private sealed record ModelProject(string? Name, string? Link, string? Technologies, string? Start, string? End, List<string?>? Bullets);

    private sealed record ModelSkillGroup(string? Category, List<string?>? Items);

    private sealed record ModelAchievement(string? Title, string? Issuer, string? Date, string? Detail);

    private sealed record ModelAnalysis(int Overall, string? Summary, List<ModelScore>? Scores, List<ModelSuggestion>? Suggestions);

    private sealed record ModelScore(string? Area, int? Score, string? Comment);

    private sealed record ModelSuggestion(string? Section, string? Issue, string? Fix, string? Severity);

    private sealed record ModelEdit(string? Latex, string? Note);
}
