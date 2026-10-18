namespace Sapling.Shared.Content;

/// <summary>Courses and branches offered at onboarding. The career catalogue maps roles onto these same names.</summary>
public static class CourseCatalog
{
    public const string OtherCourse = "Other";
    public const string GeneralBranch = "General";

    public static readonly string[] Courses = ["B.Tech", "B.Sc", "BCA", "B.Com", "BBA", OtherCourse];

    private static readonly Dictionary<string, string[]> Branches = new()
    {
        ["B.Tech"] =
        [
            "Computer Science & Engineering", "Information Technology",
            "Electronics & Communication", "Electrical Engineering",
            "Mechanical Engineering", "Civil Engineering", "Other",
        ],
        ["B.Sc"] = ["Computer Science", "Mathematics", "Physics", "Chemistry", "Other"],
        ["BCA"] = ["BCA"],
        ["B.Com"] = [GeneralBranch],
        ["BBA"] = [GeneralBranch],
        [OtherCourse] = [GeneralBranch],
    };

    public static string[] BranchesFor(string? course) =>
        !string.IsNullOrEmpty(course) && Branches.TryGetValue(course, out var branches) ? branches : [];

    /// <summary>The stored form, e.g. "B.Tech - Civil Engineering".</summary>
    public static string Format(string course, string branch) => $"{course} - {branch}";

    /// <summary>
    /// Reads the profile's branch text back into a course and branch. Older profiles and the free-text profile
    /// editor may hold just a branch name, so that is matched too.
    /// </summary>
    public static (string Course, string Branch) Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return ("", "");
        }

        var text = stored.Replace('–', '-').Replace('—', '-').Trim();

        var separator = text.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0)
        {
            return (Canonical(text[..separator].Trim()), text[(separator + 3)..].Trim());
        }

        foreach (var (course, branches) in Branches)
        {
            if (text.StartsWith(course, StringComparison.OrdinalIgnoreCase))
            {
                var rest = text[course.Length..].TrimStart(' ', '-', ':');
                return (course, string.IsNullOrEmpty(rest) ? branches[0] : rest);
            }
        }

        foreach (var (course, branches) in Branches)
        {
            var match = branches.FirstOrDefault(b => string.Equals(b, text, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return (course, match);
            }
        }

        return (OtherCourse, GeneralBranch);
    }

    private static string Canonical(string course) =>
        Courses.FirstOrDefault(c => string.Equals(c, course, StringComparison.OrdinalIgnoreCase)) ?? course;
}
