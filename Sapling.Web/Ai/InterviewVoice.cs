using System.Security;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Sapling.Web.Ai;

/// <summary>
/// A natural-sounding voice for the interviewer. Optional: without it the browser's own speech voices are used.
/// Hugging Face has no text-to-speech service, so this uses Azure AI Speech neural voices
/// (free tier: 500,000 characters a month, roughly 250 interviews).
/// </summary>
public interface IInterviewVoice
{
    bool Available { get; }

    /// <summary>MP3 audio for the text, or null if synthesis failed.</summary>
    Task<byte[]?> SpeakAsync(string text, CancellationToken ct);
}

public sealed class BrowserOnlyVoice : IInterviewVoice
{
    public bool Available => false;

    public Task<byte[]?> SpeakAsync(string text, CancellationToken ct) => Task.FromResult<byte[]?>(null);
}

public sealed class AzureSpeechVoice(
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    ILogger<AzureSpeechVoice> log,
    string key,
    string region,
    string voiceName) : IInterviewVoice
{
    public const string DefaultVoice = "en-IN-NeerjaNeural";

    public bool Available => true;

    public async Task<byte[]?> SpeakAsync(string text, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > 1500)
        {
            return null;
        }

        // "Repeat question" and reloads ask for the same line again; don't pay twice.
        var cacheKey = $"voice:{voiceName}:{text}";
        if (cache.TryGetValue(cacheKey, out byte[]? cached))
        {
            return cached;
        }

        var lang = voiceName.Length >= 5 ? voiceName[..5] : "en-IN";
        var ssml = new StringBuilder()
            .Append(CultureInfoInvariant($"<speak version='1.0' xml:lang='{lang}'><voice name='{voiceName}'>"))
            .Append("<prosody rate='-4%'>")
            .Append(SecurityElement.Escape(text))
            .Append("</prosody></voice></speak>")
            .ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1")
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"),
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        request.Headers.UserAgent.ParseAdd("Sapling");

        try
        {
            using var response = await httpFactory.CreateClient("azure-speech").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Azure Speech returned {Status}; the browser voice is used instead.", (int)response.StatusCode);
                return null;
            }

            var audio = await response.Content.ReadAsByteArrayAsync(ct);
            cache.Set(cacheKey, audio, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                Size = null,
            });
            return audio;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(e, "Azure Speech call failed; the browser voice is used instead.");
            return null;
        }
    }

    private static string CultureInfoInvariant(FormattableString s) => FormattableString.Invariant(s);
}
