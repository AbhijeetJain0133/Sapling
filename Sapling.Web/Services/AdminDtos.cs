namespace Sapling.Web.Services;

/// <summary>Admin-side shapes. Web-only, so they stay out of Sapling.Shared.</summary>
public sealed record AdminUserDto(
    string Id,
    string Name,
    string Email,
    string AccountType,
    bool Deactivated,
    bool IsSelf,
    DateTimeOffset? JoinedAt);

public sealed record AdminCountSliceDto(string Label, int Count);

public sealed record AdminDashboardDto(
    int Students,
    int DeactivatedStudents,
    int Institutes,
    int DeactivatedInstitutes,
    int Admins,
    int ClaimedColleges,
    int UnclaimedColleges,
    IReadOnlyList<AdminCountSliceDto> SignupsByMonth,
    IReadOnlyList<AdminCountSliceDto> TopColleges);

public sealed record CreateAccountRequest(string Email, string Password, string AccountType);
