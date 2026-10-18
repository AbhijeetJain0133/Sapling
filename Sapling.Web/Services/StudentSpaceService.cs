using Microsoft.EntityFrameworkCore;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>
/// The student space: one chat per college, keyed by the college name on the student's profile. Only student
/// profiles can read or post, and there is no institute or admin view of it, so staff never see it.
/// </summary>
public sealed class StudentSpaceService(SaplingDbContext db, StudentContext ctx) : IStudentSpaceService
{
    public const int MaxLength = 500;

    // What one refresh returns; older messages stay in the database but drop out of view.
    private const int PageSize = 100;

    // Photos are sent with the member list, so the list is bounded.
    private const int MaxMembers = 300;

    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2);

    public async Task<StudentSpaceDto> GetAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        return await LoadAsync(profile, ct);
    }

    public async Task<StudentSpaceDto> PostAsync(PostStudentMessageRequest request, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var college = Key(profile);
        if (college.Length == 0)
        {
            throw new InvalidOperationException("Add your college to your profile to join its student space.");
        }

        var body = Clean(request.Body);
        var now = DateTime.UtcNow;
        var last = await db.StudentMessages
            .Where(m => m.StudentProfileId == profile.Id)
            .OrderByDescending(m => m.Id)
            .Select(m => (DateTime?)m.PostedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (last is { } previous && now - previous < MinGap)
        {
            throw new ArgumentException("You're sending messages too quickly. Wait a moment.");
        }

        db.StudentMessages.Add(new StudentMessage { College = college, StudentProfileId = profile.Id, Body = body, PostedAtUtc = now });
        await db.SaveChangesAsync(ct);
        return await LoadAsync(profile, ct);
    }

    public async Task<StudentSpaceDto> EditAsync(int id, PostStudentMessageRequest request, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var message = await OwnAsync(id, profile, ct);
        var body = Clean(request.Body);
        if (body != message.Body)
        {
            message.Body = body;
            message.EditedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return await LoadAsync(profile, ct);
    }

    public async Task<StudentSpaceDto> DeleteAsync(int id, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var message = await OwnAsync(id, profile, ct);

        // Bookmarks of a deleted message go with it.
        await db.SavedStudentMessages.Where(s => s.StudentMessageId == id).ExecuteDeleteAsync(ct);
        db.StudentMessages.Remove(message);
        await db.SaveChangesAsync(ct);
        return await LoadAsync(profile, ct);
    }

    public async Task<StudentSpaceDto> SetSavedAsync(int id, bool saved, CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var college = Key(profile);
        if (!await db.StudentMessages.AnyAsync(m => m.Id == id && m.College == college && college != "", ct))
        {
            throw new InvalidOperationException("Message not found.");
        }

        var existing = await db.SavedStudentMessages
            .FirstOrDefaultAsync(s => s.StudentMessageId == id && s.StudentProfileId == profile.Id, ct);
        if (saved && existing is null)
        {
            db.SavedStudentMessages.Add(new SavedStudentMessage { StudentMessageId = id, StudentProfileId = profile.Id });
            await db.SaveChangesAsync(ct);
        }
        else if (!saved && existing is not null)
        {
            db.SavedStudentMessages.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return await LoadAsync(profile, ct);
    }

    public async Task<IReadOnlyList<StudentMessageDto>> GetSavedAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var college = Key(profile);
        var savedIds = await db.SavedStudentMessages
            .Where(s => s.StudentProfileId == profile.Id)
            .OrderByDescending(s => s.SavedAtUtc)
            .Select(s => s.StudentMessageId)
            .ToListAsync(ct);
        if (savedIds.Count == 0 || college.Length == 0)
        {
            return [];
        }

        // Still scoped to the college, so a student who changes college loses sight of the old space's messages.
        var messages = await MapAsync(db.StudentMessages.Where(m => m.College == college && savedIds.Contains(m.Id)), profile, ct);
        return messages.OrderBy(m => savedIds.IndexOf(m.Id)).ToList();
    }

    public async Task<IReadOnlyList<StudentSpaceMemberDto>> GetMembersAsync(CancellationToken ct = default)
    {
        var profile = await ctx.GetProfileAsync(ct);
        var college = Key(profile);
        if (college.Length == 0)
        {
            return [];
        }

        // Only what the member list shows: name, photo and passing year.
        var rows = await db.StudentProfiles
            .Where(p => p.College.Trim().ToLower() == college)
            .OrderBy(p => p.User!.FullName)
            .Take(MaxMembers)
            .Select(p => new { p.Id, Name = p.User!.FullName, p.GraduationYear, p.AvatarDataUrl })
            .ToListAsync(ct);

        return rows
            .Select(r => new StudentSpaceMemberDto(r.Id, Name(r.Name), r.GraduationYear, r.AvatarDataUrl, r.Id == profile.Id))
            .ToList();
    }

    private static string Key(StudentProfile profile) => profile.College.Trim().ToLowerInvariant();

    private static string Name(string? name) => string.IsNullOrWhiteSpace(name) ? "Student" : name;

    private static string Clean(string? text)
    {
        var body = (text ?? "").Trim();
        if (body.Length == 0)
        {
            throw new ArgumentException("Write a message first.");
        }

        if (body.Length > MaxLength)
        {
            throw new ArgumentException($"Keep messages under {MaxLength} characters.");
        }

        return body;
    }

    /// <summary>Students can change or remove only what they wrote themselves.</summary>
    private async Task<StudentMessage> OwnAsync(int id, StudentProfile profile, CancellationToken ct) =>
        await db.StudentMessages.FirstOrDefaultAsync(m => m.Id == id && m.StudentProfileId == profile.Id, ct)
        ?? throw new InvalidOperationException("Message not found.");

    private async Task<StudentSpaceDto> LoadAsync(StudentProfile profile, CancellationToken ct)
    {
        var college = Key(profile);
        if (college.Length == 0)
        {
            return new StudentSpaceDto("", 0, []);
        }

        var members = await db.StudentProfiles.CountAsync(p => p.College.Trim().ToLower() == college, ct);
        var latest = db.StudentMessages.Where(m => m.College == college).OrderByDescending(m => m.Id).Take(PageSize);
        var messages = await MapAsync(latest, profile, ct);
        return new StudentSpaceDto(profile.College.Trim(), members, messages.OrderBy(m => m.Id).ToList());
    }

    private async Task<List<StudentMessageDto>> MapAsync(IQueryable<StudentMessage> query, StudentProfile profile, CancellationToken ct)
    {
        var rows = await query
            .Select(m => new
            {
                m.Id,
                m.StudentProfileId,
                Name = m.Author!.User!.FullName,
                m.Body,
                m.PostedAtUtc,
                m.EditedAtUtc,
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var saved = (await db.SavedStudentMessages
            .Where(s => s.StudentProfileId == profile.Id && ids.Contains(s.StudentMessageId))
            .Select(s => s.StudentMessageId)
            .ToListAsync(ct)).ToHashSet();

        return rows
            .Select(r => new StudentMessageDto(
                r.Id,
                Name(r.Name),
                null,
                r.StudentProfileId == profile.Id,
                r.Body,
                new DateTimeOffset(DateTime.SpecifyKind(r.PostedAtUtc, DateTimeKind.Utc)),
                r.StudentProfileId,
                saved.Contains(r.Id),
                r.EditedAtUtc is not null))
            .ToList();
    }
}
