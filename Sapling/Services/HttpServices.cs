using System.Net.Http.Json;
using Sapling.Shared.Contracts;

namespace Sapling.Services;

public static class SaplingApi
{
    public const string Authenticated = "sapling-api";
    public const string Anonymous = "sapling-api-anon";

    /// <summary>
    /// The PC running Sapling.Web, as a phone on the same Wi-Fi sees it (ipconfig, "Wireless LAN adapter Wi-Fi").
    /// Update it if the router hands the PC a different address.
    /// </summary>
    private const string DevMachineHost = "192.168.1.68";

    /// <summary>
    /// The deployed server. Release builds always use it; set it to the VM's DNS name
    /// (e.g. https://sapling.eastasia.cloudapp.azure.com/).
    /// </summary>
    private const string DeployedBaseAddress = "https://sapling.eastasia.cloudapp.azure.com/";

#if DEBUG
    // Debug builds talk to Sapling.Web on your PC. Set this to true to test a debug build against the deployed server.
    private const bool UseDeployedServerInDebug = false;

    // The emulator reaches the host through 10.0.2.2; a physical phone needs the PC's LAN address.
    public static string BaseAddress => UseDeployedServerInDebug
        ? DeployedBaseAddress
        : DeviceInfo.Platform != DevicePlatform.Android
            ? "http://localhost:5250/"
            : DeviceInfo.DeviceType == DeviceType.Virtual
                ? "http://10.0.2.2:5250/"
                : $"http://{DevMachineHost}:5250/";
#else
    public static string BaseAddress => DeployedBaseAddress;
#endif
}

internal static class HttpExtensions
{
    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url, CancellationToken ct)
        where T : new()
        => await client.GetFromJsonAsync<T>(url, ct) ?? new T();

    public static async Task<T?> GetJsonOrNullAsync<T>(this HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(ct) : default;
    }

    public static async Task<T> PostJsonAsync<T>(this HttpClient client, string url, object? body, CancellationToken ct)
    {
        var response = body is null
            ? await client.PostAsync(url, null, ct)
            : await client.PostAsJsonAsync(url, body, ct);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }
}

public sealed class HttpProfileService(IHttpClientFactory factory) : IProfileService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<StudentProfileDto> GetAsync(CancellationToken ct = default) =>
        (await Client.GetFromJsonAsync<StudentProfileDto>("api/profile", ct))!;

    public async Task DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default)
    {
        var response = await Client.PostAsJsonAsync("api/profile/delete-account", request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            throw new ArgumentException((await response.Content.ReadFromJsonAsync<string>(ct)) ?? "The account could not be deleted.");
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task<StudentProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        var response = await Client.PutAsJsonAsync("api/profile", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StudentProfileDto>(ct))!;
    }

    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<SkillDto>>("api/profile/skills", ct);

    public async Task<IReadOnlyList<SkillDto>> SetClaimedSkillsAsync(IReadOnlyList<string> skillNames, CancellationToken ct = default)
    {
        var response = await Client.PutAsJsonAsync("api/profile/skills", skillNames, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<SkillDto>>(ct))!;
    }

    public async Task<IReadOnlyList<string>> GetSkillSuggestionsAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<string>>("api/profile/skill-suggestions", ct);

    public async Task<IReadOnlyList<SkillSuggestionDto>> GetSkillCatalogueAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<SkillSuggestionDto>>("api/profile/skill-catalogue", ct);

    public async Task<SkillPickerDto> GetSkillPickerAsync(CancellationToken ct = default) =>
        await Client.GetFromJsonAsync<SkillPickerDto>("api/profile/skill-picker", ct) ?? new SkillPickerDto([], null);

    public async Task<IReadOnlyList<CollegeDto>> SearchCollegesAsync(string query, string? state = null, CancellationToken ct = default)
    {
        var url = $"api/profile/colleges?q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(state))
        {
            url += $"&state={Uri.EscapeDataString(state)}";
        }
        return await Client.GetJsonAsync<List<CollegeDto>>(url, ct);
    }

    public async Task CompleteOnboardingAsync(CancellationToken ct = default) =>
        (await Client.PostAsync("api/profile/complete-onboarding", null, ct)).EnsureSuccessStatusCode();

    public async Task<StudentProfileDto> SetAvatarAsync(string? dataUrl, CancellationToken ct = default)
    {
        var response = await Client.PutAsJsonAsync("api/profile/avatar", new UpdateAvatarRequest(dataUrl), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StudentProfileDto>(ct))!;
    }
}

public sealed class HttpHomeService(IHttpClientFactory factory) : IHomeService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<HomeSummaryDto> GetSummaryAsync(CancellationToken ct = default) =>
        (await Client.GetFromJsonAsync<HomeSummaryDto>("api/home", ct))!;
}

public sealed class HttpCareerService(IHttpClientFactory factory) : ICareerService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<CareerPathSummaryDto>> GetPathsAsync(bool all = false, CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<CareerPathSummaryDto>>(all ? "api/paths?all=true" : "api/paths", ct);

    public Task<CareerPathDto?> GetPathAsync(int id, CancellationToken ct = default) =>
        Client.GetJsonOrNullAsync<CareerPathDto>($"api/paths/{id}", ct);

    public async Task<CareerPathDto?> SetTargetAsync(int id, CancellationToken ct = default)
    {
        var response = await Client.PostAsync($"api/paths/{id}/target", null, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CareerPathDto>(ct) : null;
    }
}

public sealed class HttpSkillGapService(IHttpClientFactory factory) : ISkillGapService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<SkillGapDto>> GetGapsAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<SkillGapDto>>("api/skills/gaps", ct);

    public async Task<SkillCoverageDto> GetCoverageAsync(CancellationToken ct = default) =>
        (await Client.GetFromJsonAsync<SkillCoverageDto>("api/skills/coverage", ct))!;
}

public sealed class HttpRoadmapService(IHttpClientFactory factory) : IRoadmapService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<RoadmapDto> GetAsync(CancellationToken ct = default) =>
        (await Client.GetFromJsonAsync<RoadmapDto>("api/roadmap", ct))!;

    public Task<RoadmapDto> SetCheckpointAsync(int checkpointId, bool done, CancellationToken ct = default) =>
        Client.PostJsonAsync<RoadmapDto>($"api/roadmap/checkpoints/{checkpointId}/{done.ToString().ToLowerInvariant()}", null, ct);

    public Task<RoadmapDto> AddSkillAsync(int skillId, CancellationToken ct = default) =>
        Client.PostJsonAsync<RoadmapDto>($"api/roadmap/skills/{skillId}", null, ct);

    public Task<RoadmapDto> ChooseCourseAsync(int courseId, IReadOnlyList<int> skillIds, CancellationToken ct = default) =>
        Client.PostJsonAsync<RoadmapDto>($"api/roadmap/courses/{courseId}/choose", skillIds, ct);
}

public sealed class HttpOpportunityService(IHttpClientFactory factory) : IOpportunityService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<OpportunityDto>> GetAllAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<OpportunityDto>>("api/opportunities", ct);

    public Task<OpportunityDto?> GetAsync(int id, CancellationToken ct = default) =>
        Client.GetJsonOrNullAsync<OpportunityDto>($"api/opportunities/{id}", ct);

    public Task<OpportunityDto> ApplyAsync(int id, CancellationToken ct = default) =>
        Client.PostJsonAsync<OpportunityDto>($"api/opportunities/{id}/apply", null, ct);
}

public sealed class HttpResumeService(IHttpClientFactory factory) : IResumeService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<ResumeSummaryDto>> GetAllAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<ResumeSummaryDto>>("api/resumes", ct);

    public Task<ResumeDto?> GetAsync(int id, CancellationToken ct = default) =>
        Client.GetJsonOrNullAsync<ResumeDto>($"api/resumes/{id}", ct);

    public Task<ResumeDto> ImportPdfAsync(string fileName, byte[] pdfBytes, string template, CancellationToken ct = default)
    {
        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return SendAsync<ResumeDto>(HttpMethod.Post, $"api/resumes/import?template={Uri.EscapeDataString(template)}", form, ct);
    }

    public Task<ResumeDataDto> GetStarterDataAsync(CancellationToken ct = default) =>
        SendAsync<ResumeDataDto>(HttpMethod.Get, "api/resumes/starter", null, ct);

    public Task<ResumeDataDto> DraftFromDescriptionAsync(DescribeYourselfRequest request, CancellationToken ct = default) =>
        SendAsync<ResumeDataDto>(HttpMethod.Post, "api/resumes/draft", JsonContent.Create(request), ct);

    public Task<ResumeDto> CreateFromDataAsync(CreateResumeRequest request, CancellationToken ct = default) =>
        SendAsync<ResumeDto>(HttpMethod.Post, "api/resumes", JsonContent.Create(request), ct);

    public Task<ResumeDto> SaveAsync(int id, SaveResumeRequest request, CancellationToken ct = default) =>
        SendAsync<ResumeDto>(HttpMethod.Put, $"api/resumes/{id}", JsonContent.Create(request), ct);

    public Task<ResumeDto> AnalyseAsync(int id, SaveResumeRequest request, CancellationToken ct = default) =>
        SendAsync<ResumeDto>(HttpMethod.Post, $"api/resumes/{id}/analyse", JsonContent.Create(request), ct);

    public Task<ResumeEditResultDto> EditAsync(int id, ResumeInstructionRequest request, CancellationToken ct = default) =>
        SendAsync<ResumeEditResultDto>(HttpMethod.Post, $"api/resumes/{id}/edit", JsonContent.Create(request), ct);

    public Task<ResumeEditResultDto> ApplySuggestionAsync(int id, int suggestionId, SaveResumeRequest request, CancellationToken ct = default) =>
        SendAsync<ResumeEditResultDto>(HttpMethod.Post, $"api/resumes/{id}/suggestions/{suggestionId}/apply", JsonContent.Create(request), ct);

    public Task<ResumeCompileResultDto> CompileAsync(string latex, CancellationToken ct = default) =>
        SendAsync<ResumeCompileResultDto>(HttpMethod.Post, "api/resumes/compile", JsonContent.Create(new SaveResumeRequest(latex)), ct);

    public async Task DeleteAsync(int id, CancellationToken ct = default) =>
        (await Client.DeleteAsync($"api/resumes/{id}", ct)).EnsureSuccessStatusCode();

    /// <summary>Turns the server's problem details back into the same exception the web head throws.</summary>
    private async Task<T> SendAsync<T>(HttpMethod method, string url, HttpContent? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body };
        var response = await Client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<T>(ct))!;
        }

        if ((int)response.StatusCode is 400 or 503)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(ct);
            throw new ResumeProblemException(problem?.Detail ?? "Something went wrong.", (int)response.StatusCode == 503);
        }

        response.EnsureSuccessStatusCode();
        return default!;
    }

    private sealed record ProblemBody(string? Detail);
}

public sealed class HttpInterviewService(IHttpClientFactory factory) : IInterviewService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<InterviewSummaryDto>> GetHistoryAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<InterviewSummaryDto>>("api/interview", ct);

    public Task<InterviewDto?> GetAsync(int id, CancellationToken ct = default) =>
        Client.GetJsonOrNullAsync<InterviewDto>($"api/interview/{id}", ct);

    public Task<InterviewDto> CreateAsync(CreateInterviewRequest request, CancellationToken ct = default) =>
        SendAsync<InterviewDto>(HttpMethod.Post, "api/interview", JsonContent.Create(request), ct);

    public Task<InterviewDto> AttachResumeAsync(int id, string fileName, byte[] pdfBytes, CancellationToken ct = default)
    {
        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return SendAsync<InterviewDto>(HttpMethod.Post, $"api/interview/{id}/resume", form, ct);
    }

    public Task<InterviewDto> SkipResumeAsync(int id, CancellationToken ct = default) =>
        SendAsync<InterviewDto>(HttpMethod.Post, $"api/interview/{id}/resume/skip", null, ct);

    public Task<InterviewerLineDto> BeginAsync(int id, CancellationToken ct = default) =>
        SendAsync<InterviewerLineDto>(HttpMethod.Post, $"api/interview/{id}/begin", null, ct);

    public Task<InterviewerLineDto> AnswerAsync(int id, SubmitAnswerRequest request, CancellationToken ct = default) =>
        SendAsync<InterviewerLineDto>(HttpMethod.Post, $"api/interview/{id}/answer", JsonContent.Create(request), ct);

    public Task<InterviewDto> FinishAsync(int id, CancellationToken ct = default) =>
        SendAsync<InterviewDto>(HttpMethod.Post, $"api/interview/{id}/finish", null, ct);

    public async Task DeleteAsync(int id, CancellationToken ct = default) =>
        (await Client.DeleteAsync($"api/interview/{id}", ct)).EnsureSuccessStatusCode();

    /// <summary>Turns the server's problem details back into the same exception the web head throws.</summary>
    private async Task<T> SendAsync<T>(HttpMethod method, string url, HttpContent? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body };
        var response = await Client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<T>(ct))!;
        }

        if ((int)response.StatusCode is 400 or 503)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(ct);
            throw new InterviewProblemException(problem?.Detail ?? "Something went wrong.", (int)response.StatusCode == 503);
        }

        response.EnsureSuccessStatusCode();
        return default!;
    }

    private sealed record ProblemBody(string? Detail);
}

public sealed class HttpStudentSpaceService(IHttpClientFactory factory) : IStudentSpaceService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<StudentSpaceDto> GetAsync(CancellationToken ct = default) =>
        await Client.GetFromJsonAsync<StudentSpaceDto>("api/community/space", ct) ?? new StudentSpaceDto("", 0, []);

    public async Task<StudentSpaceDto> PostAsync(PostStudentMessageRequest request, CancellationToken ct = default)
    {
        var response = await Client.PostAsJsonAsync("api/community/space", request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // The server's reason (too long, too fast) is written for the student.
            throw new ArgumentException((await response.Content.ReadFromJsonAsync<string>(ct)) ?? "That message couldn't be sent.");
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StudentSpaceDto>(ct))!;
    }

    public async Task<StudentSpaceDto> DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await Client.DeleteAsync($"api/community/space/{id}", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StudentSpaceDto>(ct))!;
    }

    public async Task<StudentSpaceDto> EditAsync(int id, PostStudentMessageRequest request, CancellationToken ct = default)
    {
        var response = await Client.PutAsJsonAsync($"api/community/space/{id}", request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            throw new ArgumentException((await response.Content.ReadFromJsonAsync<string>(ct)) ?? "That message couldn't be changed.");
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StudentSpaceDto>(ct))!;
    }

    public Task<StudentSpaceDto> SetSavedAsync(int id, bool saved, CancellationToken ct = default) =>
        Client.PostJsonAsync<StudentSpaceDto>($"api/community/space/{id}/save/{saved.ToString().ToLowerInvariant()}", null, ct);

    public async Task<IReadOnlyList<StudentMessageDto>> GetSavedAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<StudentMessageDto>>("api/community/space/saved", ct);

    public async Task<IReadOnlyList<StudentSpaceMemberDto>> GetMembersAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<StudentSpaceMemberDto>>("api/community/space/members", ct);
}

public sealed class HttpAssistantService(IHttpClientFactory factory) : IAssistantService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public Task<AssistantReplyDto> AskAsync(IReadOnlyList<AssistantMessageDto> messages, CancellationToken ct = default) =>
        Client.PostJsonAsync<AssistantReplyDto>("api/assistant", new AssistantRequest(messages), ct);
}

public sealed class HttpGovtService(IHttpClientFactory factory) : IGovtService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<GovtExamDto>> GetExamsAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<GovtExamDto>>("api/govt", ct);

    public Task<GovtExamDto?> GetExamAsync(int id, CancellationToken ct = default) =>
        Client.GetJsonOrNullAsync<GovtExamDto>($"api/govt/{id}", ct);
}

public sealed class HttpCommunityService(IHttpClientFactory factory) : ICommunityService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<CommunityPostDto>> GetFeedAsync(string? kind = null, CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<CommunityPostDto>>(
            string.IsNullOrWhiteSpace(kind) ? "api/community" : $"api/community?kind={Uri.EscapeDataString(kind)}", ct);

    public Task<CommunityPostDto?> GetPostAsync(int id, CancellationToken ct = default) =>
        Client.GetJsonOrNullAsync<CommunityPostDto>($"api/community/{id}", ct);

    public async Task<CollegeInfoDto?> GetCollegeAsync(CancellationToken ct = default)
    {
        var response = await Client.GetAsync("api/community/college", ct);
        return response.StatusCode == System.Net.HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<CollegeInfoDto>(ct)
            : null;
    }

    public Task<CommunityPostDto> SetUpvoteAsync(int id, bool upvoted, CancellationToken ct = default) =>
        Client.PostJsonAsync<CommunityPostDto>($"api/community/{id}/upvote/{upvoted.ToString().ToLowerInvariant()}", null, ct);

    public Task<CommunityPostDto> SetSavedAsync(int id, bool saved, CancellationToken ct = default) =>
        Client.PostJsonAsync<CommunityPostDto>($"api/community/{id}/save/{saved.ToString().ToLowerInvariant()}", null, ct);

    public async Task<IReadOnlyList<CommunityPostDto>> GetSavedAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<CommunityPostDto>>("api/community/saved", ct);

    public async Task<IReadOnlyList<CommunityCommentDto>> GetCommentsAsync(int postId, CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<CommunityCommentDto>>($"api/community/{postId}/comments", ct);

    public async Task<IReadOnlyList<CommunityCommentDto>> AddCommentAsync(int postId, AddCommentRequest request, CancellationToken ct = default)
    {
        var response = await Client.PostAsJsonAsync($"api/community/{postId}/comments", request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Surface the server's validation message the same way the in-process service does.
            throw new ArgumentException(await response.Content.ReadFromJsonAsync<string>(ct) ?? "Comment could not be posted.");
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<CommunityCommentDto>>(ct))!;
    }

    public async Task<IReadOnlyList<CommunityCommentDto>> DeleteCommentAsync(int postId, int commentId, CancellationToken ct = default)
    {
        var response = await Client.DeleteAsync($"api/community/{postId}/comments/{commentId}", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<CommunityCommentDto>>(ct))!;
    }
}

public sealed class HttpQuizService(IHttpClientFactory factory) : IQuizService
{
    private HttpClient Client => factory.CreateClient(SaplingApi.Authenticated);

    public async Task<IReadOnlyList<QuizQuestionDto>> GetQuestionsAsync(CancellationToken ct = default) =>
        await Client.GetJsonAsync<List<QuizQuestionDto>>("api/quiz", ct);

    public Task<QuizResultDto> SubmitAsync(QuizSubmissionRequest request, CancellationToken ct = default) =>
        Client.PostJsonAsync<QuizResultDto>("api/quiz", request, ct);
}
