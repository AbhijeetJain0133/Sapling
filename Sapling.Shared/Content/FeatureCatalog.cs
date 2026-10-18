namespace Sapling.Shared.Content;

/// <summary>Hue is the .hue-* class suffix and Art the SapArt scene; Stat is a short fact shown on landing tiles.</summary>
public sealed record Feature(
    string Key,
    string Title,
    string Blurb,
    string LongBlurb,
    string Icon,
    string Route,
    string Hue,
    string Art,
    string Stat);

/// <summary>Single source for the module list used by home cards, the nav, and the sign-in showcase.</summary>
public static class FeatureCatalog
{
    public const string CareerPaths = "career-paths";
    public const string Roadmap = "roadmap";
    public const string Opportunities = "opportunities";
    public const string Resume = "resume";
    public const string Interview = "interview";
    public const string GovtTrack = "govt-track";
    public const string Community = "community";
    public const string Profile = "profile";

    public static readonly IReadOnlyList<Feature> All =
    [
        new(CareerPaths, "Career & skills", "Pick a role and see what it takes.",
            "Every role your course leads to, ranked by fit. Choose one and see how well you fit it, the work it involves, and exactly which skills you have and which are missing.",
            "compass", "/career", "career", "career", "104 roles"),

        new(Roadmap, "Roadmap", "NPTEL courses for what you're missing.",
            "Free NPTEL courses from the IITs for each skill your target role asks for and you don't have yet, most important first, with checkpoints to tick off as you study.",
            "route", "/roadmap", "roadmap", "roadmap", "Free IIT courses"),

        new(Opportunities, "Opportunities", "Internships and jobs matched to you.",
            "Campus drives, internships and jobs ranked by two-way fit. Nothing is hidden from you: roles you are not ready for show exactly what is missing.",
            "briefcase", "/opportunities", "opportunities", "opportunities", "Two-way fit"),

        new(Resume, "Resume", "Score it, fix it, or build it from scratch.",
            "Upload your PDF for an honest AI score and concrete fixes, or build one in an ATS-safe template. Edit the LaTeX beside a live PDF, let the AI apply each fix, and export PDF or LaTeX.",
            "file-text", "/resume", "resume", "resume", "ATS-safe"),

        new(Interview, "Mock interview", "Practise before it counts.",
            "A spoken interview for your target role with an AI interviewer who asks about your resume and follows up, then a detailed report on your answers, pauses and confidence.",
            "mic", "/interview", "interview", "interview", "Spoken, scored"),

        new(GovtTrack, "Exams", "Every exam coming up, on a calendar.",
            "GATE, CAT, UPSC, SSC, banking, railways and state exams on one calendar, each with a countdown and a link to its official site.",
            "calendar", "/exams", "govt", "govt", "14 exams tracked"),

        new(Community, "Community", "Your college's private feed.",
            "Workshops, events, openings and announcements posted by your own college. Only its students can see them, and only the college can post.",
            "users", "/community", "community", "community", "College-only"),
    ];

    public static readonly IReadOnlyList<Feature> HomeCards =
        All.ToList();

    /// <summary>Number of modules, for copy that counts them.</summary>
    public static int Count => All.Count;

    public static Feature? Find(string key) => All.FirstOrDefault(f => f.Key == key);
}
