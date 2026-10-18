using Microsoft.AspNetCore.Identity;

namespace Sapling.Web.Data;

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = "";

    /// <summary>One of <see cref="AccountTypes"/>. Decides which shell the sign-in lands in.</summary>
    public string AccountType { get; set; } = AccountTypes.Student;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public StudentProfile? Profile { get; set; }
}

public static class AccountTypes
{
    public const string Student = "student";

    public const string Institution = "institution";

    public const string Admin = "admin";
}

/// <summary>
/// Deactivation reuses Identity's lockout, so every password path already refuses the account without extra checks.
/// A far-future end date is the sentinel that tells an admin block apart from a temporary failed-attempt lockout.
/// </summary>
public static class AccountStatus
{
    public static readonly DateTimeOffset DeactivatedUntil = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    // Compared against a threshold rather than the exact value, since SQLite stores the date as text.
    private static readonly DateTimeOffset Threshold = new(9990, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsDeactivated(DateTimeOffset? lockoutEnd) => lockoutEnd >= Threshold;

    public static bool IsDeactivated(AppUser user) => IsDeactivated(user.LockoutEnd);
}

public class StudentProfile
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";

    public AppUser? User { get; set; }

    public string College { get; set; } = "";

    public string Branch { get; set; } = "";

    public int GraduationYear { get; set; }

    public string State { get; set; } = "";

    public string City { get; set; } = "";

    public double Cgpa { get; set; }

    public int Backlogs { get; set; }

    public string PreferredLanguage { get; set; } = "English";

    public bool OnboardingComplete { get; set; }

    public string? RiasecCode { get; set; }

    public int TargetRoleId { get; set; }

    /// <summary>False while TargetRoleId is only the course's default; the career page asks the student to choose.</summary>
    public bool TargetChosen { get; set; }

    public List<StudentSkill> Skills { get; set; } = [];

    public List<OpportunityApplication> Applications { get; set; } = [];

    public List<StudentResume> Resumes { get; set; } = [];

    public List<MockInterview> MockInterviews { get; set; } = [];

    /// <summary>Cropped profile photo stored as a Base64 PNG data-URL. Null means "use initials".</summary>
    public string? AvatarDataUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}


public class College
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string State { get; set; } = "";

    public string City { get; set; } = "";

    public string? AicteId { get; set; }

    public string Category { get; set; } = "AICTE Approved";

    public string Aliases { get; set; } = "";
}

public class Skill
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Category { get; set; } = "";
}

public class StudentSkill
{
    public int Id { get; set; }

    public int StudentProfileId { get; set; }

    public int SkillId { get; set; }

    public Skill? Skill { get; set; }
}

/// <summary>An occupation imported from O*NET via Data/careers.json; see tools/careers for how it is built.</summary>
public class CareerRole
{
    public int Id { get; set; }

    public string OnetCode { get; set; } = "";

    public string Title { get; set; } = "";

    public string OnetTitle { get; set; } = "";

    public string Family { get; set; } = "";

    public string Description { get; set; } = "";

    public string Tasks { get; set; } = "";

    public string AlsoCalled { get; set; } = "";

    public string Technologies { get; set; } = "";

    public int JobZone { get; set; }

    /// <summary>O*NET Bright Outlook categories, e.g. "Rapid Growth|Numerous Job Openings". Empty if not listed.</summary>
    public string Outlook { get; set; } = "";

    public string Preparation { get; set; } = "";

    public string Education { get; set; } = "";

    /// <summary>O*NET interest ratings (1 to 7) as "Realistic:6.43|Investigative:5.15|...".</summary>
    public string Interests { get; set; } = "";

    public string RelatedCodes { get; set; } = "";

    public List<RoleSkillRequirement> Requirements { get; set; } = [];

    public List<CourseRoleLink> CourseLinks { get; set; } = [];
}

/// <summary>Which course and branch lead to a role. Branch "*" means any branch of the course.</summary>
public class CourseRoleLink
{
    public int Id { get; set; }

    public int CareerRoleId { get; set; }

    public string Course { get; set; } = "";

    public string Branch { get; set; } = "*";

    public string Relevance { get; set; } = "Core";
}

public class RoleSkillRequirement
{
    public int Id { get; set; }

    public int CareerRoleId { get; set; }

    public int SkillId { get; set; }

    public Skill? Skill { get; set; }

    /// <summary>Technology, Skill or Knowledge, following the O*NET domain the requirement came from.</summary>
    public string Kind { get; set; } = "Technology";

    public int Impact { get; set; }

    public int Effort { get; set; }

    public int WeeksToClose { get; set; }

    public string Rationale { get; set; } = "";
}

/// <summary>A free course from NPTEL or Microsoft Learn, imported through tools/careers; see careers.json.</summary>
public class LearningCourse
{
    public int Id { get; set; }

    public string Provider { get; set; } = "NPTEL";

    /// <summary>The provider's own id: an NPTEL course number or a Microsoft Learn path uid.</summary>
    public string ExternalId { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Who teaches it, e.g. "Prof. Partha Pratim Das, IIT Kharagpur" or "Microsoft".</summary>
    public string Byline { get; set; } = "";

    public string Url { get; set; } = "";

    public int Lessons { get; set; }

    public int Minutes { get; set; }

    public List<CourseCheckpoint> Checkpoints { get; set; } = [];
}

/// <summary>A block of NPTEL lectures or one Microsoft Learn module, roughly a sitting or a week of study.</summary>
public class CourseCheckpoint
{
    public int Id { get; set; }

    public int LearningCourseId { get; set; }

    public int Order { get; set; }

    public string Title { get; set; } = "";

    public int Lessons { get; set; }

    public int Minutes { get; set; }
}

/// <summary>A course offered for a skill. Match is "direct" or "foundation".</summary>
public class SkillCourse
{
    public int Id { get; set; }

    public int SkillId { get; set; }

    public int LearningCourseId { get; set; }

    public string Match { get; set; } = "direct";
}

/// <summary>Which of a skill's courses the student chose to follow; only that one counts towards progress.</summary>
public class StudentCourseChoice
{
    public int Id { get; set; }

    public int StudentProfileId { get; set; }

    public int SkillId { get; set; }

    public int LearningCourseId { get; set; }
}

public class CheckpointProgress
{
    public int Id { get; set; }

    public int StudentProfileId { get; set; }

    public int CourseCheckpointId { get; set; }

    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Opportunity
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string Company { get; set; } = "";

    public string Kind { get; set; } = "Internship";

    public string Location { get; set; } = "";

    public bool Remote { get; set; }

    public string Stipend { get; set; } = "";

    public DateOnly ClosesOn { get; set; }

    public int MatchScore { get; set; }

    public string MissingSkills { get; set; } = "";

    public string Reasons { get; set; } = "";

    public string Description { get; set; } = "";
}

public class OpportunityApplication
{
    public int Id { get; set; }

    public int StudentProfileId { get; set; }

    public int OpportunityId { get; set; }

    public Opportunity? Opportunity { get; set; }

    public string Status { get; set; } = "Applied";

    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One saved resume. Once the student starts editing, Latex is the source of truth; DataJson is the
/// structured content it was first rendered from (null only if the student never had one).
/// </summary>
public class StudentResume
{
    public int Id { get; set; }

    public int StudentProfileId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>single-column | two-column (ResumeTemplates.*)</summary>
    public string Template { get; set; } = "single-column";

    /// <summary>upload | wizard | prompt</summary>
    public string Source { get; set; } = "wizard";

    public string? SourceFileName { get; set; }

    /// <summary>Text extracted from the uploaded PDF, capped at 12k characters. Null for built resumes.</summary>
    public string? SourceText { get; set; }

    public string Latex { get; set; } = "";

    /// <summary>Serialised ResumeDataDto.</summary>
    public string? DataJson { get; set; }

    public int? Score { get; set; }

    /// <summary>Serialised ResumeAnalysisDto: sub-scores and suggestions with their Applied flags.</summary>
    public string? AnalysisJson { get; set; }

    public DateTime? AnalysedAtUtc { get; set; }

    // DateTime rather than DateTimeOffset: SQLite cannot ORDER BY a DateTimeOffset column.
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MockInterview
{
    public int Id { get; set; }

    public int StudentProfileId { get; set; }

    public int? CareerRoleId { get; set; }

    public string RoleTitle { get; set; } = "";

    public string Instructions { get; set; } = "";

    public string? ResumeText { get; set; }

    public string? ResumeFileName { get; set; }

    /// <summary>Setup | Ready | Live | Analysing | Complete</summary>
    public string Status { get; set; } = "Setup";

    /// <summary>Intro | Background | Technical | Behavioural | Closing</summary>
    public string Phase { get; set; } = "Intro";

    /// <summary>Hard cap on interviewer turns, a safety net behind the time target.</summary>
    public int QuestionLimit { get; set; } = 20;

    /// <summary>Planned length (10, 20 or 30). The interviewer may run shorter or longer as the interview goes.</summary>
    public int TargetMinutes { get; set; } = 20;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>The finished report, serialised InterviewReportDto. Written once, always read whole.</summary>
    public string? ReportJson { get; set; }

    public int? OverallScore { get; set; }

    public List<MockInterviewTurn> Turns { get; set; } = [];
}

public class MockInterviewTurn
{
    public int Id { get; set; }

    public int MockInterviewId { get; set; }

    public int Order { get; set; }

    /// <summary>Interviewer | Candidate</summary>
    public string Speaker { get; set; } = "Interviewer";

    public string Phase { get; set; } = "Intro";

    public string Text { get; set; } = "";

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    // Delivery measurements for candidate turns, taken in the browser.
    public int ResponseDelayMs { get; set; }

    public int SpeakingMs { get; set; }

    public int LongPauses { get; set; }

    public int LongestPauseMs { get; set; }

    public int WordCount { get; set; }

    public int FillerCount { get; set; }

    public bool Typed { get; set; }

    // The interviewer's private judgement of this answer, made when it chose the next question.
    public int? AssessmentScore { get; set; }

    public string? AssessmentNote { get; set; }
}

public class GovtExam
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Authority { get; set; } = "";

    public string Level { get; set; } = "State";

    public DateOnly? NotificationOn { get; set; }

    public DateOnly? ExamOn { get; set; }

    public int MinAge { get; set; }

    public int MaxAge { get; set; }

    public string QualificationRequired { get; set; } = "";

    public bool RequiresMpDomicile { get; set; }

    public double MinCgpa { get; set; }

    public string SyllabusAreas { get; set; } = "";

    public string Summary { get; set; } = "";

    /// <summary>The conducting body's own page, never a coaching or news site.</summary>
    public string OfficialUrl { get; set; } = "";

    /// <summary>Last day of a multi-day exam window; null when the exam is a single day.</summary>
    public DateOnly? ExamEndsOn { get; set; }

    /// <summary>True when the dates come from a calendar that the conducting body may still revise.</summary>
    public bool DatesTentative { get; set; }

    /// <summary>How many years before graduating a student may sit it: 1 for final year, 2 from third year.</summary>
    public int OpenYearsBeforeGraduation { get; set; }
}

public class QuizQuestion
{
    public int Id { get; set; }

    public string Text { get; set; } = "";

    public string Dimension { get; set; } = "";

    public int Order { get; set; }
}

/// <summary>A college that publishes to its own community feed. Students join it by naming it in their profile.</summary>
public class Institution
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string ShortName { get; set; } = "";

    public string City { get; set; } = "";

    public string State { get; set; } = "";

    public bool Verified { get; set; }

    /// <summary>The single institute account that owns this college. Null while the row is an unclaimed placeholder.</summary>
    public string? UserId { get; set; }

    /// <summary>Logo stored as a Base64 PNG data-URL. Null means "use the generated tile".</summary>
    public string? LogoDataUrl { get; set; }

    public string About { get; set; } = "";

    public string Website { get; set; } = "";

    public string ContactEmail { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<CommunityPost> Posts { get; set; } = [];
}

public class CommunityPost
{
    public int Id { get; set; }

    public int InstitutionId { get; set; }

    public Institution? Institution { get; set; }

    public string Kind { get; set; } = "Announcement";

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    // DateTime rather than DateTimeOffset: SQLite cannot ORDER BY a DateTimeOffset column.
    public DateTime PostedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartsAtUtc { get; set; }

    public string? Venue { get; set; }

    public string? CtaLabel { get; set; }

    public string? CtaUrl { get; set; }

    /// <summary>Uploaded artwork as a data-URL, or null to fall back to a placeholder by kind.</summary>
    public string? ImageUrl { get; set; }

    public string Tags { get; set; } = "";

    /// <summary>Placeholder engagement for seeded posts; real votes are counted from <see cref="Upvotes"/>.</summary>
    public int BaseUpvotes { get; set; }

    /// <summary>Pinned posts sort above the rest of the college's feed.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Archived posts stay with the institution but disappear from the student feed.</summary>
    public bool IsArchived { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public List<PostUpvote> Upvotes { get; set; } = [];

    public List<PostComment> Comments { get; set; } = [];
}

/// <summary>A post a student bookmarked for later.</summary>
/// <summary>
/// A message in a college's student space. College is the lower-cased college name from the author's profile,
/// which is what makes the space private to that college's students.
/// </summary>
public class StudentMessage
{
    public int Id { get; set; }

    public string College { get; set; } = "";

    public int StudentProfileId { get; set; }

    public StudentProfile? Author { get; set; }

    public string Body { get; set; } = "";

    public DateTime PostedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Set when the author changes the text, so readers can see it was edited.</summary>
    public DateTime? EditedAtUtc { get; set; }
}

/// <summary>A student's bookmark on a student-space message; shown with saved posts on the Saved page.</summary>
public class SavedStudentMessage
{
    public int Id { get; set; }

    public int StudentMessageId { get; set; }

    public int StudentProfileId { get; set; }

    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SavedPost
{
    public int Id { get; set; }

    public int CommunityPostId { get; set; }

    public int StudentProfileId { get; set; }

    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PostUpvote
{
    public int Id { get; set; }

    public int CommunityPostId { get; set; }

    public int StudentProfileId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PostComment
{
    public int Id { get; set; }

    public int CommunityPostId { get; set; }

    /// <summary>Replies are one level deep; a reply always points at a top-level comment.</summary>
    public int? ParentCommentId { get; set; }

    /// <summary>Set when a student wrote it. Seeded peer comments have neither author id.</summary>
    public int? StudentProfileId { get; set; }

    /// <summary>Set when an institution replied as itself.</summary>
    public int? InstitutionId { get; set; }

    public Institution? Institution { get; set; }

    public string AuthorName { get; set; } = "";

    public string? AuthorHeadline { get; set; }

    public string Body { get; set; } = "";

    public DateTime PostedAtUtc { get; set; } = DateTime.UtcNow;
}
