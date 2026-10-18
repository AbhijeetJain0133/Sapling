using Sapling.Shared.Contracts;
using Sapling.Web.Ai;
using Sapling.Web.Services;

namespace Sapling.Web.Api;

public static class ApiEndpoints
{
    /// <summary>The web head calls these services in-process; the MAUI head reaches them over HTTP.</summary>
    public static void MapSaplingApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        var profile = api.MapGroup("/profile");
        profile.MapGet("/", (IProfileService s, CancellationToken ct) => s.GetAsync(ct));
        profile.MapPost("/delete-account", async (DeleteAccountRequest r, IProfileService s, CancellationToken ct) =>
        {
            try
            {
                await s.DeleteAccountAsync(r, ct);
                return Results.NoContent();
            }
            catch (ArgumentException e)
            {
                return Results.BadRequest(e.Message);
            }
        });
        profile.MapPut("/", (UpdateProfileRequest r, IProfileService s, CancellationToken ct) => s.UpdateAsync(r, ct));
        profile.MapGet("/skills", (IProfileService s, CancellationToken ct) => s.GetSkillsAsync(ct));
        profile.MapPut("/skills", (List<string> names, IProfileService s, CancellationToken ct) => s.SetClaimedSkillsAsync(names, ct));
        profile.MapGet("/skill-suggestions", (IProfileService s, CancellationToken ct) => s.GetSkillSuggestionsAsync(ct));
        profile.MapGet("/skill-catalogue", (IProfileService s, CancellationToken ct) => s.GetSkillCatalogueAsync(ct));
        profile.MapGet("/skill-picker", (IProfileService s, CancellationToken ct) => s.GetSkillPickerAsync(ct));
        profile.MapGet("/colleges", (string? q, string? state, ICollegeCatalogService catalog) =>
            catalog.Search(q ?? "", state, 20)).AllowAnonymous();
        profile.MapPost("/complete-onboarding", async (IProfileService s, CancellationToken ct) =>
        {
            await s.CompleteOnboardingAsync(ct);
            return Results.NoContent();
        });
        profile.MapPut("/avatar", (UpdateAvatarRequest r, IProfileService s, CancellationToken ct) =>
            s.SetAvatarAsync(r.AvatarDataUrl, ct));

        api.MapGet("/home", (IHomeService s, CancellationToken ct) => s.GetSummaryAsync(ct));

        var paths = api.MapGroup("/paths");
        paths.MapGet("/", (bool? all, ICareerService s, CancellationToken ct) => s.GetPathsAsync(all == true, ct));
        paths.MapGet("/{id:int}", async (int id, ICareerService s, CancellationToken ct) =>
            await s.GetPathAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
        paths.MapPost("/{id:int}/target", async (int id, ICareerService s, CancellationToken ct) =>
            await s.SetTargetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());

        var skills = api.MapGroup("/skills");
        skills.MapGet("/gaps", (ISkillGapService s, CancellationToken ct) => s.GetGapsAsync(ct));
        skills.MapGet("/coverage", (ISkillGapService s, CancellationToken ct) => s.GetCoverageAsync(ct));

        var roadmap = api.MapGroup("/roadmap");
        roadmap.MapGet("/", (IRoadmapService s, CancellationToken ct) => s.GetAsync(ct));
        roadmap.MapPost("/checkpoints/{id:int}/{done:bool}", (int id, bool done, IRoadmapService s, CancellationToken ct) =>
            s.SetCheckpointAsync(id, done, ct));
        roadmap.MapPost("/skills/{id:int}", (int id, IRoadmapService s, CancellationToken ct) => s.AddSkillAsync(id, ct));
        roadmap.MapPost("/courses/{id:int}/choose", (int id, int[] skillIds, IRoadmapService s, CancellationToken ct) =>
            s.ChooseCourseAsync(id, skillIds, ct));

        var opportunities = api.MapGroup("/opportunities");
        opportunities.MapGet("/", (IOpportunityService s, CancellationToken ct) => s.GetAllAsync(ct));
        opportunities.MapGet("/{id:int}", async (int id, IOpportunityService s, CancellationToken ct) =>
            await s.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
        opportunities.MapPost("/{id:int}/apply", (int id, IOpportunityService s, CancellationToken ct) => s.ApplyAsync(id, ct));

        var resumes = api.MapGroup("/resumes");
        resumes.MapGet("/", (IResumeService s, CancellationToken ct) => s.GetAllAsync(ct));
        resumes.MapGet("/starter", (IResumeService s, CancellationToken ct) => s.GetStarterDataAsync(ct));
        resumes.MapGet("/{id:int}", async (int id, IResumeService s, CancellationToken ct) =>
            await s.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
        resumes.MapPost("/import", async (IFormFile file, string? template, IResumeService s, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await file.OpenReadStream().CopyToAsync(buffer, ct);
            return await Guard(() => s.ImportPdfAsync(file.FileName, buffer.ToArray(), template ?? ResumeTemplates.SingleColumn, ct));
        }).DisableAntiforgery();
        resumes.MapPost("/draft", (DescribeYourselfRequest r, IResumeService s, CancellationToken ct) =>
            Guard(() => s.DraftFromDescriptionAsync(r, ct)));
        resumes.MapPost("/", (CreateResumeRequest r, IResumeService s, CancellationToken ct) =>
            Guard(() => s.CreateFromDataAsync(r, ct)));
        resumes.MapPut("/{id:int}", (int id, SaveResumeRequest r, IResumeService s, CancellationToken ct) =>
            Guard(() => s.SaveAsync(id, r, ct)));
        resumes.MapPost("/{id:int}/analyse", (int id, SaveResumeRequest r, IResumeService s, CancellationToken ct) =>
            Guard(() => s.AnalyseAsync(id, r, ct)));
        resumes.MapPost("/{id:int}/edit", (int id, ResumeInstructionRequest r, IResumeService s, CancellationToken ct) =>
            Guard(() => s.EditAsync(id, r, ct)));
        resumes.MapPost("/{id:int}/suggestions/{suggestionId:int}/apply", (int id, int suggestionId, SaveResumeRequest r, IResumeService s, CancellationToken ct) =>
            Guard(() => s.ApplySuggestionAsync(id, suggestionId, r, ct)));
        resumes.MapPost("/compile", (SaveResumeRequest r, IResumeService s, CancellationToken ct) =>
            Guard(() => s.CompileAsync(r.Latex, ct)));
        resumes.MapDelete("/{id:int}", async (int id, IResumeService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        var interview = api.MapGroup("/interview");
        interview.MapGet("/", (IInterviewService s, CancellationToken ct) => s.GetHistoryAsync(ct));
        interview.MapPost("/", (CreateInterviewRequest r, IInterviewService s, CancellationToken ct) =>
            Guard(() => s.CreateAsync(r, ct)));
        interview.MapGet("/{id:int}", async (int id, IInterviewService s, CancellationToken ct) =>
            await s.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
        interview.MapPost("/{id:int}/resume", async (int id, IFormFile file, IInterviewService s, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await file.OpenReadStream().CopyToAsync(buffer, ct);
            return await Guard(() => s.AttachResumeAsync(id, file.FileName, buffer.ToArray(), ct));
        }).DisableAntiforgery();
        interview.MapPost("/{id:int}/resume/skip", (int id, IInterviewService s, CancellationToken ct) =>
            Guard(() => s.SkipResumeAsync(id, ct)));
        interview.MapPost("/{id:int}/begin", (int id, IInterviewService s, CancellationToken ct) =>
            Guard(() => s.BeginAsync(id, ct)));
        interview.MapPost("/{id:int}/answer", (int id, SubmitAnswerRequest r, IInterviewService s, CancellationToken ct) =>
            Guard(() => s.AnswerAsync(id, r, ct)));
        interview.MapPost("/{id:int}/finish", (int id, IInterviewService s, CancellationToken ct) =>
            Guard(() => s.FinishAsync(id, ct)));
        // Natural-voice audio for one of this interview's questions (order 0 is a short preview line).
        // Only the interview's own lines can be spoken, so this can't be used as a general text-to-speech proxy.
        interview.MapGet("/{id:int}/voice/{order:int}", async (int id, int order, IInterviewService s, IInterviewVoice voice, CancellationToken ct) =>
        {
            if (!voice.Available || await s.GetAsync(id, ct) is not { } dto)
            {
                return Results.NotFound();
            }

            var text = order == 0
                ? "Hi, I'm Sapling AI. This is how I'll sound in your interview."
                : dto.Turns.FirstOrDefault(t => t.Order == order && t.Speaker == "Interviewer")?.Text;
            if (text is null)
            {
                return Results.NotFound();
            }

            var audio = await voice.SpeakAsync(text, ct);
            return audio is null ? Results.StatusCode(502) : Results.File(audio, "audio/mpeg");
        });
        interview.MapDelete("/{id:int}", async (int id, IInterviewService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        api.MapPost("/assistant", (AssistantRequest request, IAssistantService s, CancellationToken ct) =>
            s.AskAsync(request.Messages ?? [], ct));

        var govt = api.MapGroup("/govt");
        govt.MapGet("/", (IGovtService s, CancellationToken ct) => s.GetExamsAsync(ct));
        govt.MapGet("/{id:int}", async (int id, IGovtService s, CancellationToken ct) =>
            await s.GetExamAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());

        var quiz = api.MapGroup("/quiz");
        quiz.MapGet("/", (IQuizService s, CancellationToken ct) => s.GetQuestionsAsync(ct));
        quiz.MapPost("/", (QuizSubmissionRequest r, IQuizService s, CancellationToken ct) => s.SubmitAsync(r, ct));

        var community = api.MapGroup("/community");
        community.MapGet("/space", (IStudentSpaceService s, CancellationToken ct) => s.GetAsync(ct));
        community.MapPost("/space", async (PostStudentMessageRequest r, IStudentSpaceService s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await s.PostAsync(r, ct));
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(e.Message);
            }
        });
        community.MapDelete("/space/{id:int}", async (int id, IStudentSpaceService s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await s.DeleteAsync(id, ct));
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        });
        community.MapPut("/space/{id:int}", async (int id, PostStudentMessageRequest r, IStudentSpaceService s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await s.EditAsync(id, r, ct));
            }
            catch (ArgumentException e)
            {
                return Results.BadRequest(e.Message);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        });
        community.MapPost("/space/{id:int}/save/{saved:bool}", async (int id, bool saved, IStudentSpaceService s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await s.SetSavedAsync(id, saved, ct));
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        });
        community.MapGet("/space/saved", (IStudentSpaceService s, CancellationToken ct) => s.GetSavedAsync(ct));
        community.MapGet("/space/members", (IStudentSpaceService s, CancellationToken ct) => s.GetMembersAsync(ct));
        community.MapGet("/college", async (ICommunityService s, CancellationToken ct) =>
            await s.GetCollegeAsync(ct) is { } dto ? Results.Ok(dto) : Results.NoContent());
        community.MapGet("/", (string? kind, ICommunityService s, CancellationToken ct) => s.GetFeedAsync(kind, ct));
        community.MapGet("/{id:int}", async (int id, ICommunityService s, CancellationToken ct) =>
            await s.GetPostAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
        community.MapPost("/{id:int}/upvote/{upvoted:bool}", async (int id, bool upvoted, ICommunityService s, CancellationToken ct) =>
            await s.GetPostAsync(id, ct) is null ? Results.NotFound() : Results.Ok(await s.SetUpvoteAsync(id, upvoted, ct)));
        community.MapGet("/{id:int}/comments", (int id, ICommunityService s, CancellationToken ct) => s.GetCommentsAsync(id, ct));
        community.MapPost("/{id:int}/comments", async (int id, AddCommentRequest r, ICommunityService s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await s.AddCommentAsync(id, r, ct));
            }
            catch (ArgumentException e)
            {
                return Results.BadRequest(e.Message);
            }
        });
        community.MapDelete("/{id:int}/comments/{commentId:int}", (int id, int commentId, ICommunityService s, CancellationToken ct) =>
            s.DeleteCommentAsync(id, commentId, ct));
        community.MapGet("/saved", (ICommunityService s, CancellationToken ct) => s.GetSavedAsync(ct));
        community.MapPost("/{id:int}/save/{saved:bool}", async (int id, bool saved, ICommunityService s, CancellationToken ct) =>
            await s.GetPostAsync(id, ct) is null ? Results.NotFound() : Results.Ok(await s.SetSavedAsync(id, saved, ct)));

        MapInstituteApi(app);
        MapAdminApi(app);
    }

    /// <summary>Platform administration, web-only like the institute head.</summary>
    private static void MapAdminApi(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("AdminOnly");

        admin.MapGet("/dashboard", (AdminService s, CancellationToken ct) => s.GetDashboardAsync(ct));
        admin.MapGet("/accounts/{accountType}", (string accountType, string? q, AdminService s, CancellationToken ct) =>
            Problems(() => s.GetAccountsAsync(accountType, q, ct)));
        admin.MapPost("/accounts", (CreateAccountRequest r, AdminService s, CancellationToken ct) =>
            Problems(() => s.CreateAsync(r, ct)));
        admin.MapPost("/accounts/{id}/deactivated/{deactivated:bool}", async (string id, bool deactivated, AdminService s, CancellationToken ct) =>
            await Problems<object?>(async () =>
            {
                await s.SetDeactivatedAsync(id, deactivated, ct);
                return null;
            }));
        admin.MapDelete("/accounts/{id}", async (string id, AdminService s, CancellationToken ct) =>
            await Problems<object?>(async () =>
            {
                await s.DeleteAsync(id, ct);
                return null;
            }));
    }

    /// <summary>The institute head is web-only, so these are called in-process by its pages.</summary>
    private static void MapInstituteApi(IEndpointRouteBuilder app)
    {
        var institute = app.MapGroup("/api/institute").RequireAuthorization("InstituteOnly");

        institute.MapGet("/", async (InstituteService s, CancellationToken ct) =>
            await s.FindProfileAsync(ct) is { } dto ? Results.Ok(dto) : Results.NoContent());
        institute.MapPost("/setup", (InstituteSetupRequest r, InstituteService s, CancellationToken ct) =>
            Problems(() => s.CompleteSetupAsync(r, ct)));
        institute.MapPut("/", (UpdateInstituteRequest r, InstituteService s, CancellationToken ct) =>
            Problems(() => s.UpdateProfileAsync(r, ct)));
        institute.MapPut("/logo", (UpdateAvatarRequest r, InstituteService s, CancellationToken ct) =>
            Problems(() => s.SetLogoAsync(r.AvatarDataUrl, ct)));

        institute.MapGet("/dashboard", (InstituteService s, CancellationToken ct) => s.GetDashboardAsync(ct));
        institute.MapGet("/students", (string? q, string? branch, int? year, InstituteService s, CancellationToken ct) =>
            s.GetStudentsAsync(q, branch, year, ct));
        institute.MapGet("/students.csv", async (InstituteService s, CancellationToken ct) =>
            Results.File(await s.ExportStudentsCsvAsync(ct), "text/csv", "students.csv"));

        institute.MapGet("/posts", (bool? archived, InstituteService s, CancellationToken ct) =>
            s.GetPostsAsync(archived == true, ct));
        institute.MapPost("/posts", (SavePostRequest r, InstituteService s, CancellationToken ct) =>
            Problems(() => s.CreatePostAsync(r, ct)));
        institute.MapPut("/posts/{id:int}", (int id, SavePostRequest r, InstituteService s, CancellationToken ct) =>
            Problems(() => s.UpdatePostAsync(id, r, ct)));
        institute.MapDelete("/posts/{id:int}", async (int id, InstituteService s, CancellationToken ct) =>
        {
            await s.DeletePostAsync(id, ct);
            return Results.NoContent();
        });
        institute.MapPost("/posts/{id:int}/pin/{pinned:bool}", (int id, bool pinned, InstituteService s, CancellationToken ct) =>
            Problems(() => s.SetPinnedAsync(id, pinned, ct)));
        institute.MapPost("/posts/{id:int}/archive/{archived:bool}", (int id, bool archived, InstituteService s, CancellationToken ct) =>
            Problems(() => s.SetArchivedAsync(id, archived, ct)));

        institute.MapGet("/posts/{id:int}/comments", (int id, InstituteService s, CancellationToken ct) => s.GetCommentsAsync(id, ct));
        institute.MapPost("/posts/{id:int}/comments", (int id, AddCommentRequest r, InstituteService s, CancellationToken ct) =>
            Problems(() => s.ReplyAsync(id, r, ct)));
        institute.MapDelete("/posts/{id:int}/comments/{commentId:int}", async (int id, int commentId, InstituteService s, CancellationToken ct) =>
        {
            await s.DeleteCommentAsync(id, commentId, ct);
            return Results.NoContent();
        });
    }

    /// <summary>Validation and rule failures carry a message meant for the institute staffer.</summary>
    private static async Task<IResult> Problems<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(e.Message);
        }
    }

    /// <summary>Interview and resume problems carry a message meant for the student; retryable ones (AI quota) map to 503.</summary>
    private static async Task<IResult> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (InterviewProblemException e)
        {
            return Results.Problem(detail: e.Message, statusCode: e.Retryable ? 503 : 400);
        }
        catch (ResumeProblemException e)
        {
            return Results.Problem(detail: e.Message, statusCode: e.Retryable ? 503 : 400);
        }
    }
}
