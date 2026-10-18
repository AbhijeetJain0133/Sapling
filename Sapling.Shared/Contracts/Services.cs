namespace Sapling.Shared.Contracts;

public interface IProfileService
{
    Task<StudentProfileDto> GetAsync(CancellationToken ct = default);

    Task<StudentProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SkillDto>> GetSkillsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SkillDto>> SetClaimedSkillsAsync(IReadOnlyList<string> skillNames, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetSkillSuggestionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SkillSuggestionDto>> GetSkillCatalogueAsync(CancellationToken ct = default);

    Task<SkillPickerDto> GetSkillPickerAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CollegeDto>> SearchCollegesAsync(string query, string? state = null, CancellationToken ct = default);

    Task CompleteOnboardingAsync(CancellationToken ct = default);

    Task<StudentProfileDto> SetAvatarAsync(string? dataUrl, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes the signed-in student's account and everything stored in it. The request must
    /// carry the confirmation word, so a stray call cannot delete an account.
    /// </summary>
    Task DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default);
}

/// <summary>The home dashboard: greeting, target role, roadmap progress and what is new.</summary>
public interface IHomeService
{
    Task<HomeSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}

public interface ICareerService
{
    /// <summary>Roles that the student's course leads to, or every role when <paramref name="all"/> is true.</summary>
    Task<IReadOnlyList<CareerPathSummaryDto>> GetPathsAsync(bool all = false, CancellationToken ct = default);

    Task<CareerPathDto?> GetPathAsync(int id, CancellationToken ct = default);

    Task<CareerPathDto?> SetTargetAsync(int id, CancellationToken ct = default);
}

public interface ISkillGapService
{
    Task<IReadOnlyList<SkillGapDto>> GetGapsAsync(CancellationToken ct = default);

    Task<SkillCoverageDto> GetCoverageAsync(CancellationToken ct = default);
}

public interface IRoadmapService
{
    Task<RoadmapDto> GetAsync(CancellationToken ct = default);

    /// <summary>Ticks or unticks a checkpoint. Finishing a direct course adds its skills to the profile.</summary>
    Task<RoadmapDto> SetCheckpointAsync(int checkpointId, bool done, CancellationToken ct = default);

    /// <summary>Follow this course for these skills instead of another provider's; only the followed course counts.</summary>
    Task<RoadmapDto> ChooseCourseAsync(int courseId, IReadOnlyList<int> skillIds, CancellationToken ct = default);

    /// <summary>The student says they can use a skill now, e.g. after a foundation course or learning it elsewhere.</summary>
    Task<RoadmapDto> AddSkillAsync(int skillId, CancellationToken ct = default);
}

public interface IOpportunityService
{
    Task<IReadOnlyList<OpportunityDto>> GetAllAsync(CancellationToken ct = default);

    Task<OpportunityDto?> GetAsync(int id, CancellationToken ct = default);

    Task<OpportunityDto> ApplyAsync(int id, CancellationToken ct = default);
}

public interface IResumeService
{
    Task<IReadOnlyList<ResumeSummaryDto>> GetAllAsync(CancellationToken ct = default);

    Task<ResumeDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>PDF → text → the AI copies it into structured data and judges it → rendered in the template and saved.</summary>
    Task<ResumeDto> ImportPdfAsync(string fileName, byte[] pdfBytes, string template, CancellationToken ct = default);

    /// <summary>Wizard prefill from the student's profile: name, email, college, branch, year, CGPA and skills.</summary>
    Task<ResumeDataDto> GetStarterDataAsync(CancellationToken ct = default);

    /// <summary>"Describe yourself" → structured data for the wizard to review. Nothing is saved.</summary>
    Task<ResumeDataDto> DraftFromDescriptionAsync(DescribeYourselfRequest request, CancellationToken ct = default);

    /// <summary>Renders structured data in a template (no AI) and saves it.</summary>
    Task<ResumeDto> CreateFromDataAsync(CreateResumeRequest request, CancellationToken ct = default);

    Task<ResumeDto> SaveAsync(int id, SaveResumeRequest request, CancellationToken ct = default);

    /// <summary>Saves the LaTeX, then has the AI score it again.</summary>
    Task<ResumeDto> AnalyseAsync(int id, SaveResumeRequest request, CancellationToken ct = default);

    /// <summary>Saves the LaTeX, then asks the AI to carry out a free-text instruction on it.</summary>
    Task<ResumeEditResultDto> EditAsync(int id, ResumeInstructionRequest request, CancellationToken ct = default);

    /// <summary>Saves the LaTeX, applies one suggestion with the AI, and marks it applied.</summary>
    Task<ResumeEditResultDto> ApplySuggestionAsync(int id, int suggestionId, SaveResumeRequest request, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Builds the PDF for the editor's preview and download. Nothing is saved.</summary>
    Task<ResumeCompileResultDto> CompileAsync(string latex, CancellationToken ct = default);
}

/// <summary>A resume problem the student should see as written: an unreadable PDF, the AI out of credit, a bad edit.</summary>
public sealed class ResumeProblemException(string message, bool retryable = false) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}

public interface IInterviewService
{
    Task<IReadOnlyList<InterviewSummaryDto>> GetHistoryAsync(CancellationToken ct = default);

    Task<InterviewDto?> GetAsync(int id, CancellationToken ct = default);

    Task<InterviewDto> CreateAsync(CreateInterviewRequest request, CancellationToken ct = default);

    Task<InterviewDto> AttachResumeAsync(int id, string fileName, byte[] pdfBytes, CancellationToken ct = default);

    /// <summary>Continues without a resume, removing one already uploaded.</summary>
    Task<InterviewDto> SkipResumeAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Starts the interview, or resumes one that is already live: returns the pending interviewer line,
    /// or generates the next one when the last saved turn is an answer.
    /// </summary>
    Task<InterviewerLineDto> BeginAsync(int id, CancellationToken ct = default);

    Task<InterviewerLineDto> AnswerAsync(int id, SubmitAnswerRequest request, CancellationToken ct = default);

    Task<InterviewDto> FinishAsync(int id, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface IStudentSpaceService
{
    Task<StudentSpaceDto> GetAsync(CancellationToken ct = default);

    Task<StudentSpaceDto> PostAsync(PostStudentMessageRequest request, CancellationToken ct = default);

    /// <summary>Changes the text of one of the student's own messages.</summary>
    Task<StudentSpaceDto> EditAsync(int id, PostStudentMessageRequest request, CancellationToken ct = default);

    /// <summary>Removes one of the student's own messages.</summary>
    Task<StudentSpaceDto> DeleteAsync(int id, CancellationToken ct = default);

    Task<StudentSpaceDto> SetSavedAsync(int id, bool saved, CancellationToken ct = default);

    /// <summary>The student's bookmarked messages, most recently saved first.</summary>
    Task<IReadOnlyList<StudentMessageDto>> GetSavedAsync(CancellationToken ct = default);

    /// <summary>Everyone from the student's college: name, photo and passing year only.</summary>
    Task<IReadOnlyList<StudentSpaceMemberDto>> GetMembersAsync(CancellationToken ct = default);
}

public interface IAssistantService
{
    /// <summary>Answers the last message, given the conversation so far.</summary>
    Task<AssistantReplyDto> AskAsync(IReadOnlyList<AssistantMessageDto> messages, CancellationToken ct = default);
}

public interface IGovtService
{
    Task<IReadOnlyList<GovtExamDto>> GetExamsAsync(CancellationToken ct = default);

    Task<GovtExamDto?> GetExamAsync(int id, CancellationToken ct = default);
}

public interface IQuizService
{
    Task<IReadOnlyList<QuizQuestionDto>> GetQuestionsAsync(CancellationToken ct = default);

    Task<QuizResultDto> SubmitAsync(QuizSubmissionRequest request, CancellationToken ct = default);
}

/// <summary>Institute-authored posts. Students can read, upvote and comment; only institutions publish.</summary>
public interface ICommunityService
{
    Task<IReadOnlyList<CommunityPostDto>> GetFeedAsync(string? kind = null, CancellationToken ct = default);

    Task<CommunityPostDto?> GetPostAsync(int id, CancellationToken ct = default);

    Task<CommunityPostDto> SetUpvoteAsync(int id, bool upvoted, CancellationToken ct = default);

    Task<CommunityPostDto> SetSavedAsync(int id, bool saved, CancellationToken ct = default);

    /// <summary>The student's bookmarked posts, most recently saved first.</summary>
    Task<IReadOnlyList<CommunityPostDto>> GetSavedAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CommunityCommentDto>> GetCommentsAsync(int postId, CancellationToken ct = default);

    Task<IReadOnlyList<CommunityCommentDto>> AddCommentAsync(int postId, AddCommentRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CommunityCommentDto>> DeleteCommentAsync(int postId, int commentId, CancellationToken ct = default);

    /// <summary>The student's own college as it describes itself; null when the profile names no college.</summary>
    Task<CollegeInfoDto?> GetCollegeAsync(CancellationToken ct = default);
}

/// <summary>A problem the student should see as written, such as an unreadable upload or the AI being out of credit.</summary>
public sealed class InterviewProblemException(string message, bool retryable = false) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}
