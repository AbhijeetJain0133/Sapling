using Sapling.Shared.Contracts;

namespace Sapling.Shared.Components;

/// <summary>
/// Mutable model behind the build-from-scratch wizard. Bullets and skills are edited as plain text (one per line,
/// comma-separated) and split in <see cref="ToDto"/>; entries with no name are dropped there too.
/// </summary>
public sealed class ResumeForm
{
    public ContactForm Contact { get; set; } = new();

    public string Summary { get; set; } = "";

    public List<EducationForm> Education { get; set; } = [];

    public List<ExperienceForm> Experience { get; set; } = [];

    public List<ProjectForm> Projects { get; set; } = [];

    public List<SkillGroupForm> Skills { get; set; } = [];

    public List<AchievementForm> Achievements { get; set; } = [];

    public static ResumeForm From(ResumeDataDto d) => new()
    {
        Contact = new ContactForm
        {
            FullName = d.Contact.FullName, Email = d.Contact.Email, Phone = d.Contact.Phone, Location = d.Contact.Location,
            LinkedIn = d.Contact.LinkedIn, GitHub = d.Contact.GitHub, Website = d.Contact.Website,
        },
        Summary = d.Summary,
        Education = d.Education.Select(e => new EducationForm
        {
            Institution = e.Institution, Degree = e.Degree, Field = e.Field, Start = e.Start, End = e.End, Grade = e.Grade,
            Highlights = string.Join('\n', e.Highlights),
        }).ToList(),
        Experience = d.Experience.Select(e => new ExperienceForm
        {
            Organisation = e.Organisation, Role = e.Role, Location = e.Location, Start = e.Start, End = e.End,
            Bullets = string.Join('\n', e.Bullets),
        }).ToList(),
        Projects = d.Projects.Select(p => new ProjectForm
        {
            Name = p.Name, Link = p.Link, Technologies = p.Technologies, Start = p.Start, End = p.End,
            Bullets = string.Join('\n', p.Bullets),
        }).ToList(),
        Skills = d.Skills.Select(g => new SkillGroupForm { Category = g.Category, Items = string.Join(", ", g.Items) }).ToList(),
        Achievements = d.Achievements.Select(a => new AchievementForm { Title = a.Title, Issuer = a.Issuer, Date = a.Date, Detail = a.Detail }).ToList(),
    };

    public ResumeDataDto ToDto() => new(
        new ResumeContactDto(T(Contact.FullName), T(Contact.Email), T(Contact.Phone), T(Contact.Location), T(Contact.LinkedIn), T(Contact.GitHub), T(Contact.Website)),
        T(Summary),
        Education.Where(e => !Blank(e.Institution))
            .Select(e => new ResumeEducationDto(T(e.Institution), T(e.Degree), T(e.Field), T(e.Start), T(e.End), T(e.Grade), Lines(e.Highlights))).ToList(),
        Experience.Where(e => !Blank(e.Organisation) || !Blank(e.Role))
            .Select(e => new ResumeExperienceDto(T(e.Organisation), T(e.Role), T(e.Location), T(e.Start), T(e.End), Lines(e.Bullets))).ToList(),
        Projects.Where(p => !Blank(p.Name))
            .Select(p => new ResumeProjectDto(T(p.Name), T(p.Link), T(p.Technologies), T(p.Start), T(p.End), Lines(p.Bullets))).ToList(),
        Skills.Select(g => new ResumeSkillGroupDto(T(g.Category), g.Items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()))
            .Where(g => g.Items.Count > 0).ToList(),
        Achievements.Where(a => !Blank(a.Title))
            .Select(a => new ResumeAchievementDto(T(a.Title), T(a.Issuer), T(a.Date), T(a.Detail))).ToList());

    private static string T(string? s) => s?.Trim() ?? "";

    private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);

    private static List<string> Lines(string? text) =>
        (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.TrimStart('•', '-', '*', ' ')).Where(l => l.Length > 0).ToList();

    public sealed class ContactForm
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Location { get; set; } = "";
        public string LinkedIn { get; set; } = "";
        public string GitHub { get; set; } = "";
        public string Website { get; set; } = "";
    }

    public sealed class EducationForm
    {
        public string Institution { get; set; } = "";
        public string Degree { get; set; } = "";
        public string Field { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string Grade { get; set; } = "";
        public string Highlights { get; set; } = "";
    }

    public sealed class ExperienceForm
    {
        public string Organisation { get; set; } = "";
        public string Role { get; set; } = "";
        public string Location { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string Bullets { get; set; } = "";
    }

    public sealed class ProjectForm
    {
        public string Name { get; set; } = "";
        public string Link { get; set; } = "";
        public string Technologies { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string Bullets { get; set; } = "";
    }

    public sealed class SkillGroupForm
    {
        public string Category { get; set; } = "";
        public string Items { get; set; } = "";
    }

    public sealed class AchievementForm
    {
        public string Title { get; set; } = "";
        public string Issuer { get; set; } = "";
        public string Date { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}
