using Microsoft.EntityFrameworkCore;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

public sealed class ProfileService(
    SaplingDbContext db,
    StudentContext ctx,
    ICollegeCatalogService catalog,
    CareerCatalogueFile careers,
    Microsoft.AspNetCore.Identity.UserManager<AppUser> users) : IProfileService
{
    public async Task DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default)
    {
        if (!string.Equals(request.Confirmation?.Trim(), DeleteAccountRequest.Word, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Type {DeleteAccountRequest.Word} to confirm.");
        }

        var profile = await ctx.GetProfileAsync(ct);
        var user = await users.FindByIdAsync(profile.UserId)
            ?? throw new InvalidOperationException("That account no longer exists.");

        // Bookmarks of student-space messages have no link to the profile, so they are cleared by hand:
        // this student's own bookmarks, and other students' bookmarks of this student's messages.
        var messageIds = db.StudentMessages.Where(m => m.StudentProfileId == profile.Id).Select(m => m.Id);
        await db.SavedStudentMessages
            .Where(s => s.StudentProfileId == profile.Id || messageIds.Contains(s.StudentMessageId))
            .ExecuteDeleteAsync(ct);

        // Deleting the login takes the profile with it, and the profile takes its skills, roadmap progress,
        // resumes, interviews, applications, comments, upvotes, saved posts and student-space messages.
        var deleted = await users.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", deleted.Errors.Select(e => e.Description)));
        }
    }

    public async Task<StudentProfileDto> GetAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        return Map(profile);
    }

    public async Task<StudentProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        if (!string.Equals(profile.Branch, request.Branch, StringComparison.Ordinal))
        {
            await RetargetForCourseAsync(profile, request.Branch, ct);
        }

        profile.College = request.College;
        profile.Branch = request.Branch;
        profile.GraduationYear = request.GraduationYear;
        profile.State = request.State;
        profile.City = request.City;
        profile.Cgpa = request.Cgpa;
        profile.Backlogs = request.Backlogs;
        profile.PreferredLanguage = request.PreferredLanguage;

        if (profile.User is not null)
        {
            profile.User.FullName = request.FullName;
        }

        await db.SaveChangesAsync(ct);
        return Map(profile);
    }

    /// <summary>A target role survives a change of course while it still leads somewhere from the new course.</summary>
    private async Task RetargetForCourseAsync(StudentProfile profile, string newBranch, CancellationToken ct)
    {
        var (course, branch) = Shared.Content.CourseCatalog.Parse(newBranch);
        var targetCode = await db.CareerRoles
            .Where(r => r.Id == profile.TargetRoleId)
            .Select(r => r.OnetCode)
            .FirstOrDefaultAsync(ct);

        var current = careers.Roles.FirstOrDefault(r => r.Onet == targetCode);
        if (current is not null && CareerCatalogueFile.RelevanceFor(current, course, branch) is not null)
        {
            return;
        }

        var code = careers.DefaultRoleFor(newBranch).Onet;
        profile.TargetRoleId = await db.CareerRoles.Where(r => r.OnetCode == code).Select(r => r.Id).FirstAsync(ct);
    }

    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        return profile.Skills
            .OrderBy(s => s.Skill?.Name)
            .Select(s => new SkillDto(s.SkillId, s.Skill?.Name ?? "", s.Skill?.Category ?? ""))
            .ToList();
    }

    public async Task<IReadOnlyList<SkillDto>> SetClaimedSkillsAsync(IReadOnlyList<string> skillNames, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var wanted = await db.Skills.Where(s => skillNames.Contains(s.Name)).ToListAsync(ct);

        var removable = profile.Skills.Where(s => !wanted.Any(w => w.Id == s.SkillId)).ToList();
        db.StudentSkills.RemoveRange(removable);
        foreach (var item in removable)
        {
            profile.Skills.Remove(item);
        }

        foreach (var skill in wanted.Where(w => profile.Skills.All(s => s.SkillId != w.Id)))
        {
            profile.Skills.Add(new StudentSkill { SkillId = skill.Id, Skill = skill });
        }

        await db.SaveChangesAsync(ct);
        return await GetSkillsAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetSkillSuggestionsAsync(CancellationToken ct = default) =>
        await db.Skills.OrderBy(s => s.Category).ThenBy(s => s.Name).Select(s => s.Name).ToListAsync(ct);

    private const int RecommendedCount = 16;

    /// <summary>
    /// The skills most asked for across the roles the student's course leads to, weighted by how directly it leads
    /// there and by O*NET importance, then the everyday skills. Search covers everything else.
    /// </summary>
    public async Task<SkillPickerDto> GetSkillPickerAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var (course, branch) = Shared.Content.CourseCatalog.Parse(profile.Branch);
        var vague = careers.VagueKnowledge.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var everyday = careers.EverydaySkills.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in careers.Roles)
        {
            double routeWeight = course == "" ? 1 : CareerCatalogueFile.RelevanceFor(role, course, branch) switch
            {
                CareerCatalogueFile.Primary => 4,
                CareerCatalogueFile.Core => 3,

                // Adjacent roles only break ties; counted fully they pull civil tools into B.Sc Chemistry.
                CareerCatalogueFile.Adjacent => 0.25,
                _ => 0,
            };

            foreach (var requirement in role.Requirements.Where(r => routeWeight > 0 && !vague.Contains(r.Skill) && !everyday.Contains(r.Skill)))
            {
                weights[requirement.Skill] = weights.GetValueOrDefault(requirement.Skill) + routeWeight * requirement.Impact;
                if (routeWeight >= 1)
                {
                    direct.Add(requirement.Skill);
                }
            }
        }

        // Syllabus basics first (Data structures, Tally...), then what the course's roles ask for most.
        var basics = careers.CourseBasics
            .Where(p => p.Key.Split('/') is [var c, var b]
                        && string.Equals(c, course, StringComparison.OrdinalIgnoreCase)
                        && (b == "*" || string.Equals(b, branch, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(p => p.Value);

        var recommended = basics
            .Concat(weights
                // Only what a role the course leads straight into asks for; a short honest list beats padding.
                .Where(p => direct.Count == 0 || direct.Contains(p.Key))
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => p.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)

            // With no direct roles to go on the list is a loose guess, so keep it short.
            .Take(direct.Count == 0 ? RecommendedCount / 2 : RecommendedCount)
            .Concat(careers.EverydaySkills)
            .ToList();

        var label = course == "" ? null
            : branch is "" or Shared.Content.CourseCatalog.GeneralBranch or "Other" || branch == course ? course
            : $"{course} {branch}";

        return new SkillPickerDto(recommended, label);
    }

    public async Task<IReadOnlyList<SkillSuggestionDto>> GetSkillCatalogueAsync(CancellationToken ct = default) =>
        await db.Skills.OrderBy(s => s.Category).ThenBy(s => s.Name)
            .Select(s => new SkillSuggestionDto(s.Name, s.Category)).ToListAsync(ct);

    public Task<IReadOnlyList<CollegeDto>> SearchCollegesAsync(string query, string? state = null, CancellationToken ct = default)
    {
        var results = catalog.Search(query, state, limit: 20);
        return Task.FromResult(results);
    }

    public async Task CompleteOnboardingAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        profile.OnboardingComplete = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<StudentProfileDto> SetAvatarAsync(string? dataUrl, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        profile.AvatarDataUrl = dataUrl;
        await db.SaveChangesAsync(ct);
        return Map(profile);
    }

    private static StudentProfileDto Map(StudentProfile p) => new(
        p.User?.FullName ?? "Student",
        p.User?.Email ?? "",
        p.College,
        p.Branch,
        p.GraduationYear,
        p.City,
        p.Cgpa,
        p.Backlogs,
        p.PreferredLanguage,
        p.OnboardingComplete,
        p.RiasecCode,
        p.State,
        p.AvatarDataUrl);
}
