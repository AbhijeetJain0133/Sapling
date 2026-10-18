using Sapling.Shared.Contracts;

namespace Sapling.Shared.Content;

/// <summary>Presentation rules for community posts, shared by the feed, the post page and the home dashboard.</summary>
public static class CommunityDisplay
{
    // Madhya Pradesh has no daylight saving, so a fixed offset is exact and behaves the same on server and device.
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    public static string Icon(string kind) => kind switch
    {
        PostKinds.Opportunity => "briefcase",
        PostKinds.Workshop => "graduation-cap",
        PostKinds.Event => "calendar",
        _ => "megaphone",
    };

    public static string Tone(string kind) => kind switch
    {
        PostKinds.Opportunity => "success",
        PostKinds.Workshop => "info",
        PostKinds.Event => "primary",
        _ => "neutral",
    };

    /// <summary>Feature hue used for a post kind's placeholder art and filled badge.</summary>
    public static string Hue(string kind) => kind switch
    {
        PostKinds.Opportunity => "opportunities",
        PostKinds.Workshop => "roadmap",
        PostKinds.Event => "community",
        _ => "govt",
    };

    /// <summary>
    /// Institutions cannot upload artwork yet, so a post without an image gets a placeholder chosen by its kind.
    /// The id picks between variants so two posts of the same kind do not sit next to each other looking identical.
    /// </summary>
    public static string Image(CommunityPostDto post)
    {
        if (!string.IsNullOrWhiteSpace(post.ImageUrl))
        {
            return post.ImageUrl;
        }

        var name = post.Kind switch
        {
            PostKinds.Workshop => post.Id % 2 == 0 ? "workshop-1" : "workshop-2",
            PostKinds.Event => post.Id % 2 == 0 ? "event-1" : "event-2",
            PostKinds.Opportunity => "opportunity-1",
            _ => "announcement-1",
        };

        return $"_content/Sapling.Shared/img/community/{name}.svg";
    }

    public static string Ago(DateTimeOffset at)
    {
        var elapsed = DateTimeOffset.UtcNow - at;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "Just now",
            { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)elapsed.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)elapsed.TotalDays}d ago",
            _ => at.ToOffset(Ist).ToString("d MMM"),
        };
    }

    public static string When(DateTimeOffset at) => at.ToOffset(Ist).ToString("ddd, d MMM · h:mm tt");
}
