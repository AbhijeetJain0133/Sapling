using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>Resolves the signed-in institute account and the college row it owns. Web-only: MAUI has no institute head.</summary>
public sealed class InstituteContext(SaplingDbContext db, StudentContext students)
{
    private Institution? _cached;

    public async Task<ClaimsPrincipal?> GetPrincipalAsync() => await students.GetPrincipalAsync();

    public async Task<bool> IsInstituteAsync() =>
        (await GetPrincipalAsync())?.FindFirstValue(AppUserClaimsPrincipalFactory.AccountTypeClaim) == AccountTypes.Institution;

    public async Task<string> GetUserIdAsync()
    {
        var principal = await GetPrincipalAsync()
            ?? throw new UnauthorizedAccessException("No signed-in institute.");

        if (principal.FindFirstValue(AppUserClaimsPrincipalFactory.AccountTypeClaim) != AccountTypes.Institution)
        {
            throw new UnauthorizedAccessException("This is not an institute account.");
        }

        return principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("No signed-in institute.");
    }

    /// <summary>Null until the account finishes setup by picking its college.</summary>
    public async Task<Institution?> FindAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var userId = await GetUserIdAsync();
        _cached = await db.Institutions.FirstOrDefaultAsync(i => i.UserId == userId, ct);
        return _cached;
    }

    public async Task<Institution> GetAsync(CancellationToken ct = default) =>
        await FindAsync(ct) ?? throw new InvalidOperationException("This institute account has not picked its college yet.");

    public void Forget() => _cached = null;
}
