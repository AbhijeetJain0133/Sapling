using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Api;

/// <summary>
/// Google sign-in. The browser head ends with the usual Identity cookie. The app opens the same flow in the
/// system browser and gets back a one-time code, which it trades for bearer tokens so they never sit in a URL.
/// </summary>
public static class ExternalAuthEndpoints
{
    // Fixed rather than caller-supplied, so a code can only ever be handed to the Sapling app.
    private const string AppCallback = ExternalAuth.AppCallback;

    private const string EmailVerifiedClaim = "email_verified";
    private const string MobileMarker = "mobile=true";
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

    public static void ConfigureGoogle(GoogleOptions options)
    {
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");

        // Google returns with a top-level GET, so Lax is enough, and it lets the flow run over plain HTTP in development.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        // Covers "Cancel" on Google's consent screen and an expired or tampered state, which would otherwise be a 500.
        options.Events.OnRemoteFailure = context =>
        {
            var mobile = context.Properties?.RedirectUri?.Contains(MobileMarker) == true;
            context.Response.Redirect(FailureUrl(mobile, "google"));
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    public static void MapExternalAuth(this IEndpointRouteBuilder app)
    {
        var google = app.MapGroup("/account/google").AllowAnonymous();

        google.MapGet("/", (string? returnUrl, SignInManager<AppUser> signIn, IAuthenticationSchemeProvider schemes) =>
            ChallengeAsync(signIn, schemes, mobile: false,
                $"/account/google/callback?returnUrl={Uri.EscapeDataString(LocalOnly(returnUrl))}"));

        google.MapGet("/mobile", (SignInManager<AppUser> signIn, IAuthenticationSchemeProvider schemes) =>
            ChallengeAsync(signIn, schemes, mobile: true, $"/account/google/callback?{MobileMarker}"));

        google.MapGet("/callback", CallbackAsync);

        app.MapPost("/api/identity/google/exchange", ExchangeAsync).AllowAnonymous();
    }

    private static async Task<IResult> ChallengeAsync(
        SignInManager<AppUser> signIn, IAuthenticationSchemeProvider schemes, bool mobile, string redirectUri)
    {
        if (await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
        {
            return Results.Redirect(FailureUrl(mobile, "unavailable"));
        }

        var properties = signIn.ConfigureExternalAuthenticationProperties(GoogleDefaults.AuthenticationScheme, redirectUri);
        return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext http,
        bool? mobile,
        string? returnUrl,
        SignInManager<AppUser> signIn,
        UserManager<AppUser> users,
        IMemoryCache cache)
    {
        var isMobile = mobile == true;
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return Results.Redirect(FailureUrl(isMobile, "google"));
        }

        await http.SignOutAsync(IdentityConstants.ExternalScheme);

        var (user, isNew, error) = await FindOrCreateUserAsync(users, info);
        if (user is null)
        {
            return Results.Redirect(FailureUrl(isMobile, error!));
        }

        var shell = user.AccountType switch
        {
            AccountTypes.Institution => "/institute",
            AccountTypes.Admin => "/admin",
            _ => null,
        };

        if (isMobile)
        {
            // The institute and admin heads are web-only, so the app must never receive a token for one.
            if (shell is not null)
            {
                return Results.Redirect(FailureUrl(true, "institute"));
            }

            var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            cache.Set(CodeKey(code), user.Id, CodeLifetime);
            return Results.Redirect($"{AppCallback}?code={code}{(isNew ? "&new=true" : "")}");
        }

        await signIn.SignInAsync(user, isPersistent: true);
        if (shell is not null)
        {
            return Results.LocalRedirect(shell);
        }

        return Results.LocalRedirect(isNew ? "/onboarding/about" : LocalOnly(returnUrl));
    }

    private static async Task<(AppUser? User, bool IsNew, string? Error)> FindOrCreateUserAsync(
        UserManager<AppUser> users, ExternalLoginInfo info)
    {
        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user is not null)
        {
            return await users.IsLockedOutAsync(user) ? (null, false, "locked") : (user, false, null);
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return (null, false, "no-email");
        }

        // Matching an existing account by email is only safe once Google vouches that this person owns the address.
        if (!string.Equals(info.Principal.FindFirstValue(EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase))
        {
            return (null, false, "unverified");
        }

        user = await users.FindByEmailAsync(email);
        var isNew = user is null;
        if (user is null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = DisplayName(info.Principal, email),
            };

            if (!(await users.CreateAsync(user)).Succeeded)
            {
                return (null, false, "google");
            }
        }
        else if (await users.IsLockedOutAsync(user))
        {
            return (null, false, "locked");
        }

        return (await users.AddLoginAsync(user, info)).Succeeded ? (user, isNew, null) : (null, false, "google");
    }

    private static async Task<IResult> ExchangeAsync(
        ExchangeRequest request,
        IMemoryCache cache,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        var key = CodeKey(request.Code ?? "");
        if (string.IsNullOrEmpty(request.Code) || !cache.TryGetValue(key, out string? userId))
        {
            return Results.Unauthorized();
        }

        cache.Remove(key);
        var user = await users.FindByIdAsync(userId!);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Same as the Identity login endpoint: the bearer handler writes the access and refresh tokens to the response.
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signIn.SignInAsync(user, isPersistent: false);
        return Results.Empty;
    }

    private static string DisplayName(ClaimsPrincipal principal, string email)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{principal.FindFirstValue(ClaimTypes.GivenName)} {principal.FindFirstValue(ClaimTypes.Surname)}".Trim();
        }

        return string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name;
    }

    private static string FailureUrl(bool mobile, string error) =>
        mobile ? $"{AppCallback}?error={error}" : $"/signin?error={error}";

    private static string LocalOnly(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/home";

    private static string CodeKey(string code) => $"google-code:{code}";

    private sealed record ExchangeRequest(string? Code);
}
