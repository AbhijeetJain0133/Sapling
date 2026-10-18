using Microsoft.EntityFrameworkCore;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

public sealed class HomeService(SaplingDbContext db, StudentContext ctx, ICareerService careers, IRoadmapService roadmaps) : IHomeService
{
    public async Task<HomeSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var target = await careers.GetPathAsync(profile.TargetRoleId, ct);

        var roadmap = await roadmaps.GetAsync(ct);
        var checkpoints = roadmap.Steps.SelectMany(s => s.Course.Checkpoints.Select(c => (Step: s, Checkpoint: c))).ToList();
        var next = checkpoints.Where(c => !c.Checkpoint.Done).OrderBy(c => c.Step.Have).FirstOrDefault();

        var applied = await db.OpportunityApplications
            .Where(a => a.StudentProfileId == profile.Id)
            .Select(a => a.OpportunityId)
            .ToListAsync(ct);

        var newOpportunities = await db.Opportunities.CountAsync(o => !applied.Contains(o.Id), ct);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var soon = today.AddDays(30);
        var closingExams = await db.GovtExams.CountAsync(e => e.ExamOn != null && e.ExamOn <= soon && (e.ExamEndsOn ?? e.ExamOn) >= today, ct);

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var newPosts = await CommunityService.VisibleTo(db, profile).CountAsync(p => p.PostedAtUtc >= weekAgo, ct);

        var firstName = (profile.User?.FullName ?? "Student").Split(' ')[0];

        return new HomeSummaryDto(
            firstName,
            profile.OnboardingComplete,
            target?.Id,
            target?.Title,
            target?.FitScore,
            roadmap.CompletedCheckpoints,
            roadmap.TotalCheckpoints,
            next.Checkpoint is null ? null : $"{string.Join(", ", next.Step.Skills.Select(s => s.Name))}: {next.Checkpoint.Title}",
            newOpportunities,
            closingExams,
            newPosts);
    }
}
