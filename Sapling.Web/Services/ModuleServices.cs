using Microsoft.EntityFrameworkCore;
using Sapling.Shared.Content;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

public sealed class RoadmapService(SaplingDbContext db, StudentContext ctx, CareerCatalogueFile catalogue) : IRoadmapService
{
    private const string Direct = "direct";
    private const string Nptel = "NPTEL";

    public async Task<RoadmapDto> GetAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        return await BuildAsync(profile, ct);
    }

    public async Task<RoadmapDto> SetCheckpointAsync(int checkpointId, bool done, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var checkpoint = await db.CourseCheckpoints.FirstOrDefaultAsync(c => c.Id == checkpointId, ct);
        if (checkpoint is null)
        {
            return await BuildAsync(profile, ct);
        }

        var existing = await db.CheckpointProgress
            .FirstOrDefaultAsync(p => p.StudentProfileId == profile.Id && p.CourseCheckpointId == checkpointId, ct);

        if (done && existing is null)
        {
            db.CheckpointProgress.Add(new CheckpointProgress { StudentProfileId = profile.Id, CourseCheckpointId = checkpointId });
            await db.SaveChangesAsync(ct);
            await AddSkillsIfCourseFinishedAsync(profile, checkpoint.LearningCourseId, ct);
        }
        else if (!done && existing is not null)
        {
            // Unticking never removes a skill; the student manages their skill list themselves.
            db.CheckpointProgress.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return await BuildAsync(profile, ct);
    }

    public async Task<RoadmapDto> ChooseCourseAsync(int courseId, IReadOnlyList<int> skillIds, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var offered = await db.SkillCourses
            .Where(s => s.LearningCourseId == courseId && skillIds.Contains(s.SkillId))
            .Select(s => s.SkillId)
            .ToListAsync(ct);

        var choices = await db.StudentCourseChoices
            .Where(c => c.StudentProfileId == profile.Id && offered.Contains(c.SkillId))
            .ToListAsync(ct);

        foreach (var skillId in offered)
        {
            var choice = choices.FirstOrDefault(c => c.SkillId == skillId);
            if (choice is null)
            {
                db.StudentCourseChoices.Add(new StudentCourseChoice { StudentProfileId = profile.Id, SkillId = skillId, LearningCourseId = courseId });
            }
            else
            {
                choice.LearningCourseId = courseId;
            }
        }

        await db.SaveChangesAsync(ct);
        return await BuildAsync(profile, ct);
    }

    public async Task<RoadmapDto> AddSkillAsync(int skillId, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        if (profile.Skills.All(s => s.SkillId != skillId) && await db.Skills.AnyAsync(s => s.Id == skillId, ct))
        {
            profile.Skills.Add(new StudentSkill { SkillId = skillId, Skill = await db.Skills.FindAsync([skillId], ct) });
            await db.SaveChangesAsync(ct);
        }

        return await BuildAsync(profile, ct);
    }

    /// <summary>A finished course that teaches a skill directly counts as having that skill.</summary>
    private async Task AddSkillsIfCourseFinishedAsync(StudentProfile profile, int courseId, CancellationToken ct)
    {
        var checkpointIds = await db.CourseCheckpoints.Where(c => c.LearningCourseId == courseId).Select(c => c.Id).ToListAsync(ct);
        var ticked = await db.CheckpointProgress.CountAsync(p => p.StudentProfileId == profile.Id && checkpointIds.Contains(p.CourseCheckpointId), ct);
        if (ticked < checkpointIds.Count)
        {
            return;
        }

        var taught = await db.SkillCourses
            .Where(s => s.LearningCourseId == courseId && s.Match == Direct)
            .Select(s => s.SkillId)
            .ToListAsync(ct);

        foreach (var skillId in taught.Where(id => profile.Skills.All(s => s.SkillId != id)))
        {
            profile.Skills.Add(new StudentSkill { SkillId = skillId, Skill = await db.Skills.FindAsync([skillId], ct) });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<RoadmapDto> BuildAsync(StudentProfile profile, CancellationToken ct)
    {
        var role = await db.CareerRoles.AsNoTracking().FirstAsync(r => r.Id == profile.TargetRoleId, ct);
        var requirements = await db.RoleSkillRequirements.AsNoTracking()
            .Include(r => r.Skill)
            .Where(r => r.CareerRoleId == role.Id)
            .ToListAsync(ct);

        var skillSet = new StudentSkillSet(profile, catalogue.KnowledgeEvidence);
        bool Has(RoleSkillRequirement r) => skillSet.Has(r.SkillId, r.Skill?.Name);

        var skillIds = requirements.Select(r => r.SkillId).ToList();
        var links = await db.SkillCourses.AsNoTracking().Where(s => skillIds.Contains(s.SkillId)).ToListAsync(ct);
        var courseIds = links.Select(l => l.LearningCourseId).Distinct().ToList();
        var courses = await db.LearningCourses.AsNoTracking()
            .Include(c => c.Checkpoints)
            .Where(c => courseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var allCheckpoints = courses.Values.SelectMany(c => c.Checkpoints).Select(c => c.Id).ToList();
        var done = (await db.CheckpointProgress
                .Where(p => p.StudentProfileId == profile.Id && allCheckpoints.Contains(p.CourseCheckpointId))
                .Select(p => p.CourseCheckpointId)
                .ToListAsync(ct))
            .ToHashSet();

        var choices = await db.StudentCourseChoices.AsNoTracking()
            .Where(c => c.StudentProfileId == profile.Id)
            .ToDictionaryAsync(c => c.SkillId, c => c.LearningCourseId, ct);

        RoadmapCourseDto Map(LearningCourse course, string match) => new(
            course.Id,
            course.Provider,
            course.Title,
            course.Byline,
            course.Url,
            course.Lessons,
            course.Minutes,
            match,
            course.Checkpoints
                .OrderBy(c => c.Order)
                .Select(c => new RoadmapCheckpointDto(c.Id, c.Title, c.Lessons, c.Minutes, done.Contains(c.Id)))
                .ToList());

        // Only skills some course teaches get a step. Skills offered the same set of courses share one step,
        // so Azure and AWS both pointing at Cloud Computing are ticked once.
        var steps = requirements
            .Where(r => links.Any(l => l.SkillId == r.SkillId))
            .GroupBy(r => string.Join(',', links.Where(l => l.SkillId == r.SkillId).Select(l => l.LearningCourseId).OrderBy(id => id)))
            .Select(g =>
            {
                var options = links
                    .Where(l => g.Any(r => r.SkillId == l.SkillId))
                    .GroupBy(l => l.LearningCourseId)
                    .Select(o => Map(courses[o.Key], o.Any(l => l.Match == Direct) ? Direct : "foundation"))
                    .OrderByDescending(c => c.Match == Direct)
                    .ThenByDescending(c => c.Provider == Nptel)
                    .ToList();

                // The student's own pick, else whichever they already started, else the best match.
                var chosen = g.Select(r => choices.GetValueOrDefault(r.SkillId)).FirstOrDefault(id => options.Any(o => o.Id == id));
                var selected = options.FirstOrDefault(o => o.Id == chosen)
                               ?? options.FirstOrDefault(o => o.Checkpoints.Any(c => c.Done))
                               ?? options[0];

                var have = g.All(Has);
                return new RoadmapStepDto(
                    g.Select(r => new RoadmapSkillDto(r.SkillId, r.Skill?.Name ?? "", Has(r))).ToList(),
                    have ? "Covered" : Severity(g.Max(r => r.Impact)),
                    g.Max(r => r.Impact),
                    have,
                    selected,
                    options);
            })
            .OrderBy(s => s.Have)
            .ThenByDescending(s => s.Impact)
            .ToList();

        // Progress counts only the course each step is following; a course shared by two steps counts once.
        var followed = steps.Select(s => s.Course).DistinctBy(c => c.Id).SelectMany(c => c.Checkpoints).ToList();
        return new RoadmapDto(
            role.Id,
            role.Title,
            followed.Count(c => c.Done),
            followed.Count,
            steps,
            catalogue.LearningSource);
    }

    private static string Severity(int impact) => impact >= 9 ? "Critical" : impact >= 7 ? "Important" : "Nice to have";
}

public sealed class OpportunityService(SaplingDbContext db, StudentContext ctx) : IOpportunityService
{
    public async Task<IReadOnlyList<OpportunityDto>> GetAllAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var applications = await db.OpportunityApplications
            .Where(a => a.StudentProfileId == profile.Id)
            .ToDictionaryAsync(a => a.OpportunityId, a => a.Status, ct);

        var all = await db.Opportunities.OrderByDescending(o => o.MatchScore).ToListAsync(ct);
        return all.Select(o => Map(o, applications.GetValueOrDefault(o.Id))).ToList();
    }

    public async Task<OpportunityDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null)
        {
            return null;
        }

        var status = await db.OpportunityApplications
            .Where(a => a.StudentProfileId == profile.Id && a.OpportunityId == id)
            .Select(a => a.Status)
            .FirstOrDefaultAsync(ct);

        return Map(opportunity, status);
    }

    public async Task<OpportunityDto> ApplyAsync(int id, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var existing = await db.OpportunityApplications
            .FirstOrDefaultAsync(a => a.StudentProfileId == profile.Id && a.OpportunityId == id, ct);

        if (existing is null)
        {
            db.OpportunityApplications.Add(new OpportunityApplication { StudentProfileId = profile.Id, OpportunityId = id });
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(id, ct) ?? throw new InvalidOperationException("Opportunity not found.");
    }

    // Readiness is surfaced, never used to hide a role.
    private static OpportunityDto Map(Opportunity o, string? status)
    {
        var missing = CareerService.Split(o.MissingSkills);
        var readiness = missing.Length switch
        {
            0 => "Ready to apply",
            1 => "1 skill away",
            _ => $"{missing.Length} skills away",
        };

        return new OpportunityDto(
            o.Id, o.Title, o.Company, o.Kind, o.Location, o.Remote, o.Stipend, o.ClosesOn,
            o.MatchScore, readiness, missing, CareerService.Split(o.Reasons), o.Description,
            string.IsNullOrEmpty(status) ? null : status);
    }
}

public sealed class GovtService(SaplingDbContext db, StudentContext ctx) : IGovtService
{
    public async Task<IReadOnlyList<GovtExamDto>> GetExamsAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var exams = await db.GovtExams.ToListAsync(ct);

        // Soonest first; finished exams drop off, and ones without announced dates go last.
        return exams
            .Where(e => (e.ExamEndsOn ?? e.ExamOn ?? DateOnly.MaxValue) >= today)
            .Select(e => Evaluate(e, profile))
            .OrderBy(e => e.ExamOn ?? DateOnly.MaxValue)
            .ThenBy(e => e.Name)
            .ToList();
    }

    public async Task<GovtExamDto?> GetExamAsync(int id, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var exam = await db.GovtExams.FirstOrDefaultAsync(e => e.Id == id, ct);
        return exam is null ? null : Evaluate(exam, profile);
    }

    /// <summary>Rule-based eligibility so the answer is auditable rather than generated.</summary>
    private static GovtExamDto Evaluate(GovtExam e, StudentProfile profile)
    {
        var reasons = new List<string>();
        // An exam open to final-year (or third-year) students accepts you that many years before graduating.
        var graduatesThisYear = profile.GraduationYear <= DateTime.Today.Year + e.OpenYearsBeforeGraduation;
        var status = "Eligible";

        if (e.QualificationRequired.Contains("Postgraduate", StringComparison.OrdinalIgnoreCase))
        {
            status = "Not eligible";
            reasons.Add($"Requires {e.QualificationRequired}; you are completing a bachelor's degree in {profile.GraduationYear}.");
        }
        else if (!graduatesThisYear)
        {
            status = "Eligible next year";
            reasons.Add($"Graduate qualification is required and you finish in {profile.GraduationYear}.");
        }
        else
        {
            reasons.Add($"Your qualification meets the requirement: {e.QualificationRequired}.");
        }

        if (e.RequiresMpDomicile)
        {
            var isMp = string.Equals(profile.State, "Madhya Pradesh", StringComparison.OrdinalIgnoreCase)
                       || LocationCatalog.InferStateFromCity(profile.City).Equals("Madhya Pradesh", StringComparison.OrdinalIgnoreCase);

            if (isMp)
            {
                var loc = !string.IsNullOrEmpty(profile.City) && !string.IsNullOrEmpty(profile.State)
                    ? $"{profile.City}, {profile.State}"
                    : !string.IsNullOrEmpty(profile.State) ? profile.State : profile.City;
                reasons.Add($"Madhya Pradesh domicile is confirmed (recorded location: {loc}).");
            }
            else
            {
                reasons.Add(string.IsNullOrWhiteSpace(profile.State) && string.IsNullOrWhiteSpace(profile.City)
                    ? "Madhya Pradesh domicile is required; select your state and city to confirm."
                    : $"Madhya Pradesh domicile is required and your recorded state is {(!string.IsNullOrEmpty(profile.State) ? profile.State : profile.City)}.");
            }
        }

        reasons.Add(e.MaxAge == 0
            ? "There is no upper age limit."
            : $"Age window is {e.MinAge} to {e.MaxAge} years, which a {DateTime.Today.Year} graduate normally falls inside.");

        if (e.NotificationOn is { } notified && notified <= DateOnly.FromDateTime(DateTime.Today))
        {
            reasons.Add($"Notification released on {notified:d MMM yyyy}; applications are open now.");
        }

        return new GovtExamDto(
            e.Id, e.Name, e.Authority, e.Level, e.NotificationOn, e.ExamOn,
            status, reasons, CareerService.Split(e.SyllabusAreas), e.Summary,
            e.OfficialUrl, e.ExamEndsOn, e.DatesTentative);
    }
}

public sealed class QuizService(SaplingDbContext db, StudentContext ctx) : IQuizService
{
    private static readonly string[] Dimensions =
        ["Realistic", "Investigative", "Artistic", "Social", "Enterprising", "Conventional"];

    public async Task<IReadOnlyList<QuizQuestionDto>> GetQuestionsAsync(CancellationToken ct = default) =>
        await db.QuizQuestions
            .OrderBy(q => q.Order)
            .Select(q => new QuizQuestionDto(q.Id, q.Text, q.Dimension))
            .ToListAsync(ct);

    public async Task<QuizResultDto> SubmitAsync(QuizSubmissionRequest request, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var questions = await db.QuizQuestions.ToDictionaryAsync(q => q.Id, q => q.Dimension, ct);

        var totals = Dimensions.ToDictionary(d => d, _ => 0);
        var counts = Dimensions.ToDictionary(d => d, _ => 0);

        foreach (var answer in request.Answers)
        {
            if (questions.TryGetValue(answer.QuestionId, out var dimension) && totals.ContainsKey(dimension))
            {
                totals[dimension] += answer.Value;
                counts[dimension]++;
            }
        }

        var components = Dimensions
            .Select(d => new ScoreComponentDto(
                d,
                0,
                counts[d] == 0 ? 0 : (int)Math.Round(100.0 * totals[d] / (counts[d] * 5)),
                Describe(d)))
            .OrderByDescending(c => c.Value)
            .ToList();

        var code = string.Concat(components.Take(3).Select(c => c.Name[0]));
        profile.RiasecCode = code;
        await db.SaveChangesAsync(ct);

        return new QuizResultDto(code, components);
    }

    private static string Describe(string dimension) => dimension switch
    {
        "Realistic" => "Hands-on, practical, prefers building and fixing over discussing.",
        "Investigative" => "Analytical, enjoys open problems and understanding why something works.",
        "Artistic" => "Expressive, drawn to design and to work without one correct answer.",
        "Social" => "Teaching, mentoring and collaboration energise you.",
        "Enterprising" => "Persuading, leading and owning outcomes.",
        _ => "Structured, organised, reliable with process and detail.",
    };
}
