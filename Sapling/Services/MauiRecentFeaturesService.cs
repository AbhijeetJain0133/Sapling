using Sapling.Shared.Services;

namespace Sapling.Services;

public sealed class MauiRecentFeaturesService : IRecentFeaturesService
{
    private const string Key = "sapling-recent";
    private const int Max = 6;

    public Task<IReadOnlyList<string>> GetAsync()
    {
        var raw = Preferences.Default.Get(Key, string.Empty);
        IReadOnlyList<string> items = string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries);

        return Task.FromResult(items);
    }

    public async Task RecordAsync(string featureKey)
    {
        var current = (await GetAsync()).Where(k => k != featureKey).Take(Max - 1);
        Preferences.Default.Set(Key, string.Join(',', new[] { featureKey }.Concat(current)));
    }

    public Task ClearAsync()
    {
        Preferences.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
