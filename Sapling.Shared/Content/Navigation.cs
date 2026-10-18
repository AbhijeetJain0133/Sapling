namespace Sapling.Shared.Content;

/// <summary>Hue is the .hue-* class suffix used for the active state.</summary>
public sealed record NavEntry(string Label, string Route, string Icon, bool ExactMatch = false, string Hue = "profile");

public static class Navigation
{
    public static readonly IReadOnlyList<NavEntry> Primary =
    [
        new("Home", "/home", "home", true, "brand"),
        new("Career & skills", "/career", "compass", false, "career"),
        new("Roadmap", "/roadmap", "route", false, "roadmap"),
        new("Opportunities", "/opportunities", "briefcase", false, "opportunities"),
        new("Community", "/community", "users", false, "community"),
        new("Resume", "/resume", "file-text", false, "resume"),
        new("Mock interview", "/interview", "mic", false, "interview"),
        new("Exams", "/exams", "calendar", false, "govt"),
    ];

    public static readonly IReadOnlyList<NavEntry> Tabs =
    [
        new("Home", "/home", "home", true, "brand"),
        new("Career", "/career", "compass", false, "career"),
        new("Community", "/community", "users", false, "community"),
    ];

    /// <summary>How many tabs sit to the left of the centred Sapling AI button.</summary>
    public const int TabsBeforeAi = 2;

    public static readonly IReadOnlyList<NavEntry> MoreSheet =
    [
        new("Exams", "/exams", "calendar", false, "govt"),
        new("Roadmap", "/roadmap", "route", false, "roadmap"),
        new("Opportunities", "/opportunities", "briefcase", false, "opportunities"),
        new("Resume", "/resume", "file-text", false, "resume"),
        new("Mock interview", "/interview", "mic", false, "interview"),
        new("Profile & settings", "/profile", "user", false, "profile"),
    ];
}
