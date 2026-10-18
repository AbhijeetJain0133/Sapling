using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>Platform administration: who exists, who is blocked, and the headline numbers. Web-only.</summary>
public sealed class AdminService(
    SaplingDbContext db,
    UserManager<AppUser> users,
    StudentContext ctx)
{
    private static readonly string[] Manageable =
        [AccountTypes.Student, AccountTypes.Institution, AccountTypes.Admin];

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var accounts = await db.Users
            .Select(u => new { u.AccountType, u.LockoutEnd, u.CreatedAtUtc })
            .ToListAsync(ct);

        var claimed = await db.Institutions.CountAsync(i => i.UserId != null, ct);
        var unclaimed = await db.Institutions.CountAsync(i => i.UserId == null, ct);

        // Ordered on the anonymous shape: SQLite cannot sort by a property of a projected record.
        var topColleges = await db.StudentProfiles
            .Where(p => p.College != "")
            .GroupBy(p => p.College)
            .Select(g => new { College = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToListAsync(ct);

        int CountOf(string type, bool? deactivated = null) => accounts.Count(a =>
            a.AccountType == type
            && (deactivated is null || AccountStatus.IsDeactivated(a.LockoutEnd) == deactivated));

        // Six buckets ending with the current month; accounts predating the column land before the window.
        var firstMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5);
        var signups = Enumerable.Range(0, 6)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month => new AdminCountSliceDto(
                month.ToString("MMM yy", CultureInfo.InvariantCulture),
                accounts.Count(a => a.CreatedAtUtc >= month && a.CreatedAtUtc < month.AddMonths(1))))
            .ToList();

        return new AdminDashboardDto(
            CountOf(AccountTypes.Student),
            CountOf(AccountTypes.Student, deactivated: true),
            CountOf(AccountTypes.Institution),
            CountOf(AccountTypes.Institution, deactivated: true),
            CountOf(AccountTypes.Admin),
            claimed,
            unclaimed,
            signups,
            [.. topColleges.Select(c => new AdminCountSliceDto(c.College, c.Count))]);
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetAccountsAsync(
        string accountType, string? query = null, CancellationToken ct = default)
    {
        Require(accountType);
        var me = await CurrentUserIdAsync();

        IQueryable<AppUser> matching = db.Users.Where(u => u.AccountType == accountType);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLower();
            matching = matching.Where(u => u.Email!.ToLower().Contains(q) || u.FullName.ToLower().Contains(q));
        }

        var rows = await matching
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.FullName, u.Email, u.AccountType, u.LockoutEnd, u.CreatedAtUtc })
            .ToListAsync(ct);

        return [.. rows.Select(u => new AdminUserDto(
            u.Id,
            Display(u.FullName, u.Email),
            u.Email ?? "",
            u.AccountType,
            AccountStatus.IsDeactivated(u.LockoutEnd),
            u.Id == me,
            u.CreatedAtUtc.Year <= 1 ? null : new DateTimeOffset(DateTime.SpecifyKind(u.CreatedAtUtc, DateTimeKind.Utc))))];
    }

    public async Task<AdminUserDto> CreateAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        Require(request.AccountType);

        var email = AccountFlowService.Clean(request.Email);
        if (!email.Contains('@') || email.Length < 5)
        {
            throw new ArgumentException("Enter a valid email address.");
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            throw new InvalidOperationException("An account with that email already exists.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            // An admin vouches for the address, so there is no code to send.
            EmailConfirmed = true,
            AccountType = request.AccountType,
        };

        var created = await users.CreateAsync(user, request.Password ?? "");
        if (!created.Succeeded)
        {
            throw new ArgumentException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        return new AdminUserDto(user.Id, Display(user.FullName, user.Email), email, user.AccountType, false, false,
            new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc)));
    }

    public async Task SetDeactivatedAsync(string userId, bool deactivated, CancellationToken ct = default)
    {
        var user = await FindManageableAsync(userId);
        await GuardSelfAsync(user, deactivated ? "deactivate" : "reactivate");

        if (deactivated && user.AccountType == AccountTypes.Admin && await LastActiveAdminAsync(user, ct))
        {
            throw new InvalidOperationException("That is the last active admin. Add another before deactivating this one.");
        }

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, deactivated ? AccountStatus.DeactivatedUntil : null);
        await users.ResetAccessFailedCountAsync(user);

        // Rolling the stamp signs out any session the account already has open.
        await users.UpdateSecurityStampAsync(user);
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        var user = await FindManageableAsync(userId);
        await GuardSelfAsync(user, "delete");

        if (user.AccountType == AccountTypes.Admin && await LastActiveAdminAsync(user, ct))
        {
            throw new InvalidOperationException("That is the last active admin. Add another before deleting this one.");
        }

        // The college, its posts and its comments outlive the login, so another account can claim it later.
        var college = await db.Institutions.FirstOrDefaultAsync(i => i.UserId == user.Id, ct);
        if (college is not null)
        {
            college.UserId = null;
            await db.SaveChangesAsync(ct);
        }

        // A student's profile cascades, taking their skills, resumes, interviews, upvotes and saves with it.
        var deleted = await users.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", deleted.Errors.Select(e => e.Description)));
        }
    }

    private async Task<AppUser> FindManageableAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId)
            ?? throw new ArgumentException("That account no longer exists.");

        Require(user.AccountType);
        return user;
    }

    private async Task GuardSelfAsync(AppUser user, string verb)
    {
        if (user.Id == await CurrentUserIdAsync())
        {
            throw new InvalidOperationException($"You cannot {verb} your own account.");
        }
    }

    private async Task<bool> LastActiveAdminAsync(AppUser user, CancellationToken ct)
    {
        var others = await db.Users
            .Where(u => u.AccountType == AccountTypes.Admin && u.Id != user.Id)
            .Select(u => u.LockoutEnd)
            .ToListAsync(ct);

        return !others.Any(end => !AccountStatus.IsDeactivated(end));
    }

    private async Task<string?> CurrentUserIdAsync() =>
        (await ctx.GetPrincipalAsync())?.FindFirstValue(ClaimTypes.NameIdentifier);

    private static void Require(string accountType)
    {
        if (!Manageable.Contains(accountType))
        {
            throw new ArgumentException("Unknown account type.");
        }
    }

    private static string Display(string? fullName, string? email) =>
        !string.IsNullOrWhiteSpace(fullName) ? fullName
        : string.IsNullOrWhiteSpace(email) ? "Account"
        : email[..email.IndexOf('@')];
}
