using Microsoft.EntityFrameworkCore;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

public sealed class CommunityService(SaplingDbContext db, StudentContext ctx) : ICommunityService
{
    public const int MaxCommentLength = 1000;

    /// <summary>
    /// Each college has a private community: a student sees only posts from the institution whose name matches
    /// the college on their profile (the AICTE catalogue name picked during onboarding). Archived posts are hidden.
    /// </summary>
    public static IQueryable<CommunityPost> VisibleTo(SaplingDbContext db, StudentProfile profile)
    {
        var college = profile.College.Trim().ToLower();
        return db.CommunityPosts.Where(p => college != "" && !p.IsArchived && p.Institution!.Name.ToLower() == college);
    }

    public async Task<IReadOnlyList<CommunityPostDto>> GetFeedAsync(string? kind = null, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var query = VisibleTo(db, profile);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            query = query.Where(p => p.Kind == kind);
        }

        var ordered = query.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.PostedAtUtc);
        var rows = await Project(db, ordered, profile.Id).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<CommunityPostDto?> GetPostAsync(int id, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var row = await Project(db, VisibleTo(db, profile).Where(p => p.Id == id), profile.Id).FirstOrDefaultAsync(ct);
        return row is null ? null : Map(row);
    }

    public async Task<CommunityPostDto> SetUpvoteAsync(int id, bool upvoted, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        if (!await VisibleTo(db, profile).AnyAsync(p => p.Id == id, ct))
        {
            throw new InvalidOperationException("Post not found.");
        }

        var existing = await db.PostUpvotes
            .FirstOrDefaultAsync(u => u.CommunityPostId == id && u.StudentProfileId == profile.Id, ct);

        if (upvoted && existing is null)
        {
            db.PostUpvotes.Add(new PostUpvote { CommunityPostId = id, StudentProfileId = profile.Id });
            await db.SaveChangesAsync(ct);
        }
        else if (!upvoted && existing is not null)
        {
            db.PostUpvotes.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return (await GetPostAsync(id, ct))!;
    }

    public async Task<CommunityPostDto> SetSavedAsync(int id, bool saved, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        if (!await VisibleTo(db, profile).AnyAsync(p => p.Id == id, ct))
        {
            throw new InvalidOperationException("Post not found.");
        }

        var existing = await db.SavedPosts
            .FirstOrDefaultAsync(s => s.CommunityPostId == id && s.StudentProfileId == profile.Id, ct);

        if (saved && existing is null)
        {
            db.SavedPosts.Add(new SavedPost { CommunityPostId = id, StudentProfileId = profile.Id });
            await db.SaveChangesAsync(ct);
        }
        else if (!saved && existing is not null)
        {
            db.SavedPosts.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return (await GetPostAsync(id, ct))!;
    }

    public async Task<IReadOnlyList<CommunityPostDto>> GetSavedAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var savedIds = await db.SavedPosts
            .Where(s => s.StudentProfileId == profile.Id)
            .OrderByDescending(s => s.SavedAtUtc)
            .Select(s => s.CommunityPostId)
            .ToListAsync(ct);

        if (savedIds.Count == 0)
        {
            return [];
        }

        var rows = await Project(db, VisibleTo(db, profile).Where(p => savedIds.Contains(p.Id)), profile.Id).ToListAsync(ct);
        // Reorder in memory: the saved-at order is lost once the ids go through the post query.
        return rows.Select(Map).OrderBy(p => savedIds.IndexOf(p.Id)).ToList();
    }

    public async Task<CollegeInfoDto?> GetCollegeAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var name = profile.College.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var key = name.ToLower();
        var college = await db.Institutions.FirstOrDefaultAsync(i => i.Name.ToLower() == key, ct);
        if (college is null)
        {
            return new CollegeInfoDto(name, name, profile.City, profile.State, "", "", "", null, false, false, 0);
        }

        var posts = await VisibleTo(db, profile).CountAsync(ct);
        return new CollegeInfoDto(
            college.Name, college.ShortName, college.City, college.State, college.About, college.Website,
            college.ContactEmail, college.LogoDataUrl, college.Verified, college.UserId is not null, posts);
    }

    public async Task<IReadOnlyList<CommunityCommentDto>> GetCommentsAsync(int postId, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var postAuthorId = await VisibleTo(db, profile)
            .Where(p => p.Id == postId)
            .Select(p => (int?)p.InstitutionId)
            .FirstOrDefaultAsync(ct);

        if (postAuthorId is null)
        {
            return [];
        }

        var comments = await db.PostComments
            .Include(c => c.Institution)
            .Where(c => c.CommunityPostId == postId)
            .OrderBy(c => c.PostedAtUtc)
            .ToListAsync(ct);

        var replies = comments
            .Where(c => c.ParentCommentId is not null)
            .ToLookup(c => c.ParentCommentId!.Value);

        CommunityCommentDto MapComment(PostComment c, IReadOnlyList<CommunityCommentDto> children) => new(
            c.Id,
            c.ParentCommentId,
            c.Institution?.Name ?? c.AuthorName,
            c.Institution?.City ?? c.AuthorHeadline,
            c.InstitutionId is not null,
            c.Institution?.Verified ?? false,
            c.InstitutionId == postAuthorId,
            c.StudentProfileId == profile.Id,
            c.Body,
            AsUtc(c.PostedAtUtc),
            children);

        // Newest conversations first; replies read top to bottom in the order they were written.
        return comments
            .Where(c => c.ParentCommentId is null)
            .OrderByDescending(c => c.PostedAtUtc)
            .Select(c => MapComment(c, replies[c.Id].Select(r => MapComment(r, [])).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<CommunityCommentDto>> AddCommentAsync(int postId, AddCommentRequest request, CancellationToken ct = default)
    {
        var body = request.Body?.Trim() ?? "";
        if (body.Length == 0)
        {
            throw new ArgumentException("Write something before posting.");
        }

        if (body.Length > MaxCommentLength)
        {
            throw new ArgumentException($"Comments are limited to {MaxCommentLength} characters.");
        }

        var profile = await ctx.GetProfileAsync(ct);
        if (!await VisibleTo(db, profile).AnyAsync(p => p.Id == postId, ct))
        {
            throw new ArgumentException("Post not found.");
        }

        int? parentId = null;
        if (request.ParentId is { } requestedParent)
        {
            var parent = await db.PostComments
                .Where(c => c.Id == requestedParent && c.CommunityPostId == postId)
                .Select(c => new { c.Id, c.ParentCommentId })
                .FirstOrDefaultAsync(ct)
                ?? throw new ArgumentException("The comment you replied to no longer exists.");

            // Keep threads one level deep: replying to a reply joins the same thread.
            parentId = parent.ParentCommentId ?? parent.Id;
        }

        db.PostComments.Add(new PostComment
        {
            CommunityPostId = postId,
            ParentCommentId = parentId,
            StudentProfileId = profile.Id,
            AuthorName = string.IsNullOrWhiteSpace(profile.User?.FullName) ? "Student" : profile.User.FullName,
            AuthorHeadline = Headline(profile),
            Body = body,
        });
        await db.SaveChangesAsync(ct);

        return await GetCommentsAsync(postId, ct);
    }

    public async Task<IReadOnlyList<CommunityCommentDto>> DeleteCommentAsync(int postId, int commentId, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var comment = await db.PostComments.FirstOrDefaultAsync(
            c => c.Id == commentId && c.CommunityPostId == postId && c.StudentProfileId == profile.Id, ct);

        if (comment is not null)
        {
            db.PostComments.Remove(comment);
            await db.SaveChangesAsync(ct);
        }

        return await GetCommentsAsync(postId, ct);
    }

    private static string Headline(StudentProfile profile) => string.IsNullOrWhiteSpace(profile.Branch)
        ? $"{profile.GraduationYear} batch"
        : $"{profile.Branch} · {profile.GraduationYear} batch";

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static IQueryable<PostRow> Project(SaplingDbContext db, IQueryable<CommunityPost> query, int profileId) =>
        query.Select(p => new PostRow(
            p,
            p.Institution!,
            p.BaseUpvotes + p.Upvotes.Count,
            p.Upvotes.Any(u => u.StudentProfileId == profileId),
            p.Comments.Count,
            db.SavedPosts.Any(s => s.CommunityPostId == p.Id && s.StudentProfileId == profileId)));

    private static CommunityPostDto Map(PostRow row)
    {
        var (p, i, upvotes, hasUpvoted, comments, isSaved) = row;
        return new CommunityPostDto(
            p.Id,
            new InstitutionDto(i.Id, i.Name, i.ShortName, i.City, i.Verified, i.LogoDataUrl),
            p.Kind,
            p.Title,
            p.Body,
            AsUtc(p.PostedAtUtc),
            p.StartsAtUtc is { } starts ? AsUtc(starts) : null,
            p.Venue,
            p.CtaLabel,
            p.CtaUrl,
            CareerService.Split(p.Tags),
            upvotes,
            hasUpvoted,
            comments,
            p.ImageUrl,
            p.IsPinned,
            isSaved);
    }

    private sealed record PostRow(CommunityPost Post, Institution Institution, int Upvotes, bool HasUpvoted, int Comments, bool IsSaved);
}
