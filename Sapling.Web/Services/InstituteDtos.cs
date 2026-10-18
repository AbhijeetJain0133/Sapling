namespace Sapling.Web.Services;

/// <summary>Institute-side shapes. Web-only, so they stay out of Sapling.Shared.</summary>
public sealed record InstituteProfileDto(
    int Id,
    string Name,
    string ShortName,
    string City,
    string State,
    string About,
    string Website,
    string ContactEmail,
    bool Verified,
    string? LogoDataUrl);

public sealed record UpdateInstituteRequest(
    string Name,
    string ShortName,
    string City,
    string State,
    string About,
    string Website,
    string ContactEmail);

public sealed record InstituteSetupRequest(string CollegeName, string ShortName, string City, string State);

public sealed record CountSliceDto(string Label, int Count);

public sealed record InstituteDashboardDto(
    string Name,
    bool Verified,
    int Students,
    int NewStudentsThisMonth,
    int OnboardedStudents,
    int Posts,
    int PinnedPosts,
    int ArchivedPosts,
    int Upvotes,
    int Comments,
    IReadOnlyList<CountSliceDto> ByBranch,
    IReadOnlyList<CountSliceDto> ByGraduationYear);

public sealed record InstituteStudentDto(
    int Id,
    string FullName,
    string Email,
    string Branch,
    int GraduationYear,
    string? TargetRole,
    bool OnboardingComplete,
    IReadOnlyList<string> Skills,
    string? AvatarDataUrl);

public sealed record InstitutePostDto(
    int Id,
    string Kind,
    string Title,
    string Body,
    DateTimeOffset PostedAt,
    DateTimeOffset? StartsAt,
    string? Venue,
    string? CtaLabel,
    string? CtaUrl,
    string? ImageUrl,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsArchived,
    int Upvotes,
    int CommentCount,
    int SaveCount);

public sealed record SavePostRequest(
    string Kind,
    string Title,
    string Body,
    DateTimeOffset? StartsAt,
    string? Venue,
    string? CtaLabel,
    string? CtaUrl,
    string? ImageUrl,
    string Tags);
