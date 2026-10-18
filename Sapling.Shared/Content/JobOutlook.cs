namespace Sapling.Shared.Content;

/// <summary>O*NET Bright Outlook categories, as shown to students and used to sort career paths.</summary>
public static class JobOutlook
{
    public const string NumerousOpenings = "Numerous Job Openings";
    public const string RapidGrowth = "Rapid Growth";
    public const string NewAndEmerging = "New and Emerging";

    /// <summary>Many openings outranks growth alone: it means jobs exist now, not just a rising trend.</summary>
    public static int Rank(IReadOnlyList<string> outlook) =>
        (outlook.Contains(NumerousOpenings) ? 2 : 0)
        + (outlook.Contains(RapidGrowth) || outlook.Contains(NewAndEmerging) ? 1 : 0);

    public static string Label(string category) => category switch
    {
        NumerousOpenings => "Many openings",
        RapidGrowth => "Growing fast",
        NewAndEmerging => "New and emerging",
        _ => category,
    };
}
