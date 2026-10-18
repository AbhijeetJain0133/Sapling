using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Sapling.Shared.Content;
using Sapling.Shared.Contracts;

namespace Sapling.Services;

/// <summary>
/// A null <see cref="Error"/> on failure means the student backed out, so there is nothing to show.
/// <see cref="Restart"/> means an email verification expired and the flow goes back to its first step.
/// </summary>
public sealed record AuthResult(bool Succeeded, string? Error, bool IsNewUser = false, string? Ticket = null, bool Restart = false);

/// <summary>
/// Talks to the Identity API endpoints with bearer tokens and keeps them in SecureStorage,
/// which is the transport the browser head gets for free from the auth cookie.
/// </summary>
public sealed class ApiAuthService(IHttpClientFactory factory, TokenStore tokens) : AuthenticationStateProvider
{
    private ClaimsPrincipal _user = new(new ClaimsIdentity());

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_user.Identity?.IsAuthenticated == true)
        {
            return new AuthenticationState(_user);
        }

        var token = await tokens.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            return new AuthenticationState(_user);
        }

        var email = await tokens.GetEmailAsync();
        _user = Build(email ?? "student", await tokens.GetFullNameAsync());
        return new AuthenticationState(_user);
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        var client = factory.CreateClient(SaplingApi.Anonymous);
        var response = await client.PostAsJsonAsync("api/identity/login?useCookies=false", new { email, password });

        if (!response.IsSuccessStatusCode)
        {
            // The server turns institute accounts away here, because that head only exists on the website.
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden
                && await response.Content.ReadFromJsonAsync<AccountError>() is { } refusal)
            {
                return new AuthResult(false, refusal.Message);
            }

            return new AuthResult(false, "That email and password combination did not match.");
        }

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
        {
            return new AuthResult(false, "The server did not return a session. Try again.");
        }

        await tokens.SaveAsync(payload.AccessToken, payload.RefreshToken, email);
        _user = Build(email, await FetchFullNameAsync());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_user)));
        return new AuthResult(true, null);
    }

    /// <summary>WebAuthenticator is Android-only here; the Windows head has no callback handler for the app link.</summary>
    public static bool GoogleSignInSupported => DeviceInfo.Platform == DevicePlatform.Android;

    public async Task<AuthResult> SignInWithGoogleAsync()
    {
        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(new Uri(SaplingApi.BaseAddress), "account/google/mobile"),
                new Uri(ExternalAuth.AppCallback));
        }
        catch (TaskCanceledException)
        {
            return new AuthResult(false, null);
        }

        if (result.Properties.TryGetValue("error", out var error) || !result.Properties.TryGetValue("code", out var code))
        {
            return new AuthResult(false, ExternalSignInErrors.Message(error));
        }

        var client = factory.CreateClient(SaplingApi.Anonymous);
        var response = await client.PostAsJsonAsync("api/identity/google/exchange", new { code });
        var payload = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<LoginResponse>() : null;
        if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
        {
            return new AuthResult(false, ExternalSignInErrors.Message(null));
        }

        using var infoRequest = new HttpRequestMessage(HttpMethod.Get, "api/identity/manage/info");
        infoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        using var infoResponse = await client.SendAsync(infoRequest);
        var info = infoResponse.IsSuccessStatusCode ? await infoResponse.Content.ReadFromJsonAsync<InfoResponse>() : null;
        var email = info?.Email ?? "student";

        await tokens.SaveAsync(payload.AccessToken, payload.RefreshToken, email);
        _user = Build(email, await FetchFullNameAsync());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_user)));
        return new AuthResult(true, null, result.Properties.ContainsKey("new"));
    }

    private async Task<string?> FetchFullNameAsync()
    {
        try
        {
            var client = factory.CreateClient(SaplingApi.Authenticated);
            var profile = await client.GetFromJsonAsync<Shared.Contracts.StudentProfileDto>("api/profile");
            if (!string.IsNullOrWhiteSpace(profile?.FullName))
            {
                await tokens.SaveFullNameAsync(profile.FullName);
                return profile.FullName;
            }
        }
        catch (Exception)
        {
            // The shell falls back to the email when the name is unavailable.
        }

        return null;
    }

    public async Task<AuthResult> SendCodeAsync(string purpose, string email)
    {
        var client = factory.CreateClient(SaplingApi.Anonymous);
        return await ToResultAsync(await client.PostAsJsonAsync($"api/account/{purpose}/code", new SendCodeRequest(email)));
    }

    public async Task<AuthResult> VerifyCodeAsync(string purpose, string email, string code)
    {
        var client = factory.CreateClient(SaplingApi.Anonymous);
        var response = await client.PostAsJsonAsync($"api/account/{purpose}/verify", new VerifyCodeRequest(email, code));
        if (!response.IsSuccessStatusCode)
        {
            return await ToResultAsync(response);
        }

        var verified = await response.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        return new AuthResult(true, null, Ticket: verified?.Ticket);
    }

    public async Task<AuthResult> CompleteRegistrationAsync(string email, string ticket, string password)
    {
        var client = factory.CreateClient(SaplingApi.Anonymous);
        var result = await ToResultAsync(await client.PostAsJsonAsync(
            "api/account/register", new CompleteRegistrationRequest(email, ticket, password, null)));

        return result.Succeeded ? await SignInAsync(email, password) with { IsNewUser = true } : result;
    }

    public async Task<AuthResult> ResetPasswordAsync(string email, string ticket, string password)
    {
        var client = factory.CreateClient(SaplingApi.Anonymous);
        var result = await ToResultAsync(await client.PostAsJsonAsync(
            "api/account/reset-password", new ResetPasswordRequest(email, ticket, password)));

        return result.Succeeded ? await SignInAsync(email, password) : result;
    }

    private static async Task<AuthResult> ToResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return new AuthResult(true, null);
        }

        AccountError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<AccountError>();
        }
        catch (Exception)
        {
            // A proxy or an older server may answer with HTML; fall through to the generic message.
        }

        return new AuthResult(false, error?.Message ?? "Something went wrong. Try again.", Restart: error?.Restart ?? false);
    }

    private readonly SemaphoreSlim _renewing = new(1, 1);

    /// <summary>
    /// Trades the refresh token for a new access token. Access tokens last about an hour, so without this
    /// every request fails once it lapses and the app is stuck until its data is cleared.
    /// </summary>
    /// <param name="staleToken">The access token the failed request used.</param>
    /// <returns>The access token to retry with, or null when the session cannot be renewed.</returns>
    public async Task<string?> RenewAsync(string? staleToken, CancellationToken ct)
    {
        await _renewing.WaitAsync(ct);
        try
        {
            // Several requests usually fail together; the first one renews and the rest reuse its token.
            var current = await tokens.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(current) && current != staleToken)
            {
                return current;
            }

            var refreshToken = await tokens.GetRefreshTokenAsync();
            if (string.IsNullOrEmpty(refreshToken))
            {
                return null;
            }

            var client = factory.CreateClient(SaplingApi.Anonymous);
            using var response = await client.PostAsJsonAsync("api/identity/refresh", new { refreshToken }, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                return null;
            }

            await tokens.SaveAsync(payload.AccessToken, payload.RefreshToken, await tokens.GetEmailAsync() ?? "student");
            return payload.AccessToken;
        }
        finally
        {
            _renewing.Release();
        }
    }

    public async Task SignOutAsync()
    {
        await tokens.ClearAsync();
        _user = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_user)));
    }

    private static ClaimsPrincipal Build(string email, string? fullName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Email, email),
        };

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            claims.Add(new Claim("full_name", fullName));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "sapling-bearer"));
    }

    private sealed record LoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("refreshToken")] string? RefreshToken);

    private sealed record InfoResponse([property: JsonPropertyName("email")] string? Email);
}

public sealed class TokenStore
{
    private const string AccessKey = "sapling.access";
    private const string RefreshKey = "sapling.refresh";
    private const string EmailKey = "sapling.email";
    private const string NameKey = "sapling.name";

    public async Task<string?> GetAccessTokenAsync() => await SafeGetAsync(AccessKey);

    public async Task<string?> GetRefreshTokenAsync() => await SafeGetAsync(RefreshKey);

    public async Task<string?> GetEmailAsync() => await SafeGetAsync(EmailKey);

    public async Task<string?> GetFullNameAsync() => await SafeGetAsync(NameKey);

    public Task SaveFullNameAsync(string fullName) => SecureStorage.Default.SetAsync(NameKey, fullName);

    public async Task SaveAsync(string accessToken, string? refreshToken, string email)
    {
        await SecureStorage.Default.SetAsync(AccessKey, accessToken);
        await SecureStorage.Default.SetAsync(EmailKey, email);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await SecureStorage.Default.SetAsync(RefreshKey, refreshToken);
        }
    }

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(AccessKey);
        SecureStorage.Default.Remove(RefreshKey);
        SecureStorage.Default.Remove(EmailKey);
        SecureStorage.Default.Remove(NameKey);
        return Task.CompletedTask;
    }

    private static async Task<string?> SafeGetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception)
        {
            // A corrupted keystore entry should log the user out, not crash the app.
            return null;
        }
    }
}

/// <summary>
/// Adds the access token to every API call. When the server says the token has lapsed, it renews the session
/// and sends the request again; if the session cannot be renewed the student is signed out, which sends them
/// to the sign-in screen instead of leaving every page failing.
/// </summary>
public sealed class BearerTokenHandler(TokenStore tokens, IServiceProvider services) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync();

        // A request can only be sent once, so its body is kept in case it has to go again.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        Authorize(request, token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || string.IsNullOrEmpty(token))
        {
            return response;
        }

        // Resolved here, not in the constructor: the auth service itself creates HTTP clients.
        var auth = services.GetRequiredService<ApiAuthService>();
        string? renewed;
        try
        {
            renewed = await auth.RenewAsync(token, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Offline or the server is down: that is not a reason to sign the student out.
            return response;
        }

        if (string.IsNullOrEmpty(renewed))
        {
            response.Dispose();
            await auth.SignOutAsync();

            // A cancelled call ends the page's load quietly; the router is already moving to sign-in.
            throw new OperationCanceledException("The session has expired.");
        }

        response.Dispose();
        using var retry = Clone(request, body);
        Authorize(retry, renewed);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static void Authorize(HttpRequestMessage request, string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null && request.Content is not null)
        {
            copy.Content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return copy;
    }
}
