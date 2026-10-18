using Microsoft.JSInterop;
using Sapling.Shared.Services;

namespace Sapling.Web.Services;

public sealed class BrowserRecentFeaturesService(IJSRuntime js) : IRecentFeaturesService
{
    private const string Key = "sapling-recent";
    private const int Max = 6;

    public async Task<IReadOnlyList<string>> GetAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("sapling.store.get", Key);
            return Split(raw);
        }
        catch (JSException)
        {
            return [];
        }
    }

    public async Task RecordAsync(string featureKey)
    {
        try
        {
            var current = (await GetAsync()).Where(k => k != featureKey).Take(Max - 1);
            var updated = new[] { featureKey }.Concat(current);
            await js.InvokeVoidAsync("sapling.store.set", Key, string.Join(',', updated));
        }
        catch (JSException)
        {
            // Prerendering or storage blocked.
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            await js.InvokeVoidAsync("sapling.store.remove", Key);
        }
        catch (JSException)
        {
            // Nothing to clear.
        }
    }

    private static string[] Split(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? [] : raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
