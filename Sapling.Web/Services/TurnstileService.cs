using System.Text.Json.Serialization;

namespace Sapling.Web.Services;

public interface ITurnstileService
{
    /// <summary>Verifies a Cloudflare Turnstile response token submitted with a form.</summary>
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}

/// <summary>
/// Calls Cloudflare's siteverify endpoint to confirm a Turnstile widget response before honouring a
/// sign-in, registration or password-reset submission. Fails closed: a missing token or secret key
/// never verifies.
/// </summary>
public sealed class TurnstileService(HttpClient http, IConfiguration config, IHostEnvironment env, ILogger<TurnstileService> logger) : ITurnstileService
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    // Cloudflare's published always-pass test secret, paired with the widget's test site key. Development only.
    private const string DevTestSecret = "1x0000000000000000000000000000000AA";

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        var secretKey = config["Turnstile:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) && env.IsDevelopment())
        {
            secretKey = DevTestSecret;
        }

        if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var fields = new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = token,
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            fields["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await http.PostAsync(VerifyUrl, new FormUrlEncodedContent(fields), ct);
            var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: ct);
            return result?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Cloudflare Turnstile verification request failed");
            return false;
        }
    }

    private sealed class TurnstileResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}
