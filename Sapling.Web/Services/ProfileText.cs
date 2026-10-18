using System.Globalization;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>The plain-text fact block about a student that prompts are grounded on. The model may use nothing else.</summary>
internal static class ProfileText
{
    public static string Facts(StudentProfile p)
    {
        var skills = p.Skills.Select(s => s.Skill?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var name = p.User?.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Create(CultureInfo.InvariantCulture, $"""
            First name: {(string.IsNullOrWhiteSpace(name) ? "unknown" : name)}
            College: {(string.IsNullOrWhiteSpace(p.College) ? "unknown" : p.College)}
            Course and branch: {(string.IsNullOrWhiteSpace(p.Branch) ? "unknown" : p.Branch)}
            Graduation year: {p.GraduationYear}
            CGPA: {(p.Cgpa > 0 ? p.Cgpa.ToString("0.0#", CultureInfo.InvariantCulture) : "unknown")}
            Skills they list: {(skills.Count == 0 ? "none listed" : string.Join(", ", skills))}
            """);
    }
}
