using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sapling.Shared.Content;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>
/// Career paths from the O*NET-based catalogue, scored for the signed-in student. Fit is 75% skills (the share
/// of the role's requirements the student has, weighted by O*NET importance), 10% how their interest quiz lines
/// up with O*NET's interest profile, and 15% whether their course is a usual route into the role.
/// </summary>
public sealed class CareerService(SaplingDbContext db, StudentContext ctx, CareerCatalogueFile catalogue) : ICareerService
{
    private const double SkillsWeight = 0.75;
    private const double InterestsWeight = 0.10;
    private const double CourseWeight = 0.15;

    private static readonly Dictionary<char, string> RiasecLetters = new()
    {
        ['R'] = "Realistic",
        ['I'] = "Investigative",
        ['A'] = "Artistic",
        ['S'] = "Social",
        ['E'] = "Enterprising",
        ['C'] = "Conventional",
    };

    public async Task<IReadOnlyList<CareerPathSummaryDto>> GetPathsAsync(bool all = false, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var roles = await LoadRolesAsync(ct);
        var student = Student.From(profile, catalogue.KnowledgeEvidence);

        return roles
            .Select(r => Score(r, student))
            .Where(s => all || student.Course == "" || s.Relevance is not null)
            .OrderByDescending(s => s.Fit)
            .ThenBy(s => CareerCatalogueFile.Rank(s.Relevance))
            .ThenBy(s => s.Role.Title)
            .Select(s => new CareerPathSummaryDto(
                s.Role.Id, s.Role.Title, s.Role.Family, Tier(s.Fit), s.Fit, s.Relevance,
                Reasons(s, student)[0], s.Role.Id == profile.TargetRoleId, Split(s.Role.Outlook)))
            .ToList();
    }

    public async Task<CareerPathDto?> GetPathAsync(int id, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var roles = await LoadRolesAsync(ct);
        var role = roles.FirstOrDefault(r => r.Id == id);
        if (role is null)
        {
            return null;
        }

        var student = Student.From(profile, catalogue.KnowledgeEvidence);
        var scored = Score(role, student);
        var byCode = roles.ToDictionary(r => r.OnetCode);
        var source = catalogue.Source;

        return new CareerPathDto(
            role.Id,
            role.OnetCode,
            role.Title,
            role.OnetTitle,
            role.Family,
            Tier(scored.Fit),
            scored.Fit,
            scored.Have,
            scored.Total,
            new FitBreakdownDto(scored.Skills, Percent(scored.Interests), Percent(scored.Course)),
            scored.Relevance,
            role.Id == profile.TargetRoleId,
            role.Description,
            Split(role.Tasks),
            Split(role.AlsoCalled),
            Split(role.Technologies),
            Split(role.Outlook),
            role.Preparation,
            role.Education,
            Reasons(scored, student),
            Considerations(scored, student),
            role.Requirements
                .OrderBy(r => student.Has(r))
                .ThenBy(r => KindOrder(r.Kind))
                .ThenByDescending(r => r.Impact)
                .Select(r => new CareerRequirementDto(r.Skill?.Name ?? "", r.Kind, r.Impact, student.Has(r), r.Rationale))
                .ToList(),
            Split(role.RelatedCodes)
                .Where(byCode.ContainsKey)
                .Select(c => new RelatedCareerDto(byCode[c].Id, byCode[c].Title))
                .ToList(),
            new CareerSourceDto(
                source.Name,
                source.Publisher,
                source.Url,
                source.License,
                source.LicenseUrl,
                $"https://www.onetonline.org/link/summary/{role.OnetCode}",
                source.ImportedOn,
                source.OutlookSource,
                catalogue.CourseMapping));
    }

    public async Task<CareerPathDto?> SetTargetAsync(int id, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        if (!await db.CareerRoles.AnyAsync(r => r.Id == id, ct))
        {
            return null;
        }

        profile.TargetRoleId = id;
        profile.TargetChosen = true;
        await db.SaveChangesAsync(ct);
        return await GetPathAsync(id, ct);
    }

    private Task<List<CareerRole>> LoadRolesAsync(CancellationToken ct) => db.CareerRoles
        .AsNoTracking()
        .Include(r => r.Requirements).ThenInclude(q => q.Skill)
        .Include(r => r.CourseLinks)
        .AsSplitQuery()
        .ToListAsync(ct);

    /// <summary>Importance-weighted share of the requirements the student has, from 0 to 100.</summary>
    internal static int Fit(IReadOnlyCollection<RoleSkillRequirement> requirements, Func<RoleSkillRequirement, bool> has)
    {
        var total = requirements.Sum(r => r.Impact);
        return total == 0 ? 0 : (int)Math.Round(100.0 * requirements.Where(has).Sum(r => r.Impact) / total);
    }

    private static Scored Score(CareerRole role, Student student)
    {
        var ratings = ParseInterests(role.Interests);
        double? interests = null;
        if (student.Riasec.Length > 0 && ratings.Count > 0)
        {
            double sum = 0, weights = 0;
            for (var i = 0; i < Math.Min(3, student.Riasec.Length); i++)
            {
                if (RiasecLetters.TryGetValue(student.Riasec[i], out var name) && ratings.TryGetValue(name, out var rating))
                {
                    var weight = 3 - i;
                    sum += weight * (rating - 1) / 6;
                    weights += weight;
                }
            }

            interests = weights == 0 ? null : sum / weights;
        }

        var skills = Fit(role.Requirements, student.Has);
        var relevance = Relevance(role, student);
        double? course = student.Course == "" ? null : relevance switch
        {
            CareerCatalogueFile.Primary or CareerCatalogueFile.Core => 1.0,
            CareerCatalogueFile.Adjacent => 0.6,
            _ => 0.2,
        };

        // A part the student has not filled in yet (quiz, course) drops out rather than counting as zero.
        var parts = new List<(double Value, double Weight)> { (skills / 100.0, SkillsWeight) };
        if (interests is { } interestPart)
        {
            parts.Add((interestPart, InterestsWeight));
        }

        if (course is { } coursePart)
        {
            parts.Add((coursePart, CourseWeight));
        }

        var fit = (int)Math.Round(100 * parts.Sum(p => p.Value * p.Weight) / parts.Sum(p => p.Weight));
        return new Scored(
            role,
            fit,
            skills,
            role.Requirements.Count(student.Has),
            role.Requirements.Count,
            interests,
            course,
            relevance,
            ratings);
    }

    private static string? Relevance(CareerRole role, Student student) => role.CourseLinks
        .Where(l => string.Equals(l.Course, student.Course, StringComparison.OrdinalIgnoreCase)
                    && (l.Branch == "*" || string.Equals(l.Branch, student.Branch, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(l => l.Branch == "*")
        .ThenBy(l => CareerCatalogueFile.Rank(l.Relevance))
        .Select(l => l.Relevance)
        .FirstOrDefault();

    private static List<string> Reasons(Scored s, Student student)
    {
        var covered = s.Role.Requirements
            .Where(student.Has)
            .OrderByDescending(r => r.Impact)
            .Select(r => r.Skill?.Name ?? "")
            .ToList();

        var reasons = new List<string>
        {
            covered.Count == 0
                ? $"None of the {s.Total} things this role asks for are on your profile yet. Add the ones you have."
                : $"You have {covered.Count} of the {s.Total} things this role asks for: {string.Join(", ", covered.Take(4))}{(covered.Count > 4 ? " and more" : "")}.",
        };

        var from = student.Branch is "" or CourseCatalog.GeneralBranch or "Other" || student.Branch == student.Course
            ? student.Course
            : $"{student.Course} {student.Branch}";

        reasons.Add(s.Relevance switch
        {
            _ when student.Course == "" => "Add your course to your profile to see how directly it leads here.",
            CareerCatalogueFile.Primary or CareerCatalogueFile.Core => $"A direct route from {from}.",
            CareerCatalogueFile.Adjacent => $"An adjacent route from {from}: open to you, though graduates of the core branches start with an edge.",
            _ => $"Not a usual route from {from}. Possible, but you would need to show the skills through projects or certificates.",
        });

        if (student.Riasec.Length > 0 && RiasecLetters.TryGetValue(student.Riasec[0], out var top) && s.Ratings.TryGetValue(top, out var rating))
        {
            reasons.Add($"Your interest quiz leans {top}; Sapling rates this role {rating.ToString("0.0", CultureInfo.InvariantCulture)} out of 7 for {top} interests.");
        }

        return reasons;
    }

    private static List<string> Considerations(Scored s, Student student)
    {
        var notes = new List<string>();

        var gaps = s.Role.Requirements
            .Where(r => !student.Has(r))
            .OrderByDescending(r => r.Impact)
            .Select(r => r.Skill?.Name ?? "")
            .Take(3)
            .ToList();
        if (gaps.Count > 0)
        {
            notes.Add($"The most important things you don't have yet are {string.Join(", ", gaps)}.");
        }

        if (s.Role.JobZone >= 5)
        {
            notes.Add("O*NET places this role in Job Zone 5: most people in it hold a master's degree or a PhD, so plan for further study.");
        }

        if (s.Interests is < 0.4 && s.Ratings.Count > 0)
        {
            var strongest = s.Ratings.OrderByDescending(p => p.Value).Take(2).Select(p => p.Key);
            notes.Add($"Your quiz answers point away from this kind of work. O*NET's interest profile for it is mostly {string.Join(" and ", strongest)}.");
        }

        if (s.Relevance == CareerCatalogueFile.Adjacent)
        {
            notes.Add("Campus drives for this role often shortlist core-branch students first, so off-campus applications and a portfolio matter more.");
        }

        if (notes.Count == 0)
        {
            notes.Add("Nothing major stands against this path for your profile right now.");
        }

        return notes;
    }

    private static int? Percent(double? value) => value is { } v ? (int)Math.Round(v * 100) : null;

    private static string Tier(int fit) => fit >= 65 ? "Safe" : fit >= 45 ? "Stretch" : "Aspirational";

    private static int KindOrder(string kind) => kind switch
    {
        "Technology" => 0,
        "Knowledge" => 1,
        _ => 2,
    };

    private static Dictionary<string, double> ParseInterests(string value) => Split(value)
        .Select(p => p.Split(':'))
        .Where(p => p.Length == 2)
        .ToDictionary(p => p[0], p => double.Parse(p[1], CultureInfo.InvariantCulture));

    internal static string[] Split(string value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split('|', StringSplitOptions.RemoveEmptyEntries);

    private sealed record Scored(
        CareerRole Role, int Fit, int Skills, int Have, int Total, double? Interests, double? Course, string? Relevance,
        Dictionary<string, double> Ratings);

    private sealed record Student(string Course, string Branch, string Riasec, StudentSkillSet Skills)
    {
        public static Student From(StudentProfile profile, IReadOnlyDictionary<string, string[]> evidence)
        {
            var (course, branch) = CourseCatalog.Parse(profile.Branch);
            return new Student(course, branch, (profile.RiasecCode ?? "").ToUpperInvariant(), new StudentSkillSet(profile, evidence));
        }

        public bool Has(RoleSkillRequirement requirement) => Skills.Has(requirement.SkillId, requirement.Skill?.Name);
    }
}

public sealed class SkillGapService(SaplingDbContext db, StudentContext ctx, CareerCatalogueFile catalogue, ICareerService careers) : ISkillGapService
{
    /// <summary>Every requirement of the target role: missing ones first, the most important at the top.</summary>
    public async Task<IReadOnlyList<SkillGapDto>> GetGapsAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var skills = new StudentSkillSet(profile, catalogue.KnowledgeEvidence);
        var requirements = await RequirementsAsync(profile.TargetRoleId, ct);

        return requirements
            .Select(r =>
            {
                var have = skills.Has(r.SkillId, r.Skill?.Name);
                return new SkillGapDto(
                    r.SkillId,
                    r.Skill?.Name ?? "",
                    r.Kind,
                    Severity(have, r.Impact),
                    r.Impact,
                    r.Effort,
                    have ? 0 : r.WeeksToClose,
                    have,
                    r.Rationale);
            })
            .OrderBy(g => g.Have)
            .ThenByDescending(g => g.Impact)
            .ThenBy(g => g.Effort)
            .ToList();
    }

    public async Task<SkillCoverageDto> GetCoverageAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var path = await careers.GetPathAsync(profile.TargetRoleId, ct)
                   ?? throw new InvalidOperationException("The student's target role no longer exists.");
        return new SkillCoverageDto(path.Id, path.Title, path.FitScore, path.Have, path.Total, profile.TargetChosen);
    }

    private Task<List<RoleSkillRequirement>> RequirementsAsync(int roleId, CancellationToken ct) => db.RoleSkillRequirements
        .Include(r => r.Skill)
        .Where(r => r.CareerRoleId == roleId)
        .ToListAsync(ct);

    // Severity follows O*NET importance: impact 9-10 is something the role cannot do without.
    private static string Severity(bool have, int impact) => have ? "Covered"
        : impact >= 9 ? "Critical"
        : impact >= 7 ? "Important"
        : "Nice to have";
}
