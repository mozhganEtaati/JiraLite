using System.Text;
using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.Email;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Infrastructure.RateLimiting;
using JiraLite.Api.Common.Mcp;
using JiraLite.Api.Common.Notifications;
using JiraLite.Api.Features.Admin;
using JiraLite.Api.Features.Auth;
using JiraLite.Api.Features.Backlog;
using JiraLite.Api.Features.Attachments;
using JiraLite.Api.Features.Boards;
using JiraLite.Api.Features.Calendar;
using JiraLite.Api.Features.Dashboard;
using JiraLite.Api.Features.Comments;
using JiraLite.Api.Features.Issues;
using JiraLite.Api.Features.Labels;
using JiraLite.Api.Features.Notifications;
using JiraLite.Api.Features.Projects;
using JiraLite.Api.Features.Sprints;
using JiraLite.Api.Features.Teams;
using JiraLite.Api.Features.Users;
using JiraLite.Api.Features.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

// spec/20-coding-guidelines.md §9 — `dotnet JiraLite.Api.dll --migrate` applies pending migrations
// and exits, so a deployment can run the schema change as its own step (and fail on it) without
// ever starting the API. The switch is stripped before the host sees it; see StripMigrateArgument.
var runMigrateCommand = DatabaseMigrator.IsMigrateCommand(args);

var builder = WebApplication.CreateBuilder(DatabaseMigrator.StripMigrateArgument(args));

// ---- Serilog (spec/20-coding-guidelines.md §6) ----
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---- Configuration / strongly-typed options (spec/20-coding-guidelines.md §8) ----
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();

// Read once here only to fail fast on a misconfigured deployment. It must NOT be captured by the
// DbContext/Hangfire registrations below — see ResolveConnectionString.
var configuredConnectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(configuredConnectionString) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. Set it via the ConnectionStrings__Default environment variable.");
}

// Resolved from the built container's IConfiguration, never captured from builder.Configuration.
// Under WebApplicationFactory the test host layers its own configuration in only once the host is
// actually built, so a value read at builder time is stale — the same trap already documented for
// Jwt:SigningKey below. Capturing it pointed EF and Hangfire at appsettings.Development.json's
// localhost,1433 dev database instead of the integration tests' throwaway SQL Server container.
static string ResolveConnectionString(IServiceProvider serviceProvider) =>
    serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// Skipped for --migrate: that process never issues or validates a token, and requiring the key
// would mean handing the signing secret to the migration job for no reason.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
if (string.IsNullOrWhiteSpace(jwtOptions?.SigningKey) && !builder.Environment.IsDevelopment() && !runMigrateCommand)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is not configured. Set it via the Jwt__SigningKey environment variable.");
}

// ---- EF Core / SQL Server (spec/18-database.md, spec/20-coding-guidelines.md §9) ----
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddDbContext<JiraLiteDbContext>((serviceProvider, options) =>
    options.UseSqlServer(
        ResolveConnectionString(serviceProvider),
        sql => sql.MigrationsAssembly(typeof(JiraLiteDbContext).Assembly.FullName)));

// ---- FluentValidation (validators auto-discovered from this assembly) ----
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ---- Auth building blocks (spec/01-authentication.md) ----
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

// ---- File storage (spec/11-attachments.md pattern; first consumer is Avatar upload, spec/02-users.md) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<FileStorageOptions>()
    .Bind(builder.Configuration.GetSection(FileStorageOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddScoped<IFileStorage, LocalDiskFileStorage>();
builder.Services.AddOptions<AttachmentOptions>()
    .Bind(builder.Configuration.GetSection(AttachmentOptions.SectionName));

// ---- Email (spec/13-notifications.md NFR-01; first consumers are Notification triggers and invitation emails) ----
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<NotificationDispatcher>();

// ---- Workspace invitation config (spec/03-workspaces.md BR-07) ----
builder.Services.AddOptions<InvitationOptions>()
    .Bind(builder.Configuration.GetSection(InvitationOptions.SectionName));

// ---- Password reset config (spec/01-authentication.md BR-09) ----
builder.Services.AddOptions<PasswordResetOptions>()
    .Bind(builder.Configuration.GetSection(PasswordResetOptions.SectionName));

// ---- MCP server (spec/23-mcp-server.md) ----
builder.Services.AddOptions<McpOptions>()
    .Bind(builder.Configuration.GetSection(McpOptions.SectionName));
builder.Services.AddJiraLiteMcp();

// ---- Rate limiting (spec/19-api-guidelines.md §13, spec/01-authentication.md NFR-04) ----
builder.Services.AddJiraLiteRateLimiting(builder.Configuration);

// ---- Problem Details (spec/19-api-guidelines.md §9) ----
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

// ---- JWT Authentication skeleton (spec/01-authentication.md) — no user-facing endpoints yet ----
// TokenValidationParameters is configured lazily from the bound IOptions<JwtOptions> (not the
// `jwtOptions` snapshot above) so it always matches the key JwtTokenService signs with — reading
// builder.Configuration directly here would capture a stale value under WebApplicationFactory,
// which layers in its own config (e.g. a test-only SigningKey) only once the host is actually built.
// The Personal Access Token scheme is registered alongside the JWT one, never as a fallback for
// it: `/api/*` authorizes against the default (JWT) scheme and `/mcp` against "Pat", so neither
// credential type is accepted where the other belongs (spec/23-mcp-server.md BR-02).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    .AddScheme<AuthenticationSchemeOptions, PersonalAccessTokenHandler>(
        PersonalAccessTokenDefaults.Scheme, configureOptions: null);

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, boundJwtOptions) =>
    {
        var jwt = boundJwtOptions.Value;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.SigningKey ?? "dev-only-placeholder-key-not-for-production-1234567890")),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
// ---- Authorization policies (spec/16-rbac.md FR-01 — named policies, never inline role checks) ----
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceMemberAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceAdminAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, OrganizationOwnerAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TeamViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TeamManagementAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TeamWorkspaceAdminAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ProjectViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ProjectManageAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ProjectWorkspaceAdminAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ProjectContributeAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BoardViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BoardManageAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BoardContributeAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SprintViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SprintContributeAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SprintManageAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, IssueViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, IssueContributeAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, IssueManageAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CommentEditAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CommentDeleteAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AttachmentViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AttachmentDeleteAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, LabelManageAuthorizationHandler>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("WorkspaceMember", policy => policy.RequireAuthenticatedUser().AddRequirements(new WorkspaceMemberRequirement()))
    .AddPolicy("WorkspaceAdmin", policy => policy.RequireAuthenticatedUser().AddRequirements(new WorkspaceAdminRequirement()))
    .AddPolicy("OrganizationOwner", policy => policy.RequireAuthenticatedUser().AddRequirements(new OrganizationOwnerRequirement()))
    .AddPolicy("TeamView", policy => policy.RequireAuthenticatedUser().AddRequirements(new TeamViewRequirement()))
    .AddPolicy("TeamManagement", policy => policy.RequireAuthenticatedUser().AddRequirements(new TeamManagementRequirement()))
    .AddPolicy("TeamWorkspaceAdmin", policy => policy.RequireAuthenticatedUser().AddRequirements(new TeamWorkspaceAdminRequirement()))
    .AddPolicy("ProjectView", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProjectViewRequirement()))
    .AddPolicy("ProjectManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProjectManageRequirement()))
    .AddPolicy("ProjectWorkspaceAdmin", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProjectWorkspaceAdminRequirement()))
    .AddPolicy("BoardView", policy => policy.RequireAuthenticatedUser().AddRequirements(new BoardViewRequirement()))
    .AddPolicy("BoardManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new BoardManageRequirement()))
    .AddPolicy("BoardContribute", policy => policy.RequireAuthenticatedUser().AddRequirements(new BoardContributeRequirement()))
    .AddPolicy("SprintView", policy => policy.RequireAuthenticatedUser().AddRequirements(new SprintViewRequirement()))
    .AddPolicy("SprintContribute", policy => policy.RequireAuthenticatedUser().AddRequirements(new SprintContributeRequirement()))
    .AddPolicy("SprintManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new SprintManageRequirement()))
    .AddPolicy("ProjectContribute", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProjectContributeRequirement()))
    .AddPolicy("IssueView", policy => policy.RequireAuthenticatedUser().AddRequirements(new IssueViewRequirement()))
    .AddPolicy("IssueContribute", policy => policy.RequireAuthenticatedUser().AddRequirements(new IssueContributeRequirement()))
    .AddPolicy("IssueManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new IssueManageRequirement()))
    .AddPolicy("CommentEdit", policy => policy.RequireAuthenticatedUser().AddRequirements(new CommentEditRequirement()))
    .AddPolicy("CommentDelete", policy => policy.RequireAuthenticatedUser().AddRequirements(new CommentDeleteRequirement()))
    .AddPolicy("AttachmentView", policy => policy.RequireAuthenticatedUser().AddRequirements(new AttachmentViewRequirement()))
    .AddPolicy("AttachmentDelete", policy => policy.RequireAuthenticatedUser().AddRequirements(new AttachmentDeleteRequirement()))
    .AddPolicy("LabelManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new LabelManageRequirement()));

// ---- Swagger / OpenAPI ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "JiraLite API", Version = "v1" });

    // Every feature nests its DTOs as Request/Response inside the use-case class (spec/20-coding-
    // guidelines.md §3), so the bare type name collides across features (e.g. Login+Request vs
    // Register+Request) — qualify with the declaring type to keep schema IDs unique.
    options.CustomSchemaIds(type => type.DeclaringType is not null
        ? $"{type.DeclaringType.Name}{type.Name}"
        : type.Name);

    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Provide the access token as: Bearer {token}"
    };
    options.AddSecurityDefinition("Bearer", bearerScheme);
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer"), [] }
    });
});

// ---- Hangfire (dashboard + job infrastructure only — no jobs yet, spec/21-roadmap.md Phase 0) ----
builder.Services.AddHangfire((provider, config) => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(ResolveConnectionString(provider), new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));
builder.Services.AddHangfireServer();

// ---- Health checks ----
builder.Services.AddHealthChecks();

var app = builder.Build();

if (runMigrateCommand)
{
    // Returns before app.Run(), so neither the HTTP server nor the Hangfire worker ever starts —
    // this process exists only to move the schema forward. A failure here throws, and the non-zero
    // exit code is what stops the surrounding deployment from rolling the new image out.
    await DatabaseMigrator.MigrateAsync(app.Services);
    return;
}

if (DatabaseMigrator.ShouldMigrateOnStartup(
        app.Environment,
        app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value))
{
    await DatabaseMigrator.MigrateAsync(app.Services);
}

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "JiraLite API v1"));
}

app.UseHttpsRedirection();

// Serve uploaded files (avatars for now) unauthenticated, matching the plain public-URL
// shape in spec/02-users.md — unlike Attachments (Phase 4), which are private and go
// through authenticated download/preview endpoints instead of static serving.
var fileStorageOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileStorageOptions>>().Value;
Directory.CreateDirectory(fileStorageOptions.RootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(fileStorageOptions.RootPath)),
    RequestPath = "/files"
});

app.UseAuthentication();

// After UseAuthentication so the baseline limiter can partition by the caller's user id
// rather than falling back to their IP, and before UseAuthorization so a flood of requests
// is shed before it reaches the database-backed authorization handlers.
app.UseRateLimiter();

app.UseAuthorization();

// Dashboard is unauthenticated for now — no User/role system exists until Phase 1-3.
// Must be locked down (e.g., to Workspace Admins or a dedicated ops role) before any non-local deployment.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new AllowAllDashboardAuthorizationFilter()]
});

app.MapHealthChecks("/health");

// ---- Feature endpoints (spec/20-coding-guidelines.md §2) ----
Register.MapEndpoint(app);
Login.MapEndpoint(app);
Refresh.MapEndpoint(app);
Logout.MapEndpoint(app);
ForgotPassword.MapEndpoint(app);
ResetPassword.MapEndpoint(app);

GetMyProfile.MapEndpoint(app);
UpdateMyProfile.MapEndpoint(app);
UploadAvatar.MapEndpoint(app);
DeleteAvatar.MapEndpoint(app);
GetNotificationPreferences.MapEndpoint(app);
UpdateNotificationPreferences.MapEndpoint(app);
GetPublicProfile.MapEndpoint(app);
DeactivateAccount.MapEndpoint(app);
GetMyActivity.MapEndpoint(app);

// Personal Access Tokens exist only to authenticate MCP clients, so they are mapped with the rest
// of that surface — with Mcp:Enabled off there is no /mcp and no way to mint a credential for it,
// and both 404 rather than existing and refusing (spec/23-mcp-server.md NFR-05).
if (app.Services.IsMcpEnabled())
{
    CreateAccessToken.MapEndpoint(app);
    ListAccessTokens.MapEndpoint(app);
    RevokeAccessToken.MapEndpoint(app);

    // Authorized against the "Pat" scheme only, so a JWT access token presented here is rejected
    // just as a Personal Access Token is rejected by /api/* (BR-02).
    app.MapMcp("/mcp")
        .RequireAuthorization(new AuthorizationPolicyBuilder(PersonalAccessTokenDefaults.Scheme)
            .RequireAuthenticatedUser()
            .Build());
}

CreateOrganization.MapEndpoint(app);
ListMyOrganizations.MapEndpoint(app);
GetOrganization.MapEndpoint(app);
RenameOrganization.MapEndpoint(app);
CreateWorkspace.MapEndpoint(app);
ListMyWorkspaces.MapEndpoint(app);
GetWorkspace.MapEndpoint(app);
UpdateWorkspace.MapEndpoint(app);
ArchiveWorkspace.MapEndpoint(app);
GetMyWorkspaceRole.MapEndpoint(app);
ListWorkspaceMembers.MapEndpoint(app);
ChangeMemberRole.MapEndpoint(app);
RemoveMember.MapEndpoint(app);
LeaveWorkspace.MapEndpoint(app);
ListInvitations.MapEndpoint(app);
CreateInvitation.MapEndpoint(app);
RevokeInvitation.MapEndpoint(app);
AcceptInvitation.MapEndpoint(app);
DeclineInvitation.MapEndpoint(app);

CreateTeam.MapEndpoint(app);
ListTeams.MapEndpoint(app);
GetTeam.MapEndpoint(app);
RenameTeam.MapEndpoint(app);
DeleteTeam.MapEndpoint(app);
AddTeamMember.MapEndpoint(app);
RemoveTeamMember.MapEndpoint(app);
SetTeamLead.MapEndpoint(app);

CreateProject.MapEndpoint(app);
GetProject.MapEndpoint(app);
ListProjects.MapEndpoint(app);
GetMyProjectRole.MapEndpoint(app);
EditProject.MapEndpoint(app);
ArchiveProject.MapEndpoint(app);
UnarchiveProject.MapEndpoint(app);
DeleteProject.MapEndpoint(app);
ListProjectMembers.MapEndpoint(app);
AddProjectMember.MapEndpoint(app);
ChangeProjectMemberRole.MapEndpoint(app);
RemoveProjectMember.MapEndpoint(app);
ListBoards.MapEndpoint(app);
GetBoard.MapEndpoint(app);
GetBoardIssues.MapEndpoint(app);
CreateBoard.MapEndpoint(app);
RenameBoard.MapEndpoint(app);
DeleteBoard.MapEndpoint(app);
AddColumn.MapEndpoint(app);
EditColumn.MapEndpoint(app);
DeleteColumn.MapEndpoint(app);
ReorderColumns.MapEndpoint(app);
CreateSprint.MapEndpoint(app);
ListSprints.MapEndpoint(app);
GetSprint.MapEndpoint(app);
EditSprint.MapEndpoint(app);
StartSprint.MapEndpoint(app);
CompleteSprint.MapEndpoint(app);
DeleteSprint.MapEndpoint(app);
AddSprintIssue.MapEndpoint(app);
RemoveSprintIssue.MapEndpoint(app);
GetSprintReport.MapEndpoint(app);

CreateIssue.MapEndpoint(app);
GetIssue.MapEndpoint(app);
ListIssues.MapEndpoint(app);
ListSubtasks.MapEndpoint(app);
EditIssue.MapEndpoint(app);
MoveIssue.MapEndpoint(app);
BlockIssue.MapEndpoint(app);
UnblockIssue.MapEndpoint(app);
DeleteIssue.MapEndpoint(app);
GetProductBacklog.MapEndpoint(app);
GetSprintBacklog.MapEndpoint(app);
RepositionIssueRank.MapEndpoint(app);
ListComments.MapEndpoint(app);
AddComment.MapEndpoint(app);
EditComment.MapEndpoint(app);
DeleteComment.MapEndpoint(app);
ListAttachments.MapEndpoint(app);
UploadAttachment.MapEndpoint(app);
DownloadAttachment.MapEndpoint(app);
PreviewAttachment.MapEndpoint(app);
DeleteAttachment.MapEndpoint(app);
ListLabels.MapEndpoint(app);
CreateLabel.MapEndpoint(app);
EditLabel.MapEndpoint(app);
DeleteLabel.MapEndpoint(app);
AttachLabel.MapEndpoint(app);
DetachLabel.MapEndpoint(app);

ListNotifications.MapEndpoint(app);
GetUnreadCount.MapEndpoint(app);
MarkNotificationRead.MapEndpoint(app);
MarkAllNotificationsRead.MapEndpoint(app);

GetMyTasks.MapEndpoint(app);
GetMyProjects.MapEndpoint(app);
GetMyStats.MapEndpoint(app);
GetRecentActivity.MapEndpoint(app);
GetDueDates.MapEndpoint(app);
GetSprintTimeline.MapEndpoint(app);
GetAdminOverview.MapEndpoint(app);
ListAdminUsers.MapEndpoint(app);
ListAdminProjects.MapEndpoint(app);
GetRoleCatalog.MapEndpoint(app);

app.Run();

public partial class Program;
