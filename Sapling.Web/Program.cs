using System.ClientModel;
using Azure.Communication.Email;
using OpenAI;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Sapling.Shared.Contracts;
using Sapling.Shared.Services;
using Sapling.Shared.Theming;
using Sapling.Web.Ai;
using Sapling.Web.Api;
using Sapling.Web.Components;
using Sapling.Web.Data;
using Sapling.Web.Services;

var builder = WebApplication.CreateBuilder(args);

const string BrowserUserPolicy = "BrowserUser";
const string InstitutePolicy = "InstituteOnly";
const string AdminPolicy = "AdminOnly";

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    });

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<SaplingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Sapling") ?? "Data Source=sapling.db"));

builder.Services.AddIdentityApiEndpoints<AppUser>(options =>
    {
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<SaplingDbContext>()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

// The Android app signs in with bearer tokens. It renews the short-lived access token by itself; the refresh
// token decides how long a student stays signed in without opening the app.
builder.Services.Configure<Microsoft.AspNetCore.Authentication.BearerToken.BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromHours(1);
    options.RefreshTokenExpiration = TimeSpan.FromDays(60);
});

// Pages challenge through the cookie handler so a signed-out visitor is redirected, not 401'd.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(BrowserUserPolicy, policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser())
    .AddPolicy(InstitutePolicy, policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim(AppUserClaimsPrincipalFactory.AccountTypeClaim, AccountTypes.Institution))
    .AddPolicy(AdminPolicy, policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim(AppUserClaimsPrincipalFactory.AccountTypeClaim, AccountTypes.Admin));

// Registered only when configured, so a checkout without credentials still starts; the button then says so.
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        ExternalAuthEndpoints.ConfigureGoogle(options);
    });
}

builder.Services.AddMemoryCache();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/signin";
    options.LogoutPath = "/signout";
    options.AccessDeniedPath = "/signin";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

// Hugging Face Inference Providers exposes an OpenAI-compatible router, so the OpenAI client is pointed at it.
var hfKey = builder.Configuration["Ai:HuggingFace:ApiKey"];
var hfModel = builder.Configuration["Ai:HuggingFace:Model"] ?? "openai/gpt-oss-120b";
if (!string.IsNullOrWhiteSpace(hfKey))
{
    var endpoint = new Uri(builder.Configuration["Ai:HuggingFace:Endpoint"] ?? "https://router.huggingface.co/v1");
    var openAi = new OpenAIClient(new ApiKeyCredential(hfKey), new OpenAIClientOptions { Endpoint = endpoint });
    builder.Services.AddChatClient(openAi.GetChatClient(hfModel).AsIChatClient());
}
else
{
    builder.Services.AddSingleton<IChatClient, ScriptedChatClient>();
}

builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();

// Resume PDFs are compiled with Tectonic. Without it the editor still works; only the preview and PDF download are off.
builder.Services.AddSingleton<ILatexCompiler, TectonicCompiler>();

// Optional usage reports to a Discord channel; does nothing unless Monitoring:DiscordWebhookUrl is set.
builder.Services.AddHttpClient(UsageReporter.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, CircuitCounter>();
builder.Services.AddHostedService<UsageReporter>();

// Optional natural voice for the interviewer; without it the browser's own voices are used.
var speechKey = builder.Configuration["Speech:Azure:Key"];
var speechRegion = builder.Configuration["Speech:Azure:Region"];
if (!string.IsNullOrWhiteSpace(speechKey) && !string.IsNullOrWhiteSpace(speechRegion))
{
    builder.Services.AddHttpClient("azure-speech", client => client.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddSingleton<IInterviewVoice>(sp => new AzureSpeechVoice(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
        sp.GetRequiredService<ILogger<AzureSpeechVoice>>(),
        speechKey,
        speechRegion,
        builder.Configuration["Speech:Azure:Voice"] ?? AzureSpeechVoice.DefaultVoice));
}
else
{
    builder.Services.AddSingleton<IInterviewVoice, BrowserOnlyVoice>();
}

builder.Services.AddSingleton(CareerCatalogueFile.Load());
builder.Services.AddScoped<StudentContext>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<ICareerService, CareerService>();
builder.Services.AddScoped<ISkillGapService, SkillGapService>();
builder.Services.AddScoped<IRoadmapService, RoadmapService>();
builder.Services.AddScoped<IOpportunityService, OpportunityService>();
builder.Services.AddScoped<IResumeService, ResumeService>();
builder.Services.AddScoped<IInterviewService, InterviewService>();
builder.Services.AddScoped<IAssistantService, AssistantService>();
builder.Services.AddScoped<IStudentSpaceService, StudentSpaceService>();
builder.Services.AddScoped<IGovtService, GovtService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<ICommunityService, CommunityService>();
builder.Services.AddScoped<InstituteContext>();
builder.Services.AddScoped<InstituteService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<AccountFlowService>();
builder.Services.AddHttpClient<ITurnstileService, TurnstileService>();

var azureEmailConnection = builder.Configuration["Email:Azure:ConnectionString"];
var azureEmailSender = builder.Configuration["Email:Azure:SenderAddress"];
if (!string.IsNullOrWhiteSpace(azureEmailConnection) && !string.IsNullOrWhiteSpace(azureEmailSender))
{
    builder.Services.AddSingleton(new EmailClient(azureEmailConnection));
    builder.Services.AddSingleton<IEmailDelivery>(sp => new AzureEmailDelivery(
        sp.GetRequiredService<EmailClient>(), azureEmailSender, sp.GetRequiredService<ILogger<AzureEmailDelivery>>()));
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmailDelivery, LoggedEmailDelivery>();
}
else
{
    builder.Services.AddSingleton<IEmailDelivery, UnconfiguredEmailDelivery>();
}

builder.Services.AddSingleton<IFormFactor, DesktopFormFactor>();
// The institute pages live in this assembly, so the router has to scan it as well as Sapling.Shared.
builder.Services.AddSingleton(new RouteAssemblies(typeof(Sapling.Web.Components.App).Assembly));
builder.Services.AddScoped<IThemeService, BrowserThemeService>();
builder.Services.AddScoped<AssistantChat>();
builder.Services.AddScoped<IRecentFeaturesService, BrowserRecentFeaturesService>();
builder.Services.AddSingleton<ICollegeCatalogService, CollegeCatalogService>();

var app = builder.Build();

if (string.IsNullOrWhiteSpace(googleClientId) != string.IsNullOrWhiteSpace(googleClientSecret))
{
    app.Logger.LogWarning(
        "Google sign-in is off: set both Authentication:Google:ClientId and Authentication:Google:ClientSecret ({Missing} is missing).",
        string.IsNullOrWhiteSpace(googleClientId) ? "ClientId" : "ClientSecret");
}

if (string.IsNullOrWhiteSpace(hfKey))
{
    app.Logger.LogInformation("AI: Ai:HuggingFace:ApiKey is not set, so the scripted offline interviewer is used.");
}
else
{
    app.Logger.LogInformation("AI: Hugging Face Inference Providers, model {Model}.", hfModel);
}

// Find Tectonic and fill its package cache in the background, so no student waits for the one-time downloads.
if (app.Services.GetRequiredService<ILatexCompiler>() is TectonicCompiler tectonic)
{
    _ = tectonic.WarmUpTask;
}

// Pre-initialize in-memory college catalog
_ = Task.Run(() => app.Services.GetRequiredService<ICollegeCatalogService>().InitializeAsync());

// In production Caddy terminates HTTPS and forwards plain HTTP from the same machine. Trusting its headers lets the
// app see the real https scheme and host, which Google sign-in redirects and secure cookies depend on.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
};
forwarded.KnownProxies.Add(System.Net.IPAddress.Loopback);
forwarded.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
app.UseForwardedHeaders(forwarded);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();

    // Left off in development so the MAUI head can reach the API over plain HTTP.
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGroup("/api/identity").MapIdentityApi<AppUser>().RetireUnverifiedIdentityRoutes();
app.MapAccountApi();
app.MapExternalAuth();
app.MapSaplingApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Sapling.Shared._Imports).Assembly)
    .RequireAuthorization(BrowserUserPolicy);

await DemoSeeder.SeedAsync(app.Services);

app.Run();
