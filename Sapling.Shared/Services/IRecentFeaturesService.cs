namespace Sapling.Shared.Services;

/// <summary>Device-local trail of the features a user opened, shown on the home screen.</summary>
public interface IRecentFeaturesService
{
    Task<IReadOnlyList<string>> GetAsync();

    Task RecordAsync(string featureKey);

    Task ClearAsync();
}
