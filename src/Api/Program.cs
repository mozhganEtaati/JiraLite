using System.Text;
using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions?.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions?.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions?.SigningKey ?? "dev-only-placeholder-key-not-for-production-1234567890")),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();

// Dashboard is unauthenticated for now — no User/role system exists until Phase 1-3.
// Must be locked down (e.g., to Workspace Admins or a dedicated ops role) before any non-local deployment.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new AllowAllDashboardAuthorizationFilter()]
});

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
