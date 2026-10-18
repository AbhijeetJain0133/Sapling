using Sapling.Web.Data;

namespace Sapling.Web.Services;

/// <summary>
/// The skills a student has told us they have. We take their word for it. A broad O*NET knowledge area
/// (for example "Computers &amp; electronics") also counts as covered when they have enough of the specific
/// skills listed as evidence for it in careers.json (Data structures, DBMS...).
/// </summary>
public sealed class StudentSkillSet
{
    private readonly HashSet<int> _ids;
    private readonly HashSet<string> _names;
    private readonly IReadOnlyDictionary<string, string[]> _evidence;

    public StudentSkillSet(StudentProfile profile, IReadOnlyDictionary<string, string[]>? evidence)
    {
        _ids = profile.Skills.Select(s => s.SkillId).ToHashSet();
        _names = profile.Skills.Where(s => s.Skill is not null).Select(s => s.Skill!.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _evidence = evidence ?? new Dictionary<string, string[]>();
    }

    public bool Has(int skillId, string? skillName)
    {
        if (_ids.Contains(skillId))
        {
            return true;
        }

        if (skillName is null || !_evidence.TryGetValue(skillName, out var names) || names.Length == 0)
        {
            return false;
        }

        // Two pieces of evidence (or the only one listed) are enough to count the whole area.
        return names.Count(_names.Contains) >= Math.Min(2, names.Length);
    }
}
