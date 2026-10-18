using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>Puts the student's display name in the cookie so the shell does not fall back to the email.</summary>
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<AppUser>(userManager, options)
{
    public const string AccountTypeClaim = "account_type";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            identity.AddClaim(new Claim("full_name", user.FullName));
        }

        identity.AddClaim(new Claim(AccountTypeClaim, string.IsNullOrWhiteSpace(user.AccountType) ? AccountTypes.Student : user.AccountType));

        return identity;
    }
}
