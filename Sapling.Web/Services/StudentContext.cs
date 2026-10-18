using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>Resolves the signed-in student from either an HTTP request (API, static SSR) or a Blazor circuit.</summary>
public sealed class StudentContext(
    SaplingDbContext db,
    IHttpContextAccessor http,
    IServiceProvider services,
    CareerCatalogueFile catalogue)
{
    private StudentProfile? _cached;

    /// <summary>The signed-in principal, whichever head asked: an HTTP request or a Blazor circuit.</summary>
    public async Task<ClaimsPrincipal?> GetPrincipalAsync()
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated == true)
        {
            return principal;
        }

        if (services.GetService<AuthenticationStateProvider>() is { } provider)
        {
            var state = await provider.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated == true)
            {
                return state.User;
            }
        }

        return null;
    }

    public async Task<string?> GetUserIdAsync() =>
        (await GetPrincipalAsync())?.FindFirstValue(ClaimTypes.NameIdentifier);

    public async Task<StudentProfile> GetProfileAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var principal = await GetPrincipalAsync()
            ?? throw new UnauthorizedAccessException("No signed-in student.");

        // Without this an institute account would silently get a blank student profile on any student page.
        if (principal.FindFirstValue(AppUserClaimsPrincipalFactory.AccountTypeClaim) == AccountTypes.Institution)
        {
            throw new UnauthorizedAccessException("This is an institute account.");
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("No signed-in student.");

        _cached = await db.StudentProfiles
            .Include(p => p.User)
            .Include(p => p.Skills).ThenInclude(s => s.Skill)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (_cached is null)
        {
            // A deleted account can still hold a sign-in cookie for a short while; it must not get a new profile.
            if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            {
                throw new UnauthorizedAccessException("This account no longer exists.");
            }

            var defaultCode = catalogue.DefaultRoleFor(null).Onet;
            var defaultRole = await db.CareerRoles.FirstAsync(r => r.OnetCode == defaultCode, ct);
            _cached = new StudentProfile
            {
                UserId = userId,
                GraduationYear = DateTime.Today.Year + 1,
                TargetRoleId = defaultRole.Id,
            };
            db.StudentProfiles.Add(_cached);
            await db.SaveChangesAsync(ct);
            _cached.User = await db.Users.FirstAsync(u => u.Id == userId, ct);
        }

        return _cached;
    }
}
