using System.Text;
using Microsoft.EntityFrameworkCore;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>Everything an institute account can do: its dashboard, its students, its posts and its own profile.</summary>
public sealed class InstituteService(SaplingDbContext db, InstituteContext ctx)
{
    public const int MaxPinned = 3;

    public const int MaxImageBytes = 1_500_000;

    private static readonly string[] AllowedKinds =
        [PostKinds.Opportunity, PostKinds.Workshop, PostKinds.Event, PostKinds.Announcement];

    // ---- setup and profile ----

    public async Task<InstituteProfileDto?> FindProfileAsync(CancellationToken ct = default) =>
        await ctx.FindAsync(ct) is { } i ? MapProfile(i) : null;

    /// <summary>Claims the seeded college row of that name if nobody owns it, otherwise creates an unverified one.</summary>
    public async Task<InstituteProfileDto> CompleteSetupAsync(InstituteSetupRequest request, CancellationToken ct = default)
    {
        if (await ctx.FindAsync(ct) is not null)
        {
            throw new InvalidOperationException("This account is already linked to a college.");
        }

        var name = request.CollegeName?.Trim() ?? "";
        if (name.Length < 3)
        {
            throw new ArgumentException("Pick your college, or type its full name.");
        }

        var userId = await ctx.GetUserIdAsync();
        var lowered = name.ToLower();
        var existing = await db.Institutions.FirstOrDefaultAsync(i => i.Name.ToLower() == lowered, ct);

        if (existing is not null && !string.IsNullOrEmpty(existing.UserId))
        {
            throw new InvalidOperationException($"{existing.Name} already has an account. Ask your colleague for access.");
        }

        var institution = existing ?? new Institution { Name = name };
        institution.UserId = userId;
        institution.City = request.City?.Trim() ?? institution.City;
        institution.State = request.State?.Trim() ?? institution.State;
        institution.ShortName = string.IsNullOrWhiteSpace(request.ShortName) ? institution.ShortName : request.ShortName.Trim();
        institution.ContactEmail = (await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct)) ?? "";

        if (existing is null)
        {
            institution.CreatedAtUtc = DateTime.UtcNow;
            db.Institutions.Add(institution);
        }

        await db.SaveChangesAsync(ct);
        ctx.Forget();
        return MapProfile(institution);
    }

    public async Task<InstituteProfileDto> UpdateProfileAsync(UpdateInstituteRequest request, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        var name = request.Name?.Trim() ?? "";
        if (name.Length < 3)
        {
            throw new ArgumentException("Enter your college's full name.");
        }

        var lowered = name.ToLower();
        if (await db.Institutions.AnyAsync(i => i.Id != institution.Id && i.Name.ToLower() == lowered, ct))
        {
            throw new InvalidOperationException("Another account already uses that college name.");
        }

        var website = request.Website?.Trim() ?? "";
        if (website.Length > 0 && !Uri.TryCreate(website, UriKind.Absolute, out var url) || (website.Length > 0 && !IsWeb(website)))
        {
            throw new ArgumentException("The website must be a full http or https address.");
        }

        // Students join a community by the college name on their profile, so a rename moves the whole feed.
        institution.Name = name;
        institution.ShortName = request.ShortName?.Trim() ?? "";
        institution.City = request.City?.Trim() ?? "";
        institution.State = request.State?.Trim() ?? "";
        institution.About = request.About?.Trim() ?? "";
        institution.Website = website;
        institution.ContactEmail = request.ContactEmail?.Trim() ?? "";
        await db.SaveChangesAsync(ct);
        return MapProfile(institution);
    }

    public async Task<InstituteProfileDto> SetLogoAsync(string? dataUrl, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        institution.LogoDataUrl = Validated(dataUrl);
        await db.SaveChangesAsync(ct);
        return MapProfile(institution);
    }

    // ---- dashboard ----

    public async Task<InstituteDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        var students = StudentsOf(institution);

        var byBranch = await students
            .GroupBy(p => p.Branch)
            .Select(g => new CountSliceDto(g.Key, g.Count()))
            .ToListAsync(ct);

        var byYear = await students
            .GroupBy(p => p.GraduationYear)
            .Select(g => new CountSliceDto(g.Key.ToString(), g.Count()))
            .ToListAsync(ct);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var posts = db.CommunityPosts.Where(p => p.InstitutionId == institution.Id);

        return new InstituteDashboardDto(
            institution.Name,
            institution.Verified,
            await students.CountAsync(ct),
            await students.CountAsync(p => p.CreatedAtUtc >= monthStart, ct),
            await students.CountAsync(p => p.OnboardingComplete, ct),
            await posts.CountAsync(ct),
            await posts.CountAsync(p => p.IsPinned, ct),
            await posts.CountAsync(p => p.IsArchived, ct),
            await posts.SumAsync(p => p.BaseUpvotes + p.Upvotes.Count, ct),
            await posts.SumAsync(p => p.Comments.Count, ct),
            [.. byBranch.Select(Named).OrderByDescending(s => s.Count)],
            [.. byYear.OrderBy(s => s.Label)]);
    }

    // ---- students ----

    public async Task<IReadOnlyList<InstituteStudentDto>> GetStudentsAsync(
        string? query = null, string? branch = null, int? graduationYear = null, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        var students = StudentsOf(institution).Include(p => p.User).Include(p => p.Skills).ThenInclude(s => s.Skill);

        IQueryable<StudentProfile> filtered = students;
        if (!string.IsNullOrWhiteSpace(branch))
        {
            filtered = filtered.Where(p => p.Branch == branch);
        }

        if (graduationYear is { } year)
        {
            filtered = filtered.Where(p => p.GraduationYear == year);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLower();
            filtered = filtered.Where(p =>
                p.User!.FullName.ToLower().Contains(q)
                || p.User.Email!.ToLower().Contains(q)
                || p.Branch.ToLower().Contains(q));
        }

        var rows = await filtered
            .OrderBy(p => p.User!.FullName)
            .Select(p => new
            {
                p.Id,
                p.User!.FullName,
                p.User.Email,
                p.Branch,
                p.GraduationYear,
                p.OnboardingComplete,
                p.AvatarDataUrl,
                Target = p.TargetChosen
                    ? db.CareerRoles.Where(r => r.Id == p.TargetRoleId).Select(r => r.Title).FirstOrDefault()
                    : null,
                Skills = p.Skills.Select(s => s.Skill!.Name).ToList(),
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new InstituteStudentDto(
            r.Id,
            string.IsNullOrWhiteSpace(r.FullName) ? "Student" : r.FullName,
            r.Email ?? "",
            string.IsNullOrWhiteSpace(r.Branch) ? "Not set" : r.Branch,
            r.GraduationYear,
            r.Target,
            r.OnboardingComplete,
            [.. r.Skills.OrderBy(s => s)],
            r.AvatarDataUrl))];
    }

    public async Task<byte[]> ExportStudentsCsvAsync(CancellationToken ct = default)
    {
        var students = await GetStudentsAsync(ct: ct);
        var csv = new StringBuilder("Name,Email,Branch,Graduation year,Target role,Onboarding complete,Skills\n");
        foreach (var s in students)
        {
            csv.Append(string.Join(',', new[]
            {
                Csv(s.FullName), Csv(s.Email), Csv(s.Branch), Csv(s.GraduationYear.ToString()),
                Csv(s.TargetRole ?? ""), Csv(s.OnboardingComplete ? "Yes" : "No"), Csv(string.Join("; ", s.Skills)),
            })).Append('\n');
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    // ---- posts ----

    public async Task<IReadOnlyList<InstitutePostDto>> GetPostsAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        var query = db.CommunityPosts.Where(p => p.InstitutionId == institution.Id);
        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        var rows = await query
            .OrderByDescending(p => p.IsPinned)
            .ThenByDescending(p => p.PostedAtUtc)
            .Select(p => new
            {
                Post = p,
                Upvotes = p.BaseUpvotes + p.Upvotes.Count,
                Comments = p.Comments.Count,
                Saves = db.SavedPosts.Count(s => s.CommunityPostId == p.Id),
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => MapPost(r.Post, r.Upvotes, r.Comments, r.Saves))];
    }

    public async Task<InstitutePostDto> CreatePostAsync(SavePostRequest request, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        var post = new CommunityPost { InstitutionId = institution.Id, PostedAtUtc = DateTime.UtcNow };
        Apply(post, request);
        db.CommunityPosts.Add(post);
        await db.SaveChangesAsync(ct);
        return MapPost(post, post.BaseUpvotes, 0, 0);
    }

    public async Task<InstitutePostDto> UpdatePostAsync(int id, SavePostRequest request, CancellationToken ct = default)
    {
        var post = await OwnedAsync(id, ct);
        Apply(post, request);
        post.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ReadAsync(post, ct);
    }

    public async Task DeletePostAsync(int id, CancellationToken ct = default)
    {
        var post = await OwnedAsync(id, ct);
        db.CommunityPosts.Remove(post);
        await db.SaveChangesAsync(ct);
    }

    public async Task<InstitutePostDto> SetPinnedAsync(int id, bool pinned, CancellationToken ct = default)
    {
        var post = await OwnedAsync(id, ct);
        if (pinned && !post.IsPinned)
        {
            var alreadyPinned = await db.CommunityPosts.CountAsync(p => p.InstitutionId == post.InstitutionId && p.IsPinned, ct);
            if (alreadyPinned >= MaxPinned)
            {
                throw new InvalidOperationException($"You can pin up to {MaxPinned} posts. Unpin one first.");
            }
        }

        post.IsPinned = pinned;
        if (pinned)
        {
            post.IsArchived = false;
        }

        await db.SaveChangesAsync(ct);
        return await ReadAsync(post, ct);
    }

    public async Task<InstitutePostDto> SetArchivedAsync(int id, bool archived, CancellationToken ct = default)
    {
        var post = await OwnedAsync(id, ct);
        post.IsArchived = archived;
        if (archived)
        {
            post.IsPinned = false;
        }

        await db.SaveChangesAsync(ct);
        return await ReadAsync(post, ct);
    }

    // ---- comments ----

    public async Task<IReadOnlyList<CommunityCommentDto>> GetCommentsAsync(int postId, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        if (!await db.CommunityPosts.AnyAsync(p => p.Id == postId && p.InstitutionId == institution.Id, ct))
        {
            return [];
        }

        var comments = await db.PostComments
            .Include(c => c.Institution)
            .Where(c => c.CommunityPostId == postId)
            .OrderBy(c => c.PostedAtUtc)
            .ToListAsync(ct);

        var replies = comments.Where(c => c.ParentCommentId is not null).ToLookup(c => c.ParentCommentId!.Value);

        CommunityCommentDto MapComment(PostComment c, IReadOnlyList<CommunityCommentDto> children) => new(
            c.Id,
            c.ParentCommentId,
            c.Institution?.Name ?? c.AuthorName,
            c.Institution?.City ?? c.AuthorHeadline,
            c.InstitutionId is not null,
            c.Institution?.Verified ?? false,
            c.InstitutionId == institution.Id,
            c.InstitutionId == institution.Id,
            c.Body,
            AsUtc(c.PostedAtUtc),
            children);

        return [.. comments
            .Where(c => c.ParentCommentId is null)
            .OrderByDescending(c => c.PostedAtUtc)
            .Select(c => MapComment(c, [.. replies[c.Id].Select(r => MapComment(r, []))]))];
    }

    public async Task<IReadOnlyList<CommunityCommentDto>> ReplyAsync(int postId, AddCommentRequest request, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        var body = request.Body?.Trim() ?? "";
        if (body.Length == 0)
        {
            throw new ArgumentException("Write something before posting.");
        }

        if (body.Length > CommunityService.MaxCommentLength)
        {
            throw new ArgumentException($"Comments are limited to {CommunityService.MaxCommentLength} characters.");
        }

        if (!await db.CommunityPosts.AnyAsync(p => p.Id == postId && p.InstitutionId == institution.Id, ct))
        {
            throw new ArgumentException("Post not found.");
        }

        int? parentId = null;
        if (request.ParentId is { } requested)
        {
            var parent = await db.PostComments
                .Where(c => c.Id == requested && c.CommunityPostId == postId)
                .Select(c => new { c.Id, c.ParentCommentId })
                .FirstOrDefaultAsync(ct)
                ?? throw new ArgumentException("The comment you replied to no longer exists.");

            parentId = parent.ParentCommentId ?? parent.Id;
        }

        db.PostComments.Add(new PostComment
        {
            CommunityPostId = postId,
            ParentCommentId = parentId,
            InstitutionId = institution.Id,
            AuthorName = institution.Name,
            AuthorHeadline = institution.City,
            Body = body,
        });
        await db.SaveChangesAsync(ct);

        return await GetCommentsAsync(postId, ct);
    }

    public async Task DeleteCommentAsync(int postId, int commentId, CancellationToken ct = default)
    {
        var institution = await ctx.GetAsync(ct);
        if (!await db.CommunityPosts.AnyAsync(p => p.Id == postId && p.InstitutionId == institution.Id, ct))
        {
            return;
        }

        var comment = await db.PostComments.FirstOrDefaultAsync(c => c.Id == commentId && c.CommunityPostId == postId, ct);
        if (comment is not null)
        {
            db.PostComments.Remove(comment);
            await db.SaveChangesAsync(ct);
        }
    }

    // ---- helpers ----

    private IQueryable<StudentProfile> StudentsOf(Institution institution)
    {
        var name = institution.Name.Trim().ToLower();
        return db.StudentProfiles.Where(p => name != "" && p.College.ToLower() == name);
    }

    private async Task<CommunityPost> OwnedAsync(int id, CancellationToken ct)
    {
        var institution = await ctx.GetAsync(ct);
        return await db.CommunityPosts.FirstOrDefaultAsync(p => p.Id == id && p.InstitutionId == institution.Id, ct)
            ?? throw new ArgumentException("Post not found.");
    }

    private async Task<InstitutePostDto> ReadAsync(CommunityPost post, CancellationToken ct)
    {
        var upvotes = post.BaseUpvotes + await db.PostUpvotes.CountAsync(u => u.CommunityPostId == post.Id, ct);
        var comments = await db.PostComments.CountAsync(c => c.CommunityPostId == post.Id, ct);
        var saves = await db.SavedPosts.CountAsync(s => s.CommunityPostId == post.Id, ct);
        return MapPost(post, upvotes, comments, saves);
    }

    private static void Apply(CommunityPost post, SavePostRequest request)
    {
        var kind = request.Kind?.Trim() ?? "";
        if (!AllowedKinds.Contains(kind))
        {
            throw new ArgumentException("Pick what kind of post this is.");
        }

        var title = request.Title?.Trim() ?? "";
        if (title.Length is < 4 or > 140)
        {
            throw new ArgumentException("Give the post a title between 4 and 140 characters.");
        }

        var body = request.Body?.Trim() ?? "";
        if (body.Length is < 10 or > 4000)
        {
            throw new ArgumentException("The body should be between 10 and 4000 characters.");
        }

        var ctaUrl = request.CtaUrl?.Trim();
        if (!string.IsNullOrEmpty(ctaUrl) && !IsWeb(ctaUrl))
        {
            throw new ArgumentException("The link must be a full http or https address.");
        }

        post.Kind = kind;
        post.Title = title;
        post.Body = body;
        post.StartsAtUtc = request.StartsAt?.UtcDateTime;
        post.Venue = Blank(request.Venue);
        post.CtaLabel = Blank(request.CtaLabel);
        post.CtaUrl = string.IsNullOrEmpty(ctaUrl) ? null : ctaUrl;
        post.ImageUrl = Validated(request.ImageUrl);
        post.Tags = string.Join('|', (request.Tags ?? "")
            .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8));

        if (post.CtaUrl is null)
        {
            post.CtaLabel = null;
        }
    }

    /// <summary>Artwork and logos ride in the row as data-URLs, so only small images are accepted.</summary>
    private static string? Validated(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return null;
        }

        if (!dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("That image could not be read.");
        }

        if (dataUrl.Length > MaxImageBytes)
        {
            throw new ArgumentException("That image is too large. Use one under 1 MB.");
        }

        return dataUrl;
    }

    private static bool IsWeb(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CountSliceDto Named(CountSliceDto slice) =>
        string.IsNullOrWhiteSpace(slice.Label) ? slice with { Label = "Not set" } : slice;

    private static string Csv(string value)
    {
        // A leading =, +, - or @ makes a spreadsheet treat the cell as a formula.
        var safe = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static InstituteProfileDto MapProfile(Institution i) => new(
        i.Id, i.Name, i.ShortName, i.City, i.State, i.About, i.Website, i.ContactEmail, i.Verified, i.LogoDataUrl);

    private static InstitutePostDto MapPost(CommunityPost p, int upvotes, int comments, int saves) => new(
        p.Id,
        p.Kind,
        p.Title,
        p.Body,
        AsUtc(p.PostedAtUtc),
        p.StartsAtUtc is { } starts ? AsUtc(starts) : null,
        p.Venue,
        p.CtaLabel,
        p.CtaUrl,
        p.ImageUrl,
        CareerService.Split(p.Tags),
        p.IsPinned,
        p.IsArchived,
        upvotes,
        comments,
        saves);
}
