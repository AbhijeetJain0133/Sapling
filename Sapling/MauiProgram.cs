using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Sapling.Services;
using Sapling.Shared.Contracts;
using Sapling.Shared.Services;
using Sapling.Shared.Theming;

namespace Sapling;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddAuthorizationCore();

        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddTransient<BearerTokenHandler>();
        builder.Services.AddSingleton<ApiAuthService>();
        builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthService>());

        builder.Services.AddHttpClient(SaplingApi.Anonymous, client =>
            client.BaseAddress = new Uri(SaplingApi.BaseAddress));

        builder.Services.AddHttpClient(SaplingApi.Authenticated, client =>
                client.BaseAddress = new Uri(SaplingApi.BaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>();

        builder.Services.AddScoped<IProfileService, HttpProfileService>();
        builder.Services.AddScoped<IHomeService, HttpHomeService>();
        builder.Services.AddScoped<ICareerService, HttpCareerService>();
        builder.Services.AddScoped<ISkillGapService, HttpSkillGapService>();
        builder.Services.AddScoped<IRoadmapService, HttpRoadmapService>();
        builder.Services.AddScoped<IOpportunityService, HttpOpportunityService>();
        builder.Services.AddScoped<IResumeService, HttpResumeService>();
        builder.Services.AddScoped<IInterviewService, HttpInterviewService>();
        builder.Services.AddScoped<IAssistantService, HttpAssistantService>();
        builder.Services.AddScoped<IStudentSpaceService, HttpStudentSpaceService>();
        builder.Services.AddScoped<AssistantChat>();
        builder.Services.AddScoped<IGovtService, HttpGovtService>();
        builder.Services.AddScoped<IQuizService, HttpQuizService>();
        builder.Services.AddScoped<ICommunityService, HttpCommunityService>();

        builder.Services.AddSingleton<IFormFactor, MobileFormFactor>();
        builder.Services.AddSingleton(new RouteAssemblies(typeof(AppRoot).Assembly));
        builder.Services.AddSingleton<IRecentFeaturesService, MauiRecentFeaturesService>();
        builder.Services.AddScoped<IThemeService, MauiThemeService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
