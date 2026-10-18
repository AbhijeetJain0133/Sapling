namespace Sapling.Shared.Contracts;

public sealed record StudentProfileDto(
    string FullName,
    string Email,
    string College,
    string Branch,
    int GraduationYear,
    string City,
    double Cgpa,
    int Backlogs,
    string PreferredLanguage,
    bool OnboardingComplete,
    string? RiasecCode,
    string State = "",
    string? AvatarDataUrl = null);

public sealed record UpdateAvatarRequest(string? AvatarDataUrl);

public sealed record DeleteAccountRequest(string Confirmation)
{
    /// <summary>What the student types to confirm.</summary>
    public const string Word = "DELETE";
}

public sealed record UpdateProfileRequest(
    string FullName,
    string College,
    string Branch,
    int GraduationYear,
    string City,
    double Cgpa,
    int Backlogs,
    string PreferredLanguage,
    string State = "");

public sealed record CollegeDto(
    string Name,
    string State,
    string City,
    string? AicteId = null,
    string? Category = null);

public sealed record SkillDto(
    int Id,
    string Name,
    string Category);

public sealed record ScoreComponentDto(
    string Name,
    int Weight,
    int Value,
    string Summary);

/// <summary>One card on the career paths list. The fit score is worked out for the signed-in student.</summary>
public sealed record CareerPathSummaryDto(
    int Id,
    string Title,
    string Family,
    string Tier,
    int FitScore,
    string? CourseRelevance,
    string TopReason,
    bool IsTarget,
    IReadOnlyList<string> Outlook);

public sealed record CareerPathDto(
    int Id,
    string OnetCode,
    string Title,
    string OnetTitle,
    string Family,
    string Tier,
    int FitScore,
    int Have,
    int Total,
    FitBreakdownDto Fit,
    string? CourseRelevance,
    bool IsTarget,
    string Description,
    IReadOnlyList<string> Tasks,
    IReadOnlyList<string> AlsoCalled,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Outlook,
    string Preparation,
    string Education,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Considerations,
    IReadOnlyList<CareerRequirementDto> Requirements,
    IReadOnlyList<RelatedCareerDto> Related,
    CareerSourceDto Source);

/// <summary>Each part is 0 to 100. Interests is null until the quiz is taken; Course is null without a course.</summary>
public sealed record FitBreakdownDto(int Skills, int? Interests, int? Course);

public sealed record CareerRequirementDto(string Skill, string Kind, int Impact, bool Have, string Rationale);

public sealed record RelatedCareerDto(int Id, string Title);

public sealed record CareerSourceDto(
    string Name,
    string Publisher,
    string Url,
    string License,
    string LicenseUrl,
    string OccupationUrl,
    string ImportedOn,
    string OutlookSource,
    string CourseMapping);

/// <summary>One requirement of the target role. Have is true once the student has claimed the skill.</summary>
public sealed record SkillGapDto(
    int Id,
    string SkillName,
    string Kind,
    string Severity,
    int Impact,
    int Effort,
    int WeeksToClose,
    bool Have,
    string Rationale);

/// <summary>How much of the target role the student covers, weighted by O*NET importance.</summary>
public sealed record SkillCoverageDto(
    int RoleId,
    string RoleName,
    int Fit,
    int Have,
    int Total,
    bool TargetChosen);

/// <summary>
/// The student's roadmap to their target role: one step per required skill (or group of skills sharing an NPTEL
/// course), missing skills first and then by importance.
/// </summary>
public sealed record RoadmapDto(
    int RoleId,
    string RoleName,
    int CompletedCheckpoints,
    int TotalCheckpoints,
    IReadOnlyList<RoadmapStepDto> Steps,
    string Source);

/// <summary>
/// Impact is O*NET importance (1-10) for the role; Have is true once the student has every skill in the step.
/// Course is the one the student is following, picked from Options (one per provider that teaches it).
/// </summary>
public sealed record RoadmapStepDto(
    IReadOnlyList<RoadmapSkillDto> Skills,
    string Severity,
    int Impact,
    bool Have,
    RoadmapCourseDto Course,
    IReadOnlyList<RoadmapCourseDto> Options);

public sealed record RoadmapSkillDto(int Id, string Name, bool Have);

/// <summary>
/// Provider is "NPTEL" or "Microsoft Learn". Match is "direct" (the course teaches the skill) or "foundation"
/// (the subject underneath a tool). Lessons are NPTEL lectures or Microsoft Learn modules.
/// </summary>
public sealed record RoadmapCourseDto(
    int Id,
    string Provider,
    string Title,
    string Byline,
    string Url,
    int Lessons,
    int Minutes,
    string Match,
    IReadOnlyList<RoadmapCheckpointDto> Checkpoints);

public sealed record RoadmapCheckpointDto(int Id, string Title, int Lessons, int Minutes, bool Done);

public sealed record OpportunityDto(
    int Id,
    string Title,
    string Company,
    string Kind,
    string Location,
    bool Remote,
    string Stipend,
    DateOnly ClosesOn,
    int MatchScore,
    string Readiness,
    IReadOnlyList<string> MissingSkills,
    IReadOnlyList<string> Reasons,
    string Description,
    string? ApplicationStatus);

public static class ResumeTemplates
{
    public const string SingleColumn = "single-column";

    public const string TwoColumn = "two-column";

    public static readonly IReadOnlyList<(string Key, string Name, string Blurb)> All =
    [
        (SingleColumn, "Classic single column", "Standard headings top to bottom. Parses cleanly in every ATS."),
        (TwoColumn, "Compact two column", "Contact, skills and education in a narrow side column; experience and projects in the main one."),
    ];

    public static string NameOf(string key) => All.FirstOrDefault(t => t.Key == key).Name ?? key;
}

public sealed record ResumeSummaryDto(
    int Id,
    string Title,
    string Template,
    string Source,
    int? Score,
    bool AnalysisStale,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ResumeDto(
    int Id,
    string Title,
    string Template,
    string Source,
    string? SourceFileName,
    string Latex,
    ResumeDataDto? Data,
    ResumeAnalysisDto? Analysis,
    bool AnalysisStale,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ResumeAnalysisDto(
    int Overall,
    string Summary,
    IReadOnlyList<ResumeScoreDto> Scores,
    IReadOnlyList<ResumeSuggestionDto> Suggestions);

public sealed record ResumeScoreDto(string Area, int Score, string Comment);

/// <summary>Severity is High, Medium or Low. Applied is set once "Apply with AI" has run for it.</summary>
public sealed record ResumeSuggestionDto(int Id, string Section, string Issue, string Fix, string Severity, bool Applied);

public sealed record ResumeEditResultDto(ResumeDto Resume, string Note);

/// <summary>CompilerAvailable is false when the server has no LaTeX compiler; Log then explains how to install one.</summary>
public sealed record ResumeCompileResultDto(bool Ok, byte[]? Pdf, string Log, bool CompilerAvailable);

/// <summary>Structured resume content, shared by the wizard, the AI extractor and the LaTeX templates. Empty means omit.</summary>
public sealed record ResumeDataDto(
    ResumeContactDto Contact,
    string Summary,
    IReadOnlyList<ResumeEducationDto> Education,
    IReadOnlyList<ResumeExperienceDto> Experience,
    IReadOnlyList<ResumeProjectDto> Projects,
    IReadOnlyList<ResumeSkillGroupDto> Skills,
    IReadOnlyList<ResumeAchievementDto> Achievements);

public sealed record ResumeContactDto(string FullName, string Email, string Phone, string Location, string LinkedIn, string GitHub, string Website);

/// <summary>Dates are free text as the student writes them, e.g. "Aug 2023".</summary>
public sealed record ResumeEducationDto(string Institution, string Degree, string Field, string Start, string End, string Grade, IReadOnlyList<string> Highlights);

public sealed record ResumeExperienceDto(string Organisation, string Role, string Location, string Start, string End, IReadOnlyList<string> Bullets);

public sealed record ResumeProjectDto(string Name, string Link, string Technologies, string Start, string End, IReadOnlyList<string> Bullets);

public sealed record ResumeSkillGroupDto(string Category, IReadOnlyList<string> Items);

/// <summary>Certifications, awards and positions of responsibility.</summary>
public sealed record ResumeAchievementDto(string Title, string Issuer, string Date, string Detail);

/// <summary>Source is "wizard" or "prompt".</summary>
public sealed record CreateResumeRequest(string Title, string Template, string Source, ResumeDataDto Data);

public sealed record SaveResumeRequest(string Latex, string? Title = null);

public sealed record ResumeInstructionRequest(string Latex, string Instruction);

public sealed record DescribeYourselfRequest(string Description);

public sealed record InterviewSummaryDto(
    int Id,
    string RoleTitle,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int? OverallScore,
    int QuestionsAsked,
    bool HasResume,
    int TargetMinutes,
    int ActiveSeconds);

public sealed record InterviewTurnDto(
    int Id,
    int Order,
    string Speaker,
    string Phase,
    string Text,
    DateTimeOffset At,
    int ResponseDelayMs,
    int SpeakingMs,
    int LongPauses,
    int LongestPauseMs,
    int WordCount,
    int FillerCount,
    bool Typed,
    int? AssessmentScore,
    string? AssessmentNote);

/// <summary>ActiveSeconds counts time spent in the interview, leaving out long gaps such as leaving and coming back.</summary>
public sealed record InterviewDto(
    int Id,
    int? CareerRoleId,
    string RoleTitle,
    string Instructions,
    string Status,
    string Phase,
    int TargetMinutes,
    int ActiveSeconds,
    bool NaturalVoice,
    int QuestionsAsked,
    bool HasResume,
    string? ResumeFileName,
    int ResumeChars,
    string? ResumeWarning,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<InterviewTurnDto> Turns,
    InterviewReportDto? Report);

/// <summary>TargetMinutes is 10 (short), 20 (medium) or 30 (long).</summary>
public sealed record CreateInterviewRequest(int? CareerRoleId, string RoleTitle, string Instructions, int TargetMinutes = 20);

/// <summary>Delivery measurements for one spoken answer, taken in the browser.</summary>
public sealed record AnswerMetricsDto(
    int ResponseDelayMs,
    int SpeakingMs,
    int LongPauses,
    int LongestPauseMs,
    int WordCount,
    int FillerCount,
    bool Typed);

public sealed record SubmitAnswerRequest(string Text, AnswerMetricsDto Metrics);

/// <summary>What the interviewer says next. Done means this line closes the interview.</summary>
public sealed record InterviewerLineDto(
    string Say,
    string Phase,
    bool Done,
    int TurnOrder,
    int QuestionsAsked,
    int ActiveSeconds,
    int TargetMinutes);

public sealed record AreaScoreDto(string Area, int Score, string Comment);

public sealed record QuestionFeedbackDto(
    int TurnOrder,
    string Question,
    string AnswerSummary,
    int Score,
    string WhatWorked,
    string WhatToFix,
    bool GotStuck);

public sealed record DeliveryStatsDto(
    int Answers,
    int AverageResponseDelayMs,
    int TotalLongPauses,
    int LongestPauseMs,
    double WordsPerMinute,
    int FillerCount,
    double FillersPerMinute,
    int TotalSpeakingSeconds,
    int TypedAnswers,
    int ConfidenceScore);

public sealed record InterviewReportDto(
    int Overall,
    string Summary,
    string ConfidenceLevel,
    IReadOnlyList<AreaScoreDto> Areas,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> StuckPoints,
    IReadOnlyList<string> TopicsToImprove,
    IReadOnlyList<QuestionFeedbackDto> Questions,
    IReadOnlyList<string> NextSteps,
    DeliveryStatsDto Delivery);

public sealed record GovtExamDto(
    int Id,
    string Name,
    string Authority,
    string Level,
    DateOnly? NotificationOn,
    DateOnly? ExamOn,
    string Eligibility,
    IReadOnlyList<string> EligibilityReasons,
    IReadOnlyList<string> SyllabusAreas,
    string Summary,
    string OfficialUrl = "",
    DateOnly? ExamEndsOn = null,
    bool DatesTentative = false);

public sealed record QuizQuestionDto(
    int Id,
    string Text,
    string Dimension);

public sealed record QuizSubmissionRequest(IReadOnlyList<QuizAnswerDto> Answers);

public sealed record QuizAnswerDto(int QuestionId, int Value);

public sealed record QuizResultDto(string RiasecCode, IReadOnlyList<ScoreComponentDto> Dimensions);

public sealed record HomeSummaryDto(
    string FirstName,
    bool OnboardingComplete,
    int? TargetRoleId,
    string? TargetRole,
    int? TargetFit,
    int CompletedCheckpoints,
    int TotalCheckpoints,
    string? NextCheckpoint,
    int NewOpportunities,
    int ClosingSoonExams,
    int NewCommunityPosts = 0);

public sealed record SkillSuggestionDto(
    string Name,
    string Category);

/// <summary>
/// Skills to suggest at onboarding, most common first across the roles the student's course leads to.
/// Course is the label the suggestions are for, or null when the profile has no course yet.
/// </summary>
public sealed record SkillPickerDto(IReadOnlyList<string> Recommended, string? Course);

public static class PostKinds
{
    public const string Opportunity = "Opportunity";
    public const string Workshop = "Workshop";
    public const string Event = "Event";
    public const string Announcement = "Announcement";
}

public sealed record InstitutionDto(
    int Id,
    string Name,
    string ShortName,
    string City,
    bool Verified,
    string? LogoDataUrl = null);

public sealed record CommunityPostDto(
    int Id,
    InstitutionDto Author,
    string Kind,
    string Title,
    string Body,
    DateTimeOffset PostedAt,
    DateTimeOffset? StartsAt,
    string? Venue,
    string? CtaLabel,
    string? CtaUrl,
    IReadOnlyList<string> Tags,
    int Upvotes,
    bool HasUpvoted,
    int CommentCount,
    string? ImageUrl = null,
    bool IsPinned = false,
    bool IsSaved = false);

public sealed record CommunityCommentDto(
    int Id,
    int? ParentId,
    string AuthorName,
    string? AuthorHeadline,
    bool IsInstitution,
    bool IsVerified,
    bool IsPostAuthor,
    bool IsMine,
    string Body,
    DateTimeOffset PostedAt,
    IReadOnlyList<CommunityCommentDto> Replies);

public sealed record AddCommentRequest(string Body, int? ParentId = null);

/// <summary>One turn of the Sapling AI chat. Role is "user" or "assistant".</summary>
public sealed record AssistantMessageDto(string Role, string Text);

public sealed record AssistantRequest(IReadOnlyList<AssistantMessageDto> Messages);

public sealed record AssistantReplyDto(string Text);

/// <summary>A college's student space: students of that college only, never shown to the institute.</summary>
public sealed record StudentSpaceDto(string College, int Members, IReadOnlyList<StudentMessageDto> Messages);

public sealed record StudentMessageDto(
    int Id,
    string AuthorName,
    string? AuthorHeadline,
    bool IsMine,
    string Body,
    DateTimeOffset PostedAt,
    int AuthorId = 0,
    bool IsSaved = false,
    bool IsEdited = false);

/// <summary>What the student space shows about a member: name, photo and passing year, nothing else.</summary>
public sealed record StudentSpaceMemberDto(int Id, string Name, int GraduationYear, string? AvatarUrl, bool IsMe);

/// <summary>What a college has published about itself. HasAccount is false until the college joins Sapling.</summary>
public sealed record CollegeInfoDto(
    string Name,
    string ShortName,
    string City,
    string State,
    string About,
    string Website,
    string ContactEmail,
    string? LogoDataUrl,
    bool Verified,
    bool HasAccount,
    int Posts);

public sealed record PostStudentMessageRequest(string Body);
