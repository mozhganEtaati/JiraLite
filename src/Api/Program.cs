using System.Text;
using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Features.Auth;
using JiraLite.Api.Features.Boards;
using JiraLite.Api.Features.Projects;
using JiraLite.Api.Features.Teams;
using JiraLite.Api.Features.Users;
using JiraLite.Api.Features.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog (spec/20-coding-guidelines.md §6) ----
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---- Configuration / strongly-typed options (spec/20-coding-guidelines.md §8) ----
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. Set it via the ConnectionStrings__Default environment variable.");
}

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
if (string.IsNullOrWhiteSpace(jwtOptions?.SigningKey) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is not configured. Set it via the Jwt__SigningKey environment variable.");
}

// ---- EF Core / SQL Server (spec/18-database.md, spec/20-coding-guidelines.md §9) ----
builder.Services.AddDbContext<JiraLiteDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(JiraLiteDbContext).Assembly.FullName)));

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

// ---- Workspace invitation config (spec/03-workspaces.md BR-07) ----
builder.Services.AddOptions<InvitationOptions>()
    .Bind(builder.Configuration.GetSection(InvitationOptions.SectionName));

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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

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
builder.Services.AddScoped<IAuthorizationHandler, BoardViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BoardManageAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BoardContributeAuthorizationHandler>();

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
    .AddPolicy("BoardContribute", policy => policy.RequireAuthenticatedUser().AddRequirements(new BoardContributeRequirement()));

// ---- Swagger / OpenAPI ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "JiraLite API", Version = "v1" });

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
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
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

GetMyProfile.MapEndpoint(app);
UpdateMyProfile.MapEndpoint(app);
UploadAvatar.MapEndpoint(app);
DeleteAvatar.MapEndpoint(app);
GetNotificationPreferences.MapEndpoint(app);
UpdateNotificationPreferences.MapEndpoint(app);
GetPublicProfile.MapEndpoint(app);
DeactivateAccount.MapEndpoint(app);

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
CreateBoard.MapEndpoint(app);
RenameBoard.MapEndpoint(app);
DeleteBoard.MapEndpoint(app);
AddColumn.MapEndpoint(app);
EditColumn.MapEndpoint(app);
DeleteColumn.MapEndpoint(app);

app.Run();

public partial class Program;
