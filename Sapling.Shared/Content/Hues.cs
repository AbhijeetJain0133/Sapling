namespace Sapling.Shared.Content;

/// <summary>
/// Picks a stable feature hue for things that have no hue of their own (companies, career families, skill
/// categories, institutions), so the same name is always the same colour.
/// </summary>
public static class Hues
{
    public static readonly IReadOnlyList<string> All =
        ["career", "roadmap", "opportunities", "resume", "interview", "govt", "community"];

    public static string ForKey(string? key)
    {
        // A plain character sum, not GetHashCode, which differs between processes.
        var sum = 0;
        foreach (var c in (key ?? "").Trim().ToLowerInvariant())
        {
            sum = unchecked(sum * 31 + c);
        }

        return All[(int)((uint)sum % (uint)All.Count)];
    }

    public static string ForId(int id) => All[(int)((uint)id % (uint)All.Count)];

    /// <summary>Up to two initials, e.g. "Persistent Systems" → "PS", "TCS (NQT)" → "TC".</summary>
    public static string Monogram(string? name)
    {
        var words = (name ?? "").Split([' ', '-', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToList();
        return words.Count switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}",
        };
    }
}
