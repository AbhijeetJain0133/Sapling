using System.Text.Json;
using System.Text.RegularExpressions;
using Sapling.Shared.Contracts;

namespace Sapling.Web.Services;

public interface ICollegeCatalogService
{
    IReadOnlyList<CollegeDto> Search(string query, string? state = null, int limit = 20);
    Task InitializeAsync(CancellationToken ct = default);
}

public sealed partial class CollegeCatalogService : ICollegeCatalogService
{
    private readonly ILogger<CollegeCatalogService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly List<IndexedCollege> _colleges = [];
    private readonly Lock _initLock = new();
    private bool _initialized;

    public CollegeCatalogService(ILogger<CollegeCatalogService> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await Task.Run(() =>
        {
            lock (_initLock)
            {
                if (_initialized) return;

                try
                {
                    var candidates = new[]
                    {
                        Path.Combine(AppContext.BaseDirectory, "Data", "aicte_colleges.json"),
                        Path.Combine(_env.ContentRootPath, "Data", "aicte_colleges.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "Data", "aicte_colleges.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "Sapling.Web", "Data", "aicte_colleges.json"),
                        @"c:\Users\Abhijeet\source\repos\Sapling\Sapling.Web\Data\aicte_colleges.json",
                    };

                    var jsonPath = candidates.FirstOrDefault(File.Exists);
                    if (jsonPath is null)
                    {
                        _logger.LogWarning("College catalog dataset (aicte_colleges.json) not found in candidate paths.");
                        return;
                    }

                    using var stream = File.OpenRead(jsonPath);
                    var items = JsonSerializer.Deserialize<List<RawCollegeItem>>(stream);
                    if (items is null || items.Count == 0)
                    {
                        _logger.LogWarning("College catalog dataset is empty.");
                        return;
                    }

                    _colleges.Clear();
                    _colleges.Capacity = items.Count;

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.name)) continue;

                        var name = item.name.Trim();
                        var state = (item.state ?? "").Trim();
                        var city = (item.city ?? "").Trim();
                        var category = string.IsNullOrWhiteSpace(item.category) ? "AICTE Approved" : item.category.Trim();
                        var aliases = (item.aliases ?? "").Trim();

                        var nameLower = name.ToLowerInvariant();
                        var aliasLower = aliases.ToLowerInvariant();
                        var cityLower = city.ToLowerInvariant();
                        var stateLower = state.ToLowerInvariant();

                        var nameWordsList = WordRegex().Matches(nameLower).Select(m => m.Value).ToArray();
                        var aliasWordsList = WordRegex().Matches(aliasLower).Select(m => m.Value).ToArray();

                        var isPremier = category.Contains("National Importance", StringComparison.OrdinalIgnoreCase)
                                     || category.Contains("Premier", StringComparison.OrdinalIgnoreCase);

                        _colleges.Add(new IndexedCollege(
                            Name: name,
                            State: state,
                            City: city,
                            AicteId: item.aicte_id,
                            Category: category,
                            NameLower: nameLower,
                            AliasLower: aliasLower,
                            CityLower: cityLower,
                            StateLower: stateLower,
                            NameWordsSet: new HashSet<string>(nameWordsList),
                            NameWordsList: nameWordsList,
                            AliasWordsSet: new HashSet<string>(aliasWordsList),
                            IsPremier: isPremier,
                            NameLength: name.Length
                        ));
                    }

                    _initialized = true;
                    _logger.LogInformation("CollegeCatalogService initialized successfully with {Count} colleges.", _colleges.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize CollegeCatalogService.");
                }
            }
        }, ct);
    }

    public IReadOnlyList<CollegeDto> Search(string query, string? state = null, int limit = 20)
    {
        if (!_initialized)
        {
            lock (_initLock)
            {
                if (!_initialized)
                {
                    InitializeAsync().GetAwaiter().GetResult();
                }
            }
        }

        var normalizedQuery = (query ?? "").Trim().ToLowerInvariant();
        var normalizedState = string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToLowerInvariant();

        // If query is empty but state is provided, return premier/top colleges in that state
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            if (normalizedState is null) return [];

            return _colleges
                .Where(c => c.StateLower.Contains(normalizedState))
                .OrderByDescending(c => c.IsPremier)
                .ThenBy(c => c.NameLength)
                .Take(limit)
                .Select(MapDto)
                .ToList();
        }

        var tokens = WordRegex().Matches(normalizedQuery).Select(m => m.Value).ToArray();
        if (tokens.Length == 0) return [];

        var scored = new List<(int Score, IndexedCollege College)>(Math.Min(100, _colleges.Count));

        foreach (var c in _colleges)
        {
            if (normalizedState is not null && !c.StateLower.Contains(normalizedState))
            {
                continue;
            }

            var collegeScore = 0;
            var allTokensMatched = true;

            foreach (var token in tokens)
            {
                var tokenMatched = false;
                var tokenScore = 0;

                // 1. Exact acronym / alias word match (e.g. "iit", "nit", "bits", "coep", "sgsits", "vjti", "dtu")
                if (c.AliasWordsSet.Contains(token))
                {
                    tokenScore += 1000;
                    tokenMatched = true;
                }
                // 2. Exact word in college name
                else if (c.NameWordsSet.Contains(token))
                {
                    tokenScore += 600;
                    tokenMatched = true;
                }
                // 3. Word in college name starts with token
                else if (c.NameWordsList.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
                {
                    tokenScore += 350;
                    tokenMatched = true;
                }
                // 4. Word in aliases starts with token
                else if (c.AliasWordsSet.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
                {
                    tokenScore += 300;
                    tokenMatched = true;
                }
                // 5. City matches token
                else if (c.CityLower.Contains(token, StringComparison.Ordinal))
                {
                    tokenScore += 250;
                    tokenMatched = true;
                }
                // 6. State matches token
                else if (c.StateLower.Contains(token, StringComparison.Ordinal))
                {
                    tokenScore += 200;
                    tokenMatched = true;
                }
                // 7. Substring in name (only for tokens > 3 chars to prevent false matches like "nit" in "unity")
                else if (token.Length > 3 && c.NameLower.Contains(token, StringComparison.Ordinal))
                {
                    tokenScore += 50;
                    tokenMatched = true;
                }

                if (!tokenMatched)
                {
                    allTokensMatched = false;
                    break;
                }

                collegeScore += tokenScore;
            }

            if (!allTokensMatched) continue;

            // Boosts for quality matches
            if (c.NameLower.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                collegeScore += 500;
            }
            else if (c.AliasLower.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                collegeScore += 500;
            }

            if (c.NameLower.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                collegeScore += 400;
            }

            if (c.IsPremier)
            {
                collegeScore += 250;
            }

            // Brevity bonus: concise college names rank slightly higher than verbose paragraphs
            collegeScore += Math.Max(0, 50 - c.NameLength / 2);

            scored.Add((collegeScore, c));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => MapDto(x.College))
            .ToList();
    }

    private static CollegeDto MapDto(IndexedCollege c) =>
        new(c.Name, c.State, c.City, c.AicteId, c.Category);

    private sealed record RawCollegeItem(
        string? name,
        string? state,
        string? city,
        string? aicte_id,
        string? category,
        string? aliases);

    private sealed record IndexedCollege(
        string Name,
        string State,
        string City,
        string? AicteId,
        string Category,
        string NameLower,
        string AliasLower,
        string CityLower,
        string StateLower,
        HashSet<string> NameWordsSet,
        string[] NameWordsList,
        HashSet<string> AliasWordsSet,
        bool IsPremier,
        int NameLength);

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex WordRegex();
}
