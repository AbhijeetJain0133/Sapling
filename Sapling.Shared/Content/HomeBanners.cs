namespace Sapling.Shared.Content;

/// <summary>Art is a SapArt scene name and Hue the .hue-* class suffix the slide is coloured with.</summary>
public sealed record Banner(
    string Title,
    string Subtitle,
    string Art,
    string Hue,
    string Route,
    string Cta);

public static class HomeBanners
{
    public static readonly IReadOnlyList<Banner> All =
    [
        new("See the roles that fit you",
            "Ranked by your skills, interests and course, with the reasoning shown.",
            "career", "career", "/career", "See career paths"),

        new("GATE 2027 is in February",
            "See every upcoming exam on one calendar.",
            "govt", "govt", "/exams", "Open the calendar"),

        new("Free IIT courses for your gaps",
            "NPTEL courses matched to what your target role needs, with checkpoints to track.",
            "roadmap", "roadmap", "/roadmap", "Open roadmap"),

        new("What your college is running this month",
            "Workshops, events and openings posted by your college. Ask questions in the comments.",
            "community", "community", "/community", "Open community"),

        new("Practise a mock interview",
            "Speak with an AI interviewer for fifteen minutes, then see where you got stuck.",
            "interview", "interview", "/interview", "Start an interview"),

        new("Build an ATS-ready resume",
            "Upload yours for a score and fixes, or build one from scratch and edit it live.",
            "resume", "resume", "/resume", "Open resume builder"),
    ];
}
