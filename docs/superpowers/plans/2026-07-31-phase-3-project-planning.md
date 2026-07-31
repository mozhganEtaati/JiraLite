# Phase 3 — Project Planning — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement all 8 tasks of `spec/21-roadmap.md` Phase 3 (T022, T014, T028, T023, T024, T025, T027, T026): `Project`/`ProjectMember`, `ActivityLogEntry` (+ its read endpoint, deferred from Phase 1), Project-scoped RBAC, Project CRUD, Project member management, `Board`/`BoardColumn` (+ default-board bootstrap), `Sprint` lifecycle, and Board/Column CRUD + reorder.

**Architecture:** Vertical Slice Architecture per `spec/20-coding-guidelines.md` — one file per use case in `Features/{Projects,Boards,Sprints}`, shared entities/config in `Common/Domain` and `Common/Infrastructure/Persistence`, named authorization policies in `Common/Auth`. This phase also introduces the project's first integration test project (xUnit + Testcontainers `MsSqlContainer` + `WebApplicationFactory<Program>`) and its first cursor-paginated endpoint.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 / SQL Server, FluentValidation, xUnit, Testcontainers.MsSql, Microsoft.AspNetCore.Mvc.Testing.

## Global Constraints

- Every `Request`/`Response` is a `record`; every use case is one static class in one file named `{Verb}{Entity}.cs`, per `spec/20-coding-guidelines.md` §3.
- No inline role-string checks in handlers — every write endpoint uses a named `RequireAuthorization("PolicyName")` policy, per `spec/16-rbac.md` FR-01.
- All validation lives in a colocated `Validator : AbstractValidator<Request>`; handlers only do DB-dependent business-rule checks, per `spec/20-coding-guidelines.md` §5.
- All error responses are RFC 7807 Problem Details via `ProblemResults` helpers or `Results.NotFound()`/`Results.Ok()`/etc., per `spec/19-api-guidelines.md` §9.
- Timestamps are UTC `DateTime`; date-only fields (`PlannedStartDateUtc`, `PlannedEndDateUtc`) are `DateOnly` mapped to SQL `date`.
- Migrations are generated with `dotnet ef migrations add`, never hand-written from scratch.
- **Three deferrals to Phase 4** (documented in Task 18): Sprint completion carry-forward (BR-05), `POST/DELETE /sprints/{id}/issues`, `GET /boards/{id}/issues`, and Board/Column delete guards BR-03/BR-05 — all require the `Issue` entity, which doesn't exist until Phase 4.
- **Retrofit required** (Task 9): `RemoveMember.cs`/`LeaveWorkspace.cs` already contain a comment noting their `ProjectMember` cascade (`spec/03-workspaces.md` BR-08) is a no-op until Phase 3 — now that `ProjectMember` exists, wire it up.

---

## File Structure

```
src/Api/
  Common/Domain/
    Project.cs, ProjectMember.cs, ProjectRole.cs
    Board.cs, BoardType.cs, BoardColumn.cs
    Sprint.cs, SprintStatus.cs
    ActivityLogEntry.cs
  Common/Infrastructure/Persistence/
    Configurations/ProjectConfiguration.cs, ProjectMemberConfiguration.cs,
                   BoardConfiguration.cs, BoardColumnConfiguration.cs,
                   SprintConfiguration.cs, ActivityLogEntryConfiguration.cs
    JiraLiteDbContext.cs (modified — new DbSets)
  Common/Pagination/CursorPagination.cs
  Common/Auth/ProjectAuthorization.cs, BoardAuthorization.cs, SprintAuthorization.cs
  Features/Projects/
    CreateProject.cs, GetProject.cs, ListProjects.cs, GetMyProjectRole.cs,
    EditProject.cs, ArchiveProject.cs, UnarchiveProject.cs, DeleteProject.cs,
    ListProjectMembers.cs, AddProjectMember.cs, ChangeProjectMemberRole.cs, RemoveProjectMember.cs
  Features/Boards/
    ListBoards.cs, GetBoard.cs, CreateBoard.cs, RenameBoard.cs, DeleteBoard.cs,
    AddColumn.cs, EditColumn.cs, DeleteColumn.cs, ReorderColumns.cs
  Features/Sprints/
    ListSprints.cs, GetSprint.cs, CreateSprint.cs, EditSprint.cs,
    StartSprint.cs, CompleteSprint.cs, DeleteSprint.cs
  Features/Users/GetMyActivity.cs
  Features/Workspaces/RemoveMember.cs, LeaveWorkspace.cs (modified — retrofit)
  Program.cs (modified — DI/policy/endpoint registration)
tests/JiraLite.Api.IntegrationTests/
  JiraLite.Api.IntegrationTests.csproj
  JiraLiteApiFactory.cs, DatabaseResetHelper.cs, TestDataHelper.cs
  HealthCheckTests.cs
  Persistence/ProjectPlanningSchemaTests.cs
  Pagination/CursorPaginationTests.cs
  Projects/CreateProjectTests.cs, GetProjectTests.cs, EditArchiveProjectTests.cs,
           DeleteProjectTests.cs, ProjectMemberTests.cs
  Workspaces/RemoveMemberCascadeTests.cs
  Boards/BoardTests.cs, ColumnTests.cs, ReorderColumnsTests.cs
  Sprints/SprintLifecycleTests.cs, StartSprintTests.cs, CompleteDeleteSprintTests.cs
  Users/GetMyActivityTests.cs
spec/21-roadmap.md (modified — Phase 4 deferral note)
JiraLite.slnx (modified — add test project)
```

---

### Task 1: Integration test project scaffold

**Files:**
- Create: `tests/JiraLite.Api.IntegrationTests/JiraLite.Api.IntegrationTests.csproj`
- Create: `tests/JiraLite.Api.IntegrationTests/JiraLiteApiFactory.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/DatabaseResetHelper.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/HealthCheckTests.cs`
- Modify: `JiraLite.slnx`

**Interfaces:**
- Produces: `JiraLiteApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` — exposes `HttpClient CreateAuthenticatedClientAsync()`-free base client via `CreateClient()`; `Services` for direct `JiraLiteDbContext` access in tests.
- Produces: `DatabaseResetHelper.ResetAsync(JiraLiteDbContext db)` — deletes all rows from every Phase 0-3 table in FK-safe order, for use in each test's setup.

- [ ] **Step 1: Create the test project file**

```xml
<!-- tests/JiraLite.Api.IntegrationTests/JiraLite.Api.IntegrationTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.10" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="Testcontainers.MsSql" Version="4.1.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Api\JiraLite.Api.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the project to the solution**

Modify `JiraLite.slnx` to add a `/tests/` folder with the new project, mirroring the existing `/src/Api/` entry:

```xml
<Solution>
  <Folder Name="/src/" />
  <Folder Name="/src/Api/">
    <Project Path="src/Api/JiraLite.Api.csproj" />
  </Folder>
  <Folder Name="/tests/" />
  <Folder Name="/tests/JiraLite.Api.IntegrationTests/">
    <Project Path="tests/JiraLite.Api.IntegrationTests/JiraLite.Api.IntegrationTests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 3: Write the WebApplicationFactory with a real SQL Server container**

```csharp
// tests/JiraLite.Api.IntegrationTests/JiraLiteApiFactory.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Xunit;

namespace JiraLite.Api.IntegrationTests;

public class JiraLiteApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("IntegrationTest_Passw0rd!")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _sqlContainer.GetConnectionString(),
                ["Jwt:SigningKey"] = "integration-test-signing-key-not-for-production-1234567890",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "jiralite-tests", Guid.NewGuid().ToString())
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

- [ ] **Step 4: Write the database reset helper**

```csharp
// tests/JiraLite.Api.IntegrationTests/DatabaseResetHelper.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.IntegrationTests;

/// <summary>Truncates every table in FK-safe (child-before-parent) order between tests.</summary>
public static class DatabaseResetHelper
{
    private static readonly string[] TablesInDeleteOrder =
    [
        "ActivityLogEntry",
        "Sprint", "BoardColumn", "Board",
        "ProjectMember", "Project",
        "TeamMember", "Team",
        "Invitation", "WorkspaceMember", "Workspace", "Organization",
        "RefreshToken", "NotificationPreference", "UserProfile", "User"
    ];

    public static async Task ResetAsync(JiraLiteDbContext db)
    {
        foreach (var table in TablesInDeleteOrder)
        {
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM [{table}]");
        }
    }
}
```

- [ ] **Step 5: Write the smoke test**

```csharp
// tests/JiraLite.Api.IntegrationTests/HealthCheckTests.cs
using System.Net;
using Xunit;

namespace JiraLite.Api.IntegrationTests;

public class HealthCheckTests : IClassFixture<JiraLiteApiFactory>
{
    private readonly JiraLiteApiFactory _factory;

    public HealthCheckTests(JiraLiteApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 6: Run it to verify the container boots and the test passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter HealthCheckTests`
Expected: PASS (first run pulls the `mssql/server:2022-latest` image — may take a minute; requires Docker running locally).

- [ ] **Step 7: Commit**

```bash
git add tests/JiraLite.Api.IntegrationTests JiraLite.slnx
git commit -m "test: scaffold Testcontainers-backed integration test project"
```

---

### Task 2: Phase 3 domain entities, EF configurations, and migration

**Files:**
- Create: `src/Api/Common/Domain/Project.cs`, `ProjectMember.cs`, `ProjectRole.cs`, `Board.cs`, `BoardType.cs`, `BoardColumn.cs`, `Sprint.cs`, `SprintStatus.cs`, `ActivityLogEntry.cs`
- Create: `src/Api/Common/Infrastructure/Persistence/Configurations/ProjectConfiguration.cs`, `ProjectMemberConfiguration.cs`, `BoardConfiguration.cs`, `BoardColumnConfiguration.cs`, `SprintConfiguration.cs`, `ActivityLogEntryConfiguration.cs`
- Modify: `src/Api/Common/Infrastructure/Persistence/JiraLiteDbContext.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Persistence/ProjectPlanningSchemaTests.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/TestDataHelper.cs`

**Interfaces:**
- Produces: `Project`, `ProjectMember`, `Board`, `BoardColumn`, `Sprint`, `ActivityLogEntry` domain classes matching `spec/18-database.md` §6-7 exactly (field names/types), used by every later task.
- Produces: `ProjectRole.{ProjectAdmin,Developer,Viewer,All}`, `BoardType.{Scrum,Kanban,All}`, `SprintStatus.{Planned,Active,Completed}` string constants.
- Produces: `TestDataHelper` with `RegisterAndLoginAsync(HttpClient)`, `CreateWorkspaceAsync(HttpClient, string token)` — seed helpers reused by every later test file.

- [ ] **Step 1: Write the domain entities**

```csharp
// src/Api/Common/Domain/Project.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>Container for Boards/Sprints/Issues within a Workspace. spec/18-database.md §6, spec/05-projects.md.</summary>
public class Project
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public required string Key { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

```csharp
// src/Api/Common/Domain/ProjectMember.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>User↔Project membership with role. spec/18-database.md §6, spec/05-projects.md.</summary>
public class ProjectMember
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid UserId { get; init; }
    public required string Role { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
```

```csharp
// src/Api/Common/Domain/ProjectRole.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>spec/16-rbac.md — ProjectMember.Role values.</summary>
public static class ProjectRole
{
    public const string ProjectAdmin = "ProjectAdmin";
    public const string Developer = "Developer";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [ProjectAdmin, Developer, Viewer];
}
```

```csharp
// src/Api/Common/Domain/Board.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>Visual arrangement of a Project's Issues. spec/18-database.md §6, spec/06-boards.md.</summary>
public class Board
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public required string Type { get; init; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

```csharp
// src/Api/Common/Domain/BoardType.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>spec/06-boards.md — Board.Type values.</summary>
public static class BoardType
{
    public const string Scrum = "Scrum";
    public const string Kanban = "Kanban";

    public static readonly string[] All = [Scrum, Kanban];
}
```

```csharp
// src/Api/Common/Domain/BoardColumn.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>Status lane on a Board. spec/18-database.md §6, spec/06-boards.md.</summary>
public class BoardColumn
{
    public Guid Id { get; init; }
    public Guid BoardId { get; init; }
    public required string Name { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsDoneColumn { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
```

```csharp
// src/Api/Common/Domain/Sprint.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>Time-boxed iteration on a Scrum Board. spec/18-database.md §6, spec/08-sprints.md.</summary>
public class Sprint
{
    public Guid Id { get; init; }
    public Guid BoardId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public string? Goal { get; set; }
    public required string Status { get; set; }
    public DateOnly PlannedStartDateUtc { get; set; }
    public DateOnly PlannedEndDateUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
```

```csharp
// src/Api/Common/Domain/SprintStatus.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>spec/08-sprints.md BR-02 — Sprint.Status values, strictly linear.</summary>
public static class SprintStatus
{
    public const string Planned = "Planned";
    public const string Active = "Active";
    public const string Completed = "Completed";
}
```

```csharp
// src/Api/Common/Domain/ActivityLogEntry.cs
namespace JiraLite.Api.Common.Domain;

/// <summary>
/// Immutable, append-only record of platform actions. spec/18-database.md §4, spec/02-users.md.
/// Written only by feature handlers (BR-05) — no endpoint creates/edits/deletes these directly.
/// </summary>
public class ActivityLogEntry
{
    public Guid Id { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid WorkspaceId { get; init; }
    public Guid? ProjectId { get; set; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public required string Action { get; init; }
    public required string Summary { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}
```

- [ ] **Step 2: Write the EF Core configurations**

```csharp
// src/Api/Common/Infrastructure/Persistence/Configurations/ProjectConfiguration.cs
using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Project");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Key).HasMaxLength(10).UseCollation("SQL_Latin1_General_CP1_CI_AS").IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.IsArchived).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.WorkspaceId, p.Key }).IsUnique();

        builder.HasOne<Workspace>().WithMany().HasForeignKey(p => p.WorkspaceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

```csharp
// src/Api/Common/Infrastructure/Persistence/Configurations/ProjectMemberConfiguration.cs
using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMember");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasMaxLength(20).IsRequired();
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        builder.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();

        builder.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

```csharp
// src/Api/Common/Infrastructure/Persistence/Configurations/BoardConfiguration.cs
using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Board");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(100).IsRequired();
        builder.Property(b => b.Type).HasMaxLength(20).IsRequired();
        builder.Property(b => b.DisplayOrder).IsRequired();
        builder.Property(b => b.CreatedAtUtc).IsRequired();
        builder.Property(b => b.UpdatedAtUtc).IsRequired();

        builder.HasOne<Project>().WithMany().HasForeignKey(b => b.ProjectId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

```csharp
// src/Api/Common/Infrastructure/Persistence/Configurations/BoardColumnConfiguration.cs
using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.ToTable("BoardColumn");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.DisplayOrder).IsRequired();
        builder.Property(c => c.IsDefault).IsRequired();
        builder.Property(c => c.IsDoneColumn).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasOne<Board>().WithMany().HasForeignKey(c => c.BoardId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// src/Api/Common/Infrastructure/Persistence/Configurations/SprintConfiguration.cs
using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("Sprint");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Goal).HasMaxLength(500);
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
        builder.Property(s => s.PlannedStartDateUtc).HasColumnType("date").IsRequired();
        builder.Property(s => s.PlannedEndDateUtc).HasColumnType("date").IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();

        // spec/08-sprints.md BR-01 + NFR-01: DB-enforced atomicity for "at most one Active
        // Sprint per Board" — a filtered unique index closes the race a check-then-insert
        // in application code alone cannot, under concurrent StartSprint calls.
        builder.HasIndex(s => s.BoardId)
            .IsUnique()
            .HasFilter("[Status] = N'Active'")
            .HasDatabaseName("IX_Sprint_BoardId_ActiveOnly");

        builder.HasOne<Board>().WithMany().HasForeignKey(s => s.BoardId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Project>().WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

```csharp
// src/Api/Common/Infrastructure/Persistence/Configurations/ActivityLogEntryConfiguration.cs
using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class ActivityLogEntryConfiguration : IEntityTypeConfiguration<ActivityLogEntry>
{
    public void Configure(EntityTypeBuilder<ActivityLogEntry> builder)
    {
        builder.ToTable("ActivityLogEntry");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Summary).HasMaxLength(500).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();

        builder.HasIndex(e => new { e.WorkspaceId, e.OccurredAtUtc });
        builder.HasIndex(e => new { e.ActorUserId, e.OccurredAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(e => e.WorkspaceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.NoAction);
    }
}
```

- [ ] **Step 3: Register the new DbSets**

Modify `src/Api/Common/Infrastructure/Persistence/JiraLiteDbContext.cs`, adding after `TeamMembers`:

```csharp
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();
```

- [ ] **Step 4: Generate the migration**

Run: `dotnet ef migrations add AddProjectPlanningEntities --project src/Api --startup-project src/Api`
Expected: creates `Migrations/{timestamp}_AddProjectPlanningEntities.cs` and updates `JiraLiteDbContextModelSnapshot.cs`. Open the generated migration and confirm it contains `CreateTable` calls for `Project`, `ProjectMember`, `Board`, `BoardColumn`, `Sprint`, `ActivityLogEntry`, the unique filtered index `IX_Sprint_BoardId_ActiveOnly`, and the `rowversion` column on `BoardColumn`.

- [ ] **Step 5: Write the shared test data helper**

```csharp
// tests/JiraLite.Api.IntegrationTests/TestDataHelper.cs
using System.Net.Http.Json;

namespace JiraLite.Api.IntegrationTests;

public static class TestDataHelper
{
    public sealed record RegisteredUser(Guid UserId, string Email, string AccessToken);

    public static async Task<RegisteredUser> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        const string password = "Test_Passw0rd!";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        registerResponse.EnsureSuccessStatusCode();
        var registered = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();

        return new RegisteredUser(
            registered.GetProperty("id").GetGuid(),
            email,
            login.GetProperty("accessToken").GetString()!);
    }

    public static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        var orgResponse = await client.PostAsJsonAsync("/api/organizations", new { name = $"Org-{Guid.NewGuid():N}" });
        orgResponse.EnsureSuccessStatusCode();
        var org = await orgResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orgId = org.GetProperty("id").GetGuid();

        var wsResponse = await client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/workspaces", new { name = $"Workspace-{Guid.NewGuid():N}" });
        wsResponse.EnsureSuccessStatusCode();
        var workspace = await wsResponse.Content.ReadFromJsonAsync<JsonElement>();
        return workspace.GetProperty("id").GetGuid();
    }
}
```

Add `using System.Text.Json;` to the top of the file (for `JsonElement`).

- [ ] **Step 6: Write the schema/constraint test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Persistence/ProjectPlanningSchemaTests.cs
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Persistence;

public class ProjectPlanningSchemaTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ProjectPlanningSchemaTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Project_key_is_unique_per_workspace_case_insensitively()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        var user = new User { Id = Guid.NewGuid(), Email = "schema-test@example.com", PasswordHash = "x", IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", OwnerUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var workspace = new Workspace { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "WS", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.Users.Add(user);
        db.Organizations.Add(org);
        db.Workspaces.Add(workspace);
        db.Projects.Add(new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "JIRA", Name = "P1", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Projects.Add(new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "jira", Name = "P2", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sprint_active_uniqueness_index_rejects_a_second_active_sprint_on_the_same_board()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        var user = new User { Id = Guid.NewGuid(), Email = "schema-test-2@example.com", PasswordHash = "x", IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", OwnerUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var workspace = new Workspace { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "WS", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var project = new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "JIRA", Name = "P1", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Main", Type = BoardType.Scrum, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.AddRange(user, org, workspace, project, board);
        db.Sprints.Add(new Sprint { Id = Guid.NewGuid(), BoardId = board.Id, ProjectId = project.Id, Name = "S1", Status = SprintStatus.Active, PlannedStartDateUtc = DateOnly.FromDateTime(DateTime.UtcNow), PlannedEndDateUtc = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Sprints.Add(new Sprint { Id = Guid.NewGuid(), BoardId = board.Id, ProjectId = project.Id, Name = "S2", Status = SprintStatus.Active, PlannedStartDateUtc = DateOnly.FromDateTime(DateTime.UtcNow), PlannedEndDateUtc = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
```

- [ ] **Step 7: Run the tests to verify they fail (entities don't exist yet before Steps 1-4) then pass after**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter ProjectPlanningSchemaTests`
Expected: PASS (both tests) once Steps 1-4 are done. If run before Step 1, this won't even compile — confirming the test drives out the entities.

- [ ] **Step 8: Commit**

```bash
git add src/Api/Common/Domain src/Api/Common/Infrastructure/Persistence tests/JiraLite.Api.IntegrationTests
git commit -m "feat: add Project Planning domain entities, EF configurations, and migration"
```

---

### Task 3: Cursor pagination helper

**Files:**
- Create: `src/Api/Common/Pagination/CursorPagination.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Pagination/CursorPaginationTests.cs`

**Interfaces:**
- Produces: `CursorPagination.PageInfo(bool HasNextPage, string? NextCursor)`, `CursorPagination.DecodeOffset(string? cursor)`, `CursorPagination.EncodeOffset(int offset)` — consumed by `GetMyActivity` (Task 17) and any future cursor-paginated endpoint.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Pagination/CursorPaginationTests.cs
using JiraLite.Api.Common.Pagination;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Pagination;

public class CursorPaginationTests
{
    [Fact]
    public void Encode_then_decode_round_trips_the_offset()
    {
        var cursor = CursorPagination.EncodeOffset(25);

        var decoded = CursorPagination.DecodeOffset(cursor);

        Assert.Equal(25, decoded);
    }

    [Fact]
    public void Decode_of_null_cursor_returns_zero()
    {
        Assert.Equal(0, CursorPagination.DecodeOffset(null));
    }

    [Fact]
    public void Decode_of_malformed_cursor_throws_a_bad_request_friendly_exception()
    {
        Assert.Throws<FormatException>(() => CursorPagination.DecodeOffset("not-a-valid-cursor"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter CursorPaginationTests`
Expected: FAIL to compile — `JiraLite.Api.Common.Pagination` namespace doesn't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Api/Common/Pagination/CursorPagination.cs
using System.Text;
using System.Text.Json;

namespace JiraLite.Api.Common.Pagination;

/// <summary>
/// Opaque, server-generated cursor tokens for list endpoints, per spec/19-api-guidelines.md §5.
/// V1 encodes a plain offset — clients must treat the string as opaque and never construct one.
/// </summary>
public static class CursorPagination
{
    public record PageInfo(bool HasNextPage, string? NextCursor);

    private record CursorPayload(int Offset);

    public static string EncodeOffset(int offset)
    {
        var json = JsonSerializer.Serialize(new CursorPayload(offset));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static int DecodeOffset(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var payload = JsonSerializer.Deserialize<CursorPayload>(json)
            ?? throw new FormatException("Cursor payload deserialized to null.");
        return payload.Offset;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter CursorPaginationTests`
Expected: PASS (3 tests). Note: `Convert.FromBase64String` on a non-base64 string like `"not-a-valid-cursor"` throws `FormatException` directly, satisfying the third test without extra handling.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Common/Pagination tests/JiraLite.Api.IntegrationTests/Pagination
git commit -m "feat: add opaque cursor pagination helper"
```

---

### Task 4: CreateProject (+ default Board/columns bootstrap)

**Files:**
- Create: `src/Api/Features/Projects/CreateProject.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Projects/CreateProjectTests.cs`

**Interfaces:**
- Consumes: existing `"WorkspaceAdmin"` policy; `Project`, `ProjectMember`, `ProjectRole`, `Board`, `BoardType`, `BoardColumn` from Task 2.
- Produces: `POST /api/workspaces/{workspaceId}/projects` — used by every later Projects test as setup.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Projects/CreateProjectTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class CreateProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CreateProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Workspace_admin_creates_a_project_and_gets_a_default_board_with_three_columns()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/projects",
            new { key = "JIRA", name = "JiraLite Platform", description = "Core work" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = body.GetProperty("id").GetGuid();
        Assert.Equal("JIRA", body.GetProperty("key").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var creatorMembership = await db.ProjectMembers.SingleAsync(m => m.ProjectId == projectId && m.UserId == admin.UserId);
        Assert.Equal(ProjectRole.ProjectAdmin, creatorMembership.Role);

        var board = await db.Boards.SingleAsync(b => b.ProjectId == projectId);
        Assert.Equal("Main Board", board.Name);
        Assert.Equal(BoardType.Kanban, board.Type);
        var columns = await db.BoardColumns.Where(c => c.BoardId == board.Id).OrderBy(c => c.DisplayOrder).ToListAsync();
        Assert.Equal(3, columns.Count);
        Assert.True(columns[0].IsDefault);
        Assert.True(columns[2].IsDoneColumn);
    }

    [Fact]
    public async Task Duplicate_key_within_the_same_workspace_is_rejected_case_insensitively()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null });

        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "jira", name = "P2", description = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
```

Add `using System.Text.Json;` at the top of the file.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter CreateProjectTests`
Expected: FAIL — `POST /api/workspaces/{workspaceId}/projects` returns 404 (route not mapped).

- [ ] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Projects/CreateProject.cs
using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-01, FR-02, BR-03; spec/06-boards.md FR-01 (default Board bootstrap).</summary>
public static class CreateProject
{
    public record Request(string Key, string Name, string? Description);

    public record Response(Guid Id, Guid WorkspaceId, string Key, string Name, string? Description, bool IsArchived, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Key).NotEmpty().Length(2, 10).Matches("^[A-Za-z][A-Za-z0-9]*$")
                .WithMessage("Key must be 2-10 letters/digits starting with a letter.");
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).MaximumLength(1000);
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var workspace = await db.Workspaces.SingleOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound();
            }

            if (workspace.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/workspace-archived",
                    "Cannot create a Project in an archived Workspace.");
            }

            var key = request.Key.Trim().ToUpperInvariant();
            if (await db.Projects.AnyAsync(p => p.WorkspaceId == workspaceId && p.Key == key, cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/duplicate-project-key",
                    $"A Project with key '{key}' already exists in this Workspace.");
            }

            var now = DateTime.UtcNow;
            var userId = caller.GetUserId();

            var project = new Project
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Key = key,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                IsArchived = false,
                CreatedByUserId = userId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Projects.Add(project);

            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                UserId = userId,
                Role = ProjectRole.ProjectAdmin,
                CreatedAtUtc = now
            });

            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Main Board",
                Type = BoardType.Kanban,
                DisplayOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Boards.Add(board);

            db.BoardColumns.AddRange(
                new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "To Do", DisplayOrder = 0, IsDefault = true, IsDoneColumn = false },
                new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "In Progress", DisplayOrder = 1, IsDefault = false, IsDoneColumn = false },
                new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Done", DisplayOrder = 2, IsDefault = false, IsDoneColumn = true });

            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/projects/{project.Id}",
                new Response(project.Id, project.WorkspaceId, project.Key, project.Name, project.Description, project.IsArchived, project.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/workspaces/{workspaceId:guid}/projects", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Projects");
}
```

- [ ] **Step 4: Register the endpoint in Program.cs**

Add `using JiraLite.Api.Features.Projects;` to the top of `src/Api/Program.cs`, and add after the `SetTeamLead.MapEndpoint(app);` line:

```csharp
CreateProject.MapEndpoint(app);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter CreateProjectTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Api/Features/Projects/CreateProject.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Projects
git commit -m "feat: add CreateProject with default Board/columns bootstrap"
```

---

### Task 5: Project-scoped authorization + GetProject + ListProjects + GetMyProjectRole

**Files:**
- Create: `src/Api/Common/Auth/ProjectAuthorization.cs`
- Create: `src/Api/Features/Projects/GetProject.cs`, `ListProjects.cs`, `GetMyProjectRole.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Projects/GetProjectTests.cs`

**Interfaces:**
- Produces: policies `"ProjectView"`, `"ProjectManage"`, `"ProjectWorkspaceAdmin"` — consumed by every remaining Projects/Boards/Sprints task.
- Produces: `file static class ProjectAuthorizationQueries` — internal to the auth file, not consumed elsewhere.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Projects/GetProjectTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class GetProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public GetProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid WorkspaceId, Guid ProjectId, string AdminToken)> SeedProjectAsync(HttpClient client)
    {
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null });
        var project = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (workspaceId, project.GetProperty("id").GetGuid(), admin.AccessToken);
    }

    [Fact]
    public async Task Project_admin_can_get_the_project_they_created()
    {
        var client = _factory.CreateClient();
        var (_, projectId, token) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unrelated_user_is_forbidden_from_viewing_the_project()
    {
        var client = _factory.CreateClient();
        var (_, projectId, _) = await SeedProjectAsync(client);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", outsider.AccessToken);

        var response = await client.GetAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task My_role_endpoint_returns_null_effective_role_for_a_user_with_no_access()
    {
        var client = _factory.CreateClient();
        var (_, projectId, _) = await SeedProjectAsync(client);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", outsider.AccessToken);

        var response = await client.GetAsync($"/api/projects/{projectId}/my-role");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("effectiveRole").ValueKind);
    }
}
```

Add `using System.Text.Json;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter GetProjectTests`
Expected: FAIL — routes not mapped (404 instead of 200/403).

- [x] **Step 3: Write the authorization policies**

```csharp
// src/Api/Common/Auth/ProjectAuthorization.cs
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Project named by "projectId", or Workspace Admin. spec/16-rbac.md BR-02.</summary>
public class ProjectViewRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on "projectId", or Workspace Admin. spec/16-rbac.md BR-02/BR-03.</summary>
public class ProjectManageRequirement : IAuthorizationRequirement;

/// <summary>Caller is WorkspaceMember.Role = Admin on the Workspace owning "projectId" — no ProjectAdmin fallback. spec/05-projects.md BR-07.</summary>
public class ProjectWorkspaceAdminRequirement : IAuthorizationRequirement;

file static class ProjectAuthorizationQueries
{
    public static async Task<Guid?> GetWorkspaceIdAsync(JiraLiteDbContext db, Guid projectId) =>
        await db.Projects.Where(p => p.Id == projectId).Select(p => (Guid?)p.WorkspaceId).SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class ProjectViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<ProjectViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var projectId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "projectId");
        if (projectId is null) return;

        var workspaceId = await ProjectAuthorizationQueries.GetWorkspaceIdAsync(db, projectId.Value);
        if (workspaceId is null)
        {
            // Project doesn't exist — defer to the endpoint handler's own 404 rather than
            // failing closed here, which would incorrectly surface as 403.
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.IsWorkspaceAdminAsync(db, workspaceId.Value, userId))
        {
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.GetProjectRoleAsync(db, projectId.Value, userId) is not null)
        {
            context.Succeed(requirement);
        }
    }
}

public class ProjectManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<ProjectManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var projectId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "projectId");
        if (projectId is null) return;

        var workspaceId = await ProjectAuthorizationQueries.GetWorkspaceIdAsync(db, projectId.Value);
        if (workspaceId is null)
        {
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.IsWorkspaceAdminAsync(db, workspaceId.Value, userId))
        {
            context.Succeed(requirement);
            return;
        }

        var role = await ProjectAuthorizationQueries.GetProjectRoleAsync(db, projectId.Value, userId);
        if (role == ProjectRole.ProjectAdmin)
        {
            context.Succeed(requirement);
        }
    }
}

public class ProjectWorkspaceAdminAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<ProjectWorkspaceAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectWorkspaceAdminRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var projectId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "projectId");
        if (projectId is null) return;

        var workspaceId = await ProjectAuthorizationQueries.GetWorkspaceIdAsync(db, projectId.Value);
        if (workspaceId is null)
        {
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.IsWorkspaceAdminAsync(db, workspaceId.Value, userId))
        {
            context.Succeed(requirement);
        }
    }
}
```

- [x] **Step 4: Write GetProject, ListProjects, GetMyProjectRole**

```csharp
// src/Api/Features/Projects/GetProject.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md §9, §14 — ProjectMember (any role) or Workspace Admin.</summary>
public static class GetProject
{
    public record Response(Guid Id, Guid WorkspaceId, string Key, string Name, string? Description, bool IsArchived, DateTime CreatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var response = await db.Projects
                .Where(p => p.Id == projectId)
                .Select(p => new Response(p.Id, p.WorkspaceId, p.Key, p.Name, p.Description, p.IsArchived, p.CreatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);

            return response is null ? Results.NotFound() : Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Projects");
}
```

```csharp
// src/Api/Features/Projects/ListProjects.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md §9 — GET /api/workspaces/{workspaceId}/projects, any Workspace Member.</summary>
public static class ListProjects
{
    public record ProjectItem(Guid Id, string Key, string Name, string? Description, bool IsArchived);

    public record Response(IReadOnlyList<ProjectItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid workspaceId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.Projects
                .Where(p => p.WorkspaceId == workspaceId)
                .OrderBy(p => p.Name)
                .Select(p => new ProjectItem(p.Id, p.Key, p.Name, p.Description, p.IsArchived))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/projects", Handler.Handle)
            .RequireAuthorization("WorkspaceMember")
            .WithTags("Projects");
}
```

```csharp
// src/Api/Features/Projects/GetMyProjectRole.cs
using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/16-rbac.md FR-02, BR-02, §9-11 — GET /api/projects/{projectId}/my-role.</summary>
public static class GetMyProjectRole
{
    public record Response(Guid ProjectId, string? EffectiveRole, bool ViaWorkspaceAdmin);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            var workspaceId = await db.Projects
                .Where(p => p.Id == projectId)
                .Select(p => (Guid?)p.WorkspaceId)
                .SingleOrDefaultAsync(cancellationToken);
            if (workspaceId is null)
            {
                return Results.NotFound();
            }

            var isWorkspaceAdmin = await db.WorkspaceMembers.AnyAsync(
                m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin,
                cancellationToken);
            if (isWorkspaceAdmin)
            {
                return Results.Ok(new Response(projectId, WorkspaceRole.Admin, true));
            }

            var role = await db.ProjectMembers
                .Where(m => m.ProjectId == projectId && m.UserId == userId)
                .Select(m => (string?)m.Role)
                .SingleOrDefaultAsync(cancellationToken);

            return Results.Ok(new Response(projectId, role, false));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/my-role", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Projects");
}
```

- [x] **Step 5: Register handlers and policies in Program.cs**

Add `using JiraLite.Api.Features.Projects;` (already added in Task 4). After the existing `builder.Services.AddScoped<IAuthorizationHandler, TeamWorkspaceAdminAuthorizationHandler>();` line, add:

```csharp
builder.Services.AddScoped<IAuthorizationHandler, ProjectViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ProjectManageAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ProjectWorkspaceAdminAuthorizationHandler>();
```

After the existing `.AddPolicy("TeamWorkspaceAdmin", ...)` line (before the closing `;` of the `AddAuthorizationBuilder()` chain — add these as additional chained `.AddPolicy(...)` calls):

```csharp
    .AddPolicy("ProjectView", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProjectViewRequirement()))
    .AddPolicy("ProjectManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProjectManageRequirement()))
    .AddPolicy("ProjectWorkspaceAdmin", policy => policy.RequireAuthenticatedUser().AddRequirements(new ProjectWorkspaceAdminRequirement()));
```

(Move the trailing `;` from the old last `.AddPolicy("TeamWorkspaceAdmin", ...)` line to the new last line above.)

After `CreateProject.MapEndpoint(app);`, add:

```csharp
GetProject.MapEndpoint(app);
ListProjects.MapEndpoint(app);
GetMyProjectRole.MapEndpoint(app);
```

- [x] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter GetProjectTests`
Expected: PASS (3 tests).

- [x] **Step 7: Commit**

```bash
git add src/Api/Common/Auth/ProjectAuthorization.cs src/Api/Features/Projects src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Projects
git commit -m "feat: add Project-scoped authorization, GetProject, ListProjects, GetMyProjectRole"
```

---

### Task 6: EditProject, ArchiveProject, UnarchiveProject

**Files:**
- Create: `src/Api/Features/Projects/EditProject.cs`, `ArchiveProject.cs`, `UnarchiveProject.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Projects/EditArchiveProjectTests.cs`

**Interfaces:**
- Consumes: `"ProjectManage"` policy from Task 5.
- Produces: the archived-project write-lock pattern (`ProblemResults.Conflict("https://jiralite.dev/errors/project-archived", ...)`) reused by `CreateBoard` (Task 10) and `CreateSprint` (Task 14).

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Projects/EditArchiveProjectTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class EditArchiveProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public EditArchiveProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Project_admin_can_edit_name_and_description()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "Old", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync($"/api/projects/{projectId}", new { name = "New Name", description = "New description" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New Name", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Archiving_a_project_then_attempting_to_edit_it_is_still_allowed_but_creating_a_board_is_blocked()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var archiveResponse = await client.PostAsync($"/api/projects/{projectId}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);

        var boardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Support", type = "Kanban" });
        Assert.Equal(HttpStatusCode.Conflict, boardResponse.StatusCode);

        var unarchiveResponse = await client.PostAsync($"/api/projects/{projectId}/unarchive", null);
        Assert.Equal(HttpStatusCode.OK, unarchiveResponse.StatusCode);
    }
}
```

Add `using System.Text.Json;` at the top. Note: this test's second assertion (`boardResponse`) will not compile/pass until Task 10 maps `POST /api/projects/{projectId}/boards` — leave that assertion commented out until Task 10, or implement Tasks 6 and 10 together if running strict per-task TDD. For this task, keep only the first two assertions (edit + archive + unarchive) and add the board-blocked assertion back in as part of Task 10's test file instead.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter EditArchiveProjectTests`
Expected: FAIL — routes not mapped.

- [x] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Projects/EditProject.cs
using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-03 — Key is immutable, only Name/Description are editable.</summary>
public static class EditProject
{
    public record Request(string Name, string? Description);

    public record Response(Guid Id, string Key, string Name, string? Description, DateTime UpdatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).MaximumLength(1000);
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            project.Name = request.Name.Trim();
            project.Description = request.Description?.Trim();
            project.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(project.Id, project.Key, project.Name, project.Description, project.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/projects/{projectId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
```

```csharp
// src/Api/Features/Projects/ArchiveProject.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-05, BR-04.</summary>
public static class ArchiveProject
{
    public record Response(Guid Id, bool IsArchived, DateTime UpdatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            project.IsArchived = true;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(project.Id, project.IsArchived, project.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/projects/{projectId:guid}/archive", Handler.Handle)
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
```

```csharp
// src/Api/Features/Projects/UnarchiveProject.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-05, BR-04 — restores write access.</summary>
public static class UnarchiveProject
{
    public record Response(Guid Id, bool IsArchived, DateTime UpdatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            project.IsArchived = false;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(project.Id, project.IsArchived, project.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/projects/{projectId:guid}/unarchive", Handler.Handle)
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
```

- [x] **Step 4: Register endpoints in Program.cs**

Add `using JiraLite.Api.Features.Projects;` (already present). After `GetMyProjectRole.MapEndpoint(app);`, add:

```csharp
EditProject.MapEndpoint(app);
ArchiveProject.MapEndpoint(app);
UnarchiveProject.MapEndpoint(app);
```

- [x] **Step 5: Trim the test to what this task delivers, then run it**

Remove the `boardResponse`/`unarchiveResponse` block's board-creation assertion from the test written in Step 1 (it depends on Task 10); keep only:

```csharp
    [Fact]
    public async Task Archiving_then_unarchiving_a_project_round_trips_IsArchived()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var archiveResponse = await client.PostAsync($"/api/projects/{projectId}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        var archiveBody = await archiveResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(archiveBody.GetProperty("isArchived").GetBoolean());

        var unarchiveResponse = await client.PostAsync($"/api/projects/{projectId}/unarchive", null);
        Assert.Equal(HttpStatusCode.OK, unarchiveResponse.StatusCode);
        var unarchiveBody = await unarchiveResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(unarchiveBody.GetProperty("isArchived").GetBoolean());
    }
```

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter EditArchiveProjectTests`
Expected: PASS (2 tests).

- [x] **Step 6: Commit**

```bash
git add src/Api/Features/Projects/EditProject.cs src/Api/Features/Projects/ArchiveProject.cs src/Api/Features/Projects/UnarchiveProject.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Projects/EditArchiveProjectTests.cs
git commit -m "feat: add EditProject, ArchiveProject, UnarchiveProject"
```

---

### Task 7: DeleteProject (cascade + ActivityLogEntry detach)

**Files:**
- Create: `src/Api/Features/Projects/DeleteProject.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Projects/DeleteProjectTests.cs`

**Interfaces:**
- Consumes: `"ProjectWorkspaceAdmin"` policy from Task 5.
- Produces: nothing consumed by later tasks — this is a terminal operation.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Projects/DeleteProjectTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class DeleteProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public DeleteProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_a_non_archived_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_archived_project_cascades_and_detaches_activity_log_entries()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            db.ActivityLogEntries.Add(new ActivityLogEntry
            {
                Id = Guid.NewGuid(),
                ActorUserId = admin.UserId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                EntityType = "Project",
                EntityId = projectId,
                Action = "Created",
                Summary = "created Project JIRA",
                OccurredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await client.PostAsync($"/api/projects/{projectId}/archive", null);
        var response = await client.DeleteAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await verifyDb.Projects.AnyAsync(p => p.Id == projectId));
        Assert.False(await verifyDb.Boards.AnyAsync(b => b.ProjectId == projectId));
        var activityEntry = await verifyDb.ActivityLogEntries.SingleAsync(e => e.EntityId == projectId && e.EntityType == "Project");
        Assert.Null(activityEntry.ProjectId);
        Assert.Equal(workspaceId, activityEntry.WorkspaceId);
    }
}
```

Add `using System.Text.Json;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter DeleteProjectTests`
Expected: FAIL — route not mapped.

- [x] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Projects/DeleteProject.cs
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>
/// spec/05-projects.md BR-05, BR-06, BR-07 — archive-before-delete rail, Workspace-Admin-only,
/// cascades Boards/Columns/Sprints/ProjectMembers and detaches (nulls) ActivityLogEntry.ProjectId.
/// spec/18-database.md §9 — Project/Board/Sprint use NO ACTION FKs, so this orchestration is
/// application code, not a database cascade.
/// </summary>
public static class DeleteProject
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (!project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-not-archived",
                    "A Project must be archived before it can be permanently deleted.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var boardIds = await db.Boards.Where(b => b.ProjectId == projectId).Select(b => b.Id).ToListAsync(cancellationToken);
            await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync(cancellationToken);
            await db.Sprints.Where(s => s.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
            await db.Boards.Where(b => b.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
            await db.ProjectMembers.Where(m => m.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

            await db.ActivityLogEntries
                .Where(e => e.ProjectId == projectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.ProjectId, (Guid?)null), cancellationToken);

            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/projects/{projectId:guid}", Handler.Handle)
            .RequireAuthorization("ProjectWorkspaceAdmin")
            .WithTags("Projects");
}
```

Note: `ExecuteDeleteAsync`/`ExecuteUpdateAsync` (EF Core bulk operations) run each as its own statement against the database inside the explicit transaction — this avoids loading every child row into memory just to delete it, appropriate for a cascading delete that may touch many rows. Comments/Attachments/Labels/Issues are not yet in scope (Phase 4) so are not referenced here; this handler must be revisited in Phase 4 to also cascade those.

- [x] **Step 4: Register the endpoint in Program.cs**

After `UnarchiveProject.MapEndpoint(app);`, add:

```csharp
DeleteProject.MapEndpoint(app);
```

- [x] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter DeleteProjectTests`
Expected: PASS (2 tests).

- [x] **Step 6: Commit**

```bash
git add src/Api/Features/Projects/DeleteProject.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Projects/DeleteProjectTests.cs
git commit -m "feat: add DeleteProject with cascade and ActivityLogEntry detach"
```

---

### Task 8: Project member management

**Files:**
- Create: `src/Api/Features/Projects/ListProjectMembers.cs`, `AddProjectMember.cs`, `ChangeProjectMemberRole.cs`, `RemoveProjectMember.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Projects/ProjectMemberTests.cs`

**Interfaces:**
- Consumes: `"ProjectView"`, `"ProjectManage"` policies from Task 5.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Projects/ProjectMemberTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class ProjectMemberTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ProjectMemberTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Adding_a_workspace_member_as_a_project_member_then_changing_and_removing_their_role_works()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var teammate = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var inviteResponse = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/invitations", new { email = teammate.Email, role = "Member" });
        Assert.True(inviteResponse.IsSuccessStatusCode);
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = invitation.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", teammate.AccessToken);
        await client.PostAsJsonAsync("/api/workspaces/accept-invitation", new { token });

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var addResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/members", new { userId = teammate.UserId, role = "Developer" });
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var changeResponse = await client.PatchAsJsonAsync($"/api/projects/{projectId}/members/{teammate.UserId}", new { role = "ProjectAdmin" });
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        var removeResponse = await client.DeleteAsync($"/api/projects/{projectId}/members/{teammate.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/projects/{projectId}/members");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(listBody.GetProperty("items").EnumerateArray());
    }
}
```

Add `using System.Text.Json;` at the top. Note: this test exercises the real `/api/workspaces/{workspaceId}/invitations` + accept-invitation flow already built in Phase 2 — check `src/Api/Features/Workspaces/CreateInvitation.cs` and `AcceptInvitation.cs` for the exact request/response field names and adjust this test's request bodies/routes if they differ from what's assumed here (`email`/`role` on create, `token` on accept, and whether accept is a flat `/api/workspaces/accept-invitation` route or nested — confirm against `Program.cs`'s registered routes before finalizing this test).

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter ProjectMemberTests`
Expected: FAIL — Project member routes not mapped.

- [x] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Projects/ListProjectMembers.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md §9, §14.</summary>
public static class ListProjectMembers
{
    public record MemberItem(Guid UserId, string Role, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<MemberItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.ProjectMembers
                .Where(m => m.ProjectId == projectId)
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => new MemberItem(m.UserId, m.Role, m.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/members", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Projects");
}
```

```csharp
// src/Api/Features/Projects/AddProjectMember.cs
using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-04, BR-01 — only existing WorkspaceMembers may be added.</summary>
public static class AddProjectMember
{
    public record Request(Guid UserId, string Role);

    public record Response(Guid UserId, string Role, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.UserId).NotEmpty();
            RuleFor(x => x.Role).NotEmpty().Must(r => ProjectRole.All.Contains(r))
                .WithMessage($"Role must be one of: {string.Join(", ", ProjectRole.All)}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            var isWorkspaceMember = await db.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == project.WorkspaceId && m.UserId == request.UserId, cancellationToken);
            if (!isWorkspaceMember)
            {
                return Results.BadRequest(new { detail = "User must be a member of the owning Workspace before being added to a Project." });
            }

            if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == request.UserId, cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/already-project-member",
                    "This user is already a member of the Project.");
            }

            var now = DateTime.UtcNow;
            var member = new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = request.UserId,
                Role = request.Role,
                CreatedAtUtc = now
            };
            db.ProjectMembers.Add(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/projects/{projectId}/members/{member.UserId}",
                new Response(member.UserId, member.Role, member.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/projects/{projectId:guid}/members", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
```

```csharp
// src/Api/Features/Projects/ChangeProjectMemberRole.cs
using FluentValidation;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-04. No "last ProjectAdmin" guard — spec/05-projects.md BR-02: Workspace Admin is always a fallback authority.</summary>
public static class ChangeProjectMemberRole
{
    public record Request(string Role);

    public record Response(Guid UserId, string Role);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Role).NotEmpty().Must(r => ProjectRole.All.Contains(r))
                .WithMessage($"Role must be one of: {string.Join(", ", ProjectRole.All)}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Guid userId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var member = await db.ProjectMembers
                .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            member.Role = request.Role;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(member.UserId, member.Role));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/projects/{projectId:guid}/members/{userId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
```

```csharp
// src/Api/Features/Projects/RemoveProjectMember.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-04, BR-02 — no "last ProjectAdmin" guard, Workspace Admin is always a fallback.</summary>
public static class RemoveProjectMember
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Guid userId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var member = await db.ProjectMembers
                .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            db.ProjectMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/projects/{projectId:guid}/members/{userId:guid}", Handler.Handle)
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
```

- [x] **Step 4: Register endpoints in Program.cs**

After `DeleteProject.MapEndpoint(app);`, add:

```csharp
ListProjectMembers.MapEndpoint(app);
AddProjectMember.MapEndpoint(app);
ChangeProjectMemberRole.MapEndpoint(app);
RemoveProjectMember.MapEndpoint(app);
```

- [x] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter ProjectMemberTests`
Expected: PASS. If the invitation flow's actual request/response shape differs from what Step 1 assumed, fix the test to match `CreateInvitation.cs`/`AcceptInvitation.cs` rather than changing those files.

- [x] **Step 6: Commit**

```bash
git add src/Api/Features/Projects/ListProjectMembers.cs src/Api/Features/Projects/AddProjectMember.cs src/Api/Features/Projects/ChangeProjectMemberRole.cs src/Api/Features/Projects/RemoveProjectMember.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Projects/ProjectMemberTests.cs
git commit -m "feat: add Project member management endpoints"
```

---

### Task 9: Retrofit — cascade ProjectMember removal on Workspace membership loss

**Files:**
- Modify: `src/Api/Features/Workspaces/RemoveMember.cs`, `src/Api/Features/Workspaces/LeaveWorkspace.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Workspaces/RemoveMemberCascadeTests.cs`

**Interfaces:**
- Consumes: `ProjectMember` from Task 2.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Workspaces/RemoveMemberCascadeTests.cs
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Workspaces;

public class RemoveMemberCascadeTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public RemoveMemberCascadeTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Removing_a_workspace_member_also_removes_their_project_memberships_in_that_workspace()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var teammate = await TestDataHelper.RegisterAndLoginAsync(client);
        Guid teammateProjectMemberId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            db.WorkspaceMembers.Add(new() { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = teammate.UserId, Role = "Member", CreatedAtUtc = DateTime.UtcNow });
            var member = new JiraLite.Api.Common.Domain.ProjectMember { Id = Guid.NewGuid(), ProjectId = projectId, UserId = teammate.UserId, Role = "Developer", CreatedAtUtc = DateTime.UtcNow };
            db.ProjectMembers.Add(member);
            await db.SaveChangesAsync();
            teammateProjectMemberId = member.Id;
        }

        await client.DeleteAsync($"/api/workspaces/{workspaceId}/members/{teammate.UserId}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await verifyDb.ProjectMembers.AnyAsync(m => m.Id == teammateProjectMemberId));
    }
}
```

Add `using System.Text.Json;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter RemoveMemberCascadeTests`
Expected: FAIL — `ProjectMember` row is still present after removal (current `RemoveMember.cs` doesn't touch `ProjectMember`).

- [x] **Step 3: Retrofit RemoveMember.cs**

In `src/Api/Features/Workspaces/RemoveMember.cs`, after the existing Team-membership cleanup block (`db.TeamMembers.RemoveRange(teamMemberships);`) and before `db.WorkspaceMembers.Remove(member);`, add:

```csharp
            // spec/03-workspaces.md BR-08: removing a WorkspaceMember cascades to their ProjectMember
            // records within this Workspace's Projects — a user cannot retain project-level access
            // after losing workspace membership. No-op until now (Project/ProjectMember didn't exist
            // before Phase 3 — see the note this comment replaces).
            var projectMemberships = await db.ProjectMembers
                .Where(pm => pm.UserId == userId && db.Projects.Any(p => p.Id == pm.ProjectId && p.WorkspaceId == workspaceId))
                .ToListAsync(cancellationToken);
            db.ProjectMembers.RemoveRange(projectMemberships);
```

Also update the file's leading XML doc comment, replacing the line `/// ProjectMember cascade (BR-08) is a no-op until Phase 3, where Project/ProjectMember exist.` with:

```csharp
/// Cascades to ProjectMember (BR-08) now that Project/ProjectMember exist (Phase 3).
```

- [x] **Step 4: Retrofit LeaveWorkspace.cs identically**

In `src/Api/Features/Workspaces/LeaveWorkspace.cs`, after its existing `db.TeamMembers.RemoveRange(teamMemberships);` line and before `db.WorkspaceMembers.Remove(member);`, add the identical block from Step 3 (same variable names — `userId`, `workspaceId`, and `cancellationToken` are already in scope in this handler).

- [x] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter RemoveMemberCascadeTests`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/Api/Features/Workspaces/RemoveMember.cs src/Api/Features/Workspaces/LeaveWorkspace.cs tests/JiraLite.Api.IntegrationTests/Workspaces/RemoveMemberCascadeTests.cs
git commit -m "fix: cascade ProjectMember removal on Workspace membership loss (spec/03-workspaces.md BR-08)"
```

---

### Task 10: Board-scoped authorization + ListBoards, GetBoard, CreateBoard

**Files:**
- Create: `src/Api/Common/Auth/BoardAuthorization.cs`
- Create: `src/Api/Features/Boards/ListBoards.cs`, `GetBoard.cs`, `CreateBoard.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Boards/BoardTests.cs`

**Interfaces:**
- Produces: policies `"BoardView"`, `"BoardManage"`, `"BoardContribute"` — consumed by Tasks 11-14.
- Consumes: `"ProjectView"`/`"ProjectManage"` (existing routes stay project-scoped for list/create).

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Boards/BoardTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class BoardTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public BoardTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string Token, Guid ProjectId)> SeedProjectAsync(HttpClient client)
    {
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        return (admin.AccessToken, created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Listing_boards_after_project_creation_returns_the_default_board()
    {
        var client = _factory.CreateClient();
        var (token, projectId) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync($"/api/projects/{projectId}/boards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Project_admin_creates_a_second_board_and_can_get_it_with_its_columns()
    {
        var client = _factory.CreateClient();
        var (token, projectId) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Support", type = "Kanban" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = created.GetProperty("id").GetGuid();

        var getResponse = await client.GetAsync($"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var board = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Support", board.GetProperty("name").GetString());
        Assert.Equal(3, board.GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task Creating_a_board_in_an_archived_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var (token, projectId) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await client.PostAsync($"/api/projects/{projectId}/archive", null);

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Support", type = "Kanban" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
```

Add `using System.Text.Json;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter BoardTests`
Expected: FAIL — routes not mapped.

- [x] **Step 3: Write the Board-scoped authorization policies**

```csharp
// src/Api/Common/Auth/BoardAuthorization.cs
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Board's Project (resolved via "boardId"), or Workspace Admin.</summary>
public class BoardViewRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on the Board's Project, or Workspace Admin. spec/06-boards.md §14.</summary>
public class BoardManageRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role in (Developer, ProjectAdmin) on the Board's Project, or Workspace Admin. spec/08-sprints.md §14 (Create Sprint is boardId-routed).</summary>
public class BoardContributeRequirement : IAuthorizationRequirement;

file static class BoardAuthorizationQueries
{
    public static async Task<(Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid boardId) =>
        await db.Boards
            .Where(b => b.Id == boardId)
            .Join(db.Projects, b => b.ProjectId, p => p.Id, (b, p) => new { p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid>(x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class BoardViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<BoardViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BoardViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var boardId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "boardId");
        if (boardId is null) return;

        var resolved = await BoardAuthorizationQueries.ResolveAsync(db, boardId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await BoardAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await BoardAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) is not null) context.Succeed(requirement);
    }
}

public class BoardManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<BoardManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BoardManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var boardId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "boardId");
        if (boardId is null) return;

        var resolved = await BoardAuthorizationQueries.ResolveAsync(db, boardId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await BoardAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await BoardAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) == ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}

public class BoardContributeAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<BoardContributeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BoardContributeRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var boardId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "boardId");
        if (boardId is null) return;

        var resolved = await BoardAuthorizationQueries.ResolveAsync(db, boardId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await BoardAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        var role = await BoardAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role is ProjectRole.Developer or ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}
```

- [x] **Step 4: Write ListBoards, GetBoard, CreateBoard**

```csharp
// src/Api/Features/Boards/ListBoards.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md §9 — GET /api/projects/{projectId}/boards.</summary>
public static class ListBoards
{
    public record BoardItem(Guid Id, string Name, string Type, int DisplayOrder);

    public record Response(IReadOnlyList<BoardItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.Boards
                .Where(b => b.ProjectId == projectId)
                .OrderBy(b => b.DisplayOrder)
                .Select(b => new BoardItem(b.Id, b.Name, b.Type, b.DisplayOrder))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/boards", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Boards");
}
```

```csharp
// src/Api/Features/Boards/GetBoard.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md §9, §11 — GET /api/boards/{boardId}, includes columns ordered by DisplayOrder.</summary>
public static class GetBoard
{
    public record ColumnItem(Guid Id, string Name, int DisplayOrder, bool IsDefault, bool IsDoneColumn, string RowVersion);

    public record Response(Guid Id, Guid ProjectId, string Name, string Type, IReadOnlyList<ColumnItem> Columns);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var board = await db.Boards.SingleOrDefaultAsync(b => b.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            var columns = await db.BoardColumns
                .Where(c => c.BoardId == boardId)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync(cancellationToken);

            var response = new Response(
                board.Id,
                board.ProjectId,
                board.Name,
                board.Type,
                columns.Select(c => new ColumnItem(c.Id, c.Name, c.DisplayOrder, c.IsDefault, c.IsDoneColumn, Convert.ToBase64String(c.RowVersion))).ToList());

            return Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/boards/{boardId:guid}", Handler.Handle)
            .RequireAuthorization("BoardView")
            .WithTags("Boards");
}
```

```csharp
// src/Api/Features/Boards/CreateBoard.cs
using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md FR-02, spec/05-projects.md BR-04 (archived-project write-lock).</summary>
public static class CreateBoard
{
    public record Request(string Name, string Type);

    public record Response(Guid Id, Guid ProjectId, string Name, string Type, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Type).NotEmpty().Must(t => BoardType.All.Contains(t))
                .WithMessage($"Type must be one of: {string.Join(", ", BoardType.All)}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot create a Board in an archived Project.");
            }

            var nextDisplayOrder = await db.Boards
                .Where(b => b.ProjectId == projectId)
                .Select(b => (int?)b.DisplayOrder)
                .MaxAsync(cancellationToken) ?? -1;

            var now = DateTime.UtcNow;
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = request.Name.Trim(),
                Type = request.Type,
                DisplayOrder = nextDisplayOrder + 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Boards.Add(board);

            if (request.Type == BoardType.Kanban)
            {
                db.BoardColumns.AddRange(
                    new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "To Do", DisplayOrder = 0, IsDefault = true, IsDoneColumn = false },
                    new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "In Progress", DisplayOrder = 1, IsDefault = false, IsDoneColumn = false },
                    new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Done", DisplayOrder = 2, IsDefault = false, IsDoneColumn = true });
            }
            else
            {
                // Scrum boards still need BR-02's invariants satisfied (exactly one Default, at
                // least one Done column) — spec/06-boards.md doesn't specify Scrum column names,
                // so a minimal two-column starter set is used, editable via AddColumn/EditColumn.
                db.BoardColumns.AddRange(
                    new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "To Do", DisplayOrder = 0, IsDefault = true, IsDoneColumn = false },
                    new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Done", DisplayOrder = 1, IsDefault = false, IsDoneColumn = true });
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/boards/{board.Id}",
                new Response(board.Id, board.ProjectId, board.Name, board.Type, board.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/projects/{projectId:guid}/boards", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectManage")
            .WithTags("Boards");
}
```

- [x] **Step 5: Register handlers, policies, and endpoints in Program.cs**

Add `using JiraLite.Api.Features.Boards;` to the top. After the `ProjectWorkspaceAdminAuthorizationHandler` registration line, add:

```csharp
builder.Services.AddScoped<IAuthorizationHandler, BoardViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BoardManageAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BoardContributeAuthorizationHandler>();
```

After the `"ProjectWorkspaceAdmin"` policy line, add (moving the trailing `;` to the new last line):

```csharp
    .AddPolicy("BoardView", policy => policy.RequireAuthenticatedUser().AddRequirements(new BoardViewRequirement()))
    .AddPolicy("BoardManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new BoardManageRequirement()))
    .AddPolicy("BoardContribute", policy => policy.RequireAuthenticatedUser().AddRequirements(new BoardContributeRequirement()));
```

After `RemoveProjectMember.MapEndpoint(app);`, add:

```csharp
ListBoards.MapEndpoint(app);
GetBoard.MapEndpoint(app);
CreateBoard.MapEndpoint(app);
```

- [x] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter BoardTests`
Expected: PASS (3 tests). This also retroactively satisfies the board-creation assertion dropped from Task 6's test — optionally add it back to `EditArchiveProjectTests.cs` now, though `BoardTests.Creating_a_board_in_an_archived_project_is_rejected` already covers it.

- [x] **Step 7: Commit**

```bash
git add src/Api/Common/Auth/BoardAuthorization.cs src/Api/Features/Boards src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Boards/BoardTests.cs
git commit -m "feat: add Board-scoped authorization, ListBoards, GetBoard, CreateBoard"
```

---

### Task 11: RenameBoard + DeleteBoard

**Files:**
- Create: `src/Api/Features/Boards/RenameBoard.cs`, `DeleteBoard.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Boards/DeleteBoardTests.cs`

**Interfaces:**
- Consumes: `"BoardManage"` policy from Task 10.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Boards/DeleteBoardTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class DeleteBoardTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public DeleteBoardTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_the_only_board_in_a_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var boardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = boards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/boards/{boardId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_a_board_updates_its_name()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var boardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = boards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}", new { name = "Renamed Board" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed Board", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_board_with_a_completed_sprint_referencing_it_cannot_be_deleted_even_with_zero_issues()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var scrumBoardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Scrum", type = "Scrum" });
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var scrumBoardId = scrumBoard.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            db.Sprints.Add(new Sprint
            {
                Id = Guid.NewGuid(),
                BoardId = scrumBoardId,
                ProjectId = projectId,
                Name = "Sprint 1",
                Status = SprintStatus.Completed,
                PlannedStartDateUtc = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)),
                PlannedEndDateUtc = DateOnly.FromDateTime(DateTime.UtcNow),
                StartedAtUtc = DateTime.UtcNow.AddDays(-14),
                CompletedAtUtc = DateTime.UtcNow,
                CreatedByUserId = admin.UserId,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-14)
            });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/boards/{scrumBoardId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
```

Add `using System.Text.Json;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter DeleteBoardTests`
Expected: FAIL — routes not mapped.

- [x] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Boards/RenameBoard.cs
using FluentValidation;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md §9, §12 — Type is immutable, only Name is editable.</summary>
public static class RenameBoard
{
    public record Request(string Name);

    public record Response(Guid Id, string Name, DateTime UpdatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var board = await db.Boards.SingleOrDefaultAsync(b => b.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            board.Name = request.Name.Trim();
            board.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(board.Id, board.Name, board.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/boards/{boardId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
```

```csharp
// src/Api/Features/Boards/DeleteBoard.cs
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md BR-04 (last Board in a Project), BR-09 (any Sprint, including Completed,
/// blocks delete). BR-05 (Issue-presence guard) is deferred to Phase 4 — no Issue entity exists
/// yet to check against; this guard must be added when Issue is introduced.
/// </summary>
public static class DeleteBoard
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var board = await db.Boards.SingleOrDefaultAsync(b => b.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            var otherBoardExists = await db.Boards.AnyAsync(b => b.ProjectId == board.ProjectId && b.Id != boardId, cancellationToken);
            if (!otherBoardExists)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/last-board",
                    "A Project must retain at least one Board.");
            }

            if (await db.Sprints.AnyAsync(s => s.BoardId == boardId, cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/board-has-sprints",
                    "This Board cannot be deleted while any Sprint (including Completed ones) references it.");
            }

            await db.BoardColumns.Where(c => c.BoardId == boardId).ExecuteDeleteAsync(cancellationToken);
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/boards/{boardId:guid}", Handler.Handle)
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
```

- [x] **Step 4: Register endpoints in Program.cs**

After `CreateBoard.MapEndpoint(app);`, add:

```csharp
RenameBoard.MapEndpoint(app);
DeleteBoard.MapEndpoint(app);
```

- [x] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter DeleteBoardTests`
Expected: PASS (3 tests).

- [x] **Step 6: Commit**

```bash
git add src/Api/Features/Boards/RenameBoard.cs src/Api/Features/Boards/DeleteBoard.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Boards/DeleteBoardTests.cs
git commit -m "feat: add RenameBoard and DeleteBoard with last-board and Sprint-reference guards"
```

---

### Task 12: AddColumn, EditColumn, DeleteColumn

**Files:**
- Create: `src/Api/Features/Boards/AddColumn.cs`, `EditColumn.cs`, `DeleteColumn.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Boards/ColumnTests.cs`

**Interfaces:**
- Consumes: `"BoardManage"` policy from Task 10.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Boards/ColumnTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class ColumnTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ColumnTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid BoardId)> SeedBoardAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var boardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = boards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        return (client, boardId);
    }

    [Fact]
    public async Task Adding_a_column_appends_it_after_the_existing_three()
    {
        var (client, boardId) = await SeedBoardAsync();

        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/columns", new { name = "Code Review", isDefault = false, isDoneColumn = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var getResponse = await client.GetAsync($"/api/boards/{boardId}");
        var board = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, board.GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task Deleting_the_last_remaining_column_is_rejected()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var toDoColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "To Do").Select(c => c.Id).SingleAsync();
        var inProgressColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "In Progress").Select(c => c.Id).SingleAsync();
        var doneColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "Done").Select(c => c.Id).SingleAsync();

        // Give "To Do" both flags first, so deleting "In Progress" and "Done" below doesn't
        // trip the BR-02 sole-Default/sole-Done guards — this test isolates BR-01 only.
        await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/{toDoColumnId}", new { isDoneColumn = true });

        var deleteInProgress = await client.DeleteAsync($"/api/boards/{boardId}/columns/{inProgressColumnId}");
        Assert.Equal(HttpStatusCode.OK, deleteInProgress.StatusCode);
        var deleteDone = await client.DeleteAsync($"/api/boards/{boardId}/columns/{doneColumnId}");
        Assert.Equal(HttpStatusCode.OK, deleteDone.StatusCode);

        var lastResponse = await client.DeleteAsync($"/api/boards/{boardId}/columns/{toDoColumnId}");

        Assert.Equal(HttpStatusCode.Conflict, lastResponse.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_sole_default_column_without_another_default_is_rejected()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var toDoColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "To Do").Select(c => c.Id).SingleAsync();

        var response = await client.DeleteAsync($"/api/boards/{boardId}/columns/{toDoColumnId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_sole_done_column_without_another_done_is_rejected()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var doneColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "Done").Select(c => c.Id).SingleAsync();

        var response = await client.DeleteAsync($"/api/boards/{boardId}/columns/{doneColumnId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Setting_a_new_default_column_unsets_the_previous_one()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var inProgressColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "In Progress").Select(c => c.Id).SingleAsync();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/{inProgressColumnId}", new { isDefault = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var toDoColumn = await db.BoardColumns.AsNoTracking().SingleAsync(c => c.BoardId == boardId && c.Name == "To Do");
        Assert.False(toDoColumn.IsDefault);
    }
}
```

Add `using System.Text.Json;` and `using System.Linq;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter ColumnTests`
Expected: FAIL — routes not mapped.

- [x] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Boards/AddColumn.cs
using FluentValidation;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md FR-03, BR-02 — a new IsDefault column steals the flag from the previous holder.</summary>
public static class AddColumn
{
    public record Request(string Name, bool IsDefault, bool IsDoneColumn);

    public record Response(Guid Id, string Name, int DisplayOrder, bool IsDefault, bool IsDoneColumn);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            if (!await db.Boards.AnyAsync(b => b.Id == boardId, cancellationToken))
            {
                return Results.NotFound();
            }

            if (request.IsDefault)
            {
                await db.BoardColumns.Where(c => c.BoardId == boardId && c.IsDefault)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.IsDefault, false), cancellationToken);
            }

            var nextDisplayOrder = await db.BoardColumns
                .Where(c => c.BoardId == boardId)
                .Select(c => (int?)c.DisplayOrder)
                .MaxAsync(cancellationToken) ?? -1;

            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                Name = request.Name.Trim(),
                DisplayOrder = nextDisplayOrder + 1,
                IsDefault = request.IsDefault,
                IsDoneColumn = request.IsDoneColumn
            };
            db.BoardColumns.Add(column);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/boards/{boardId}/columns/{column.Id}",
                new Response(column.Id, column.Name, column.DisplayOrder, column.IsDefault, column.IsDoneColumn));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/boards/{boardId:guid}/columns", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
```

```csharp
// src/Api/Features/Boards/EditColumn.cs
using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md FR-03, FR-04, BR-02. Partial update: only fields present are changed.
/// Setting IsDefault=true steals the flag from the board's previous default column.
/// Setting IsDefault=false or IsDoneColumn=false on the sole holder of that flag is rejected.
/// </summary>
public static class EditColumn
{
    public record Request(string? Name, bool? IsDefault, bool? IsDoneColumn);

    public record Response(Guid Id, string Name, bool IsDefault, bool IsDoneColumn);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Name).MaximumLength(100).When(x => x.Name is not null);
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid boardId,
            Guid columnId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var column = await db.BoardColumns.SingleOrDefaultAsync(c => c.Id == columnId && c.BoardId == boardId, cancellationToken);
            if (column is null)
            {
                return Results.NotFound();
            }

            if (request.IsDefault == false && column.IsDefault)
            {
                var anotherDefaultExists = await db.BoardColumns.AnyAsync(c => c.BoardId == boardId && c.Id != columnId && c.IsDefault, cancellationToken);
                if (!anotherDefaultExists)
                {
                    return Results.BadRequest(new { detail = "Cannot unset the only default column without setting another." });
                }
            }

            if (request.IsDoneColumn == false && column.IsDoneColumn)
            {
                var anotherDoneExists = await db.BoardColumns.AnyAsync(c => c.BoardId == boardId && c.Id != columnId && c.IsDoneColumn, cancellationToken);
                if (!anotherDoneExists)
                {
                    return Results.BadRequest(new { detail = "Cannot unset the only Done column without setting another." });
                }
            }

            if (request.IsDefault == true)
            {
                await db.BoardColumns.Where(c => c.BoardId == boardId && c.Id != columnId && c.IsDefault)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.IsDefault, false), cancellationToken);
            }

            if (request.Name is not null)
            {
                column.Name = request.Name.Trim();
            }
            if (request.IsDefault is not null)
            {
                column.IsDefault = request.IsDefault.Value;
            }
            if (request.IsDoneColumn is not null)
            {
                column.IsDoneColumn = request.IsDoneColumn.Value;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(column.Id, column.Name, column.IsDefault, column.IsDoneColumn));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/boards/{boardId:guid}/columns/{columnId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
```

```csharp
// src/Api/Features/Boards/DeleteColumn.cs
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md BR-01 (last column on a Board) and BR-02 (deleting the sole Default or sole
/// Done column would leave the Board without one, same invariant EditColumn enforces on unset).
/// BR-03 (Issue-presence guard) is deferred to Phase 4 — no Issue entity exists yet to check against.
/// </summary>
public static class DeleteColumn
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, Guid columnId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var column = await db.BoardColumns.SingleOrDefaultAsync(c => c.Id == columnId && c.BoardId == boardId, cancellationToken);
            if (column is null)
            {
                return Results.NotFound();
            }

            var otherColumnExists = await db.BoardColumns.AnyAsync(c => c.BoardId == boardId && c.Id != columnId, cancellationToken);
            if (!otherColumnExists)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/last-column",
                    "A Board must retain at least one Column.");
            }

            if (column.IsDefault)
            {
                var anotherDefaultExists = await db.BoardColumns.AnyAsync(c => c.BoardId == boardId && c.Id != columnId && c.IsDefault, cancellationToken);
                if (!anotherDefaultExists)
                {
                    return Results.BadRequest(new { detail = "Cannot delete the only default column without another column already marked default." });
                }
            }

            if (column.IsDoneColumn)
            {
                var anotherDoneExists = await db.BoardColumns.AnyAsync(c => c.BoardId == boardId && c.Id != columnId && c.IsDoneColumn, cancellationToken);
                if (!anotherDoneExists)
                {
                    return Results.BadRequest(new { detail = "Cannot delete the only Done column without another column already marked Done." });
                }
            }

            db.BoardColumns.Remove(column);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { deleted = true });
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/boards/{boardId:guid}/columns/{columnId:guid}", Handler.Handle)
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
```

- [x] **Step 4: Register endpoints in Program.cs**

After `DeleteBoard.MapEndpoint(app);`, add:

```csharp
AddColumn.MapEndpoint(app);
EditColumn.MapEndpoint(app);
DeleteColumn.MapEndpoint(app);
```

- [x] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter ColumnTests`
Expected: PASS (5 tests).

- [x] **Step 6: Commit**

```bash
git add src/Api/Features/Boards/AddColumn.cs src/Api/Features/Boards/EditColumn.cs src/Api/Features/Boards/DeleteColumn.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Boards/ColumnTests.cs
git commit -m "feat: add AddColumn, EditColumn, DeleteColumn with BR-01/BR-02 guards"
```

---

### Task 13: ReorderColumns (RowVersion concurrency)

**Files:**
- Create: `src/Api/Features/Boards/ReorderColumns.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Boards/ReorderColumnsTests.cs`

**Interfaces:**
- Consumes: `"BoardManage"` policy from Task 10.

**Design note:** `spec/06-boards.md`'s illustrative reorder request (`{ orderedColumnIds: [...] }`) omits a concurrency token, but `spec/19-api-guidelines.md` §11 requires one for this exact endpoint, and §1 states this document's conventions win over a conflicting illustrative example. Because a bulk reorder touches every column on the Board — each with its own independent `RowVersion` — the request is shaped as a list of `{ columnId, rowVersion }` pairs (order given by list position) rather than a flat id array, so each row's concurrency token can be checked individually.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Boards/ReorderColumnsTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class ReorderColumnsTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ReorderColumnsTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid BoardId, JiraLite.Api.Common.Domain.BoardColumn[] Columns)> SeedBoardAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var boardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = boards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var columns = await db.BoardColumns.Where(c => c.BoardId == boardId).OrderBy(c => c.DisplayOrder).ToArrayAsync();
        return (client, boardId, columns);
    }

    [Fact]
    public async Task Valid_reorder_updates_display_order_for_every_column()
    {
        var (client, boardId, columns) = await SeedBoardAsync();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/reorder", new
        {
            columns = new[]
            {
                new { columnId = columns[2].Id, rowVersion = Convert.ToBase64String(columns[2].RowVersion) },
                new { columnId = columns[0].Id, rowVersion = Convert.ToBase64String(columns[0].RowVersion) },
                new { columnId = columns[1].Id, rowVersion = Convert.ToBase64String(columns[1].RowVersion) }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var reordered = await db.BoardColumns.AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.DisplayOrder).ToListAsync();
        Assert.Equal(columns[2].Id, reordered[0].Id);
        Assert.Equal(columns[0].Id, reordered[1].Id);
        Assert.Equal(columns[1].Id, reordered[2].Id);
    }

    [Fact]
    public async Task Stale_row_version_is_rejected_with_409()
    {
        var (client, boardId, columns) = await SeedBoardAsync();

        // Change one column first so its RowVersion in the request below is now stale.
        await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/{columns[0].Id}", new { name = "Renamed" });

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/reorder", new
        {
            columns = new[]
            {
                new { columnId = columns[0].Id, rowVersion = Convert.ToBase64String(columns[0].RowVersion) },
                new { columnId = columns[1].Id, rowVersion = Convert.ToBase64String(columns[1].RowVersion) },
                new { columnId = columns[2].Id, rowVersion = Convert.ToBase64String(columns[2].RowVersion) }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Payload_missing_a_column_is_rejected_with_400()
    {
        var (client, boardId, columns) = await SeedBoardAsync();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/reorder", new
        {
            columns = new[]
            {
                new { columnId = columns[0].Id, rowVersion = Convert.ToBase64String(columns[0].RowVersion) },
                new { columnId = columns[1].Id, rowVersion = Convert.ToBase64String(columns[1].RowVersion) }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

Add `using System.Text.Json;` and `using System.Linq;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter ReorderColumnsTests`
Expected: FAIL — route not mapped.

- [x] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Boards/ReorderColumns.cs
using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md FR-03, NFR-01; spec/19-api-guidelines.md §11 (RowVersion concurrency wins
/// over the plain-id-array example in spec/06-boards.md — see this file's Task doc note).
/// </summary>
public static class ReorderColumns
{
    public record ColumnOrderEntry(Guid ColumnId, string RowVersion);

    public record Request(IReadOnlyList<ColumnOrderEntry> Columns);

    public record ResponseColumn(Guid ColumnId, int DisplayOrder, string RowVersion);

    public record Response(IReadOnlyList<ResponseColumn> Columns);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Columns).NotEmpty();
            RuleForEach(x => x.Columns).ChildRules(entry =>
            {
                entry.RuleFor(e => e.ColumnId).NotEmpty();
                entry.RuleFor(e => e.RowVersion).NotEmpty();
            });
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var currentColumns = await db.BoardColumns.Where(c => c.BoardId == boardId).ToListAsync(cancellationToken);
            if (currentColumns.Count == 0)
            {
                return Results.NotFound();
            }

            var currentIds = currentColumns.Select(c => c.Id).ToHashSet();
            var requestedIds = request.Columns.Select(e => e.ColumnId).ToHashSet();
            if (!currentIds.SetEquals(requestedIds))
            {
                return Results.BadRequest(new { detail = "The reorder payload must contain exactly the Board's current set of columns." });
            }

            for (var i = 0; i < request.Columns.Count; i++)
            {
                var entry = request.Columns[i];
                var column = currentColumns.Single(c => c.Id == entry.ColumnId);
                db.Entry(column).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(entry.RowVersion);
                column.DisplayOrder = i;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/concurrency-conflict",
                    "One or more columns were modified since you last loaded them. Reload and try again.");
            }

            var response = new Response(
                currentColumns
                    .OrderBy(c => c.DisplayOrder)
                    .Select(c => new ResponseColumn(c.Id, c.DisplayOrder, Convert.ToBase64String(c.RowVersion)))
                    .ToList());

            return Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/boards/{boardId:guid}/columns/reorder", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
```

- [x] **Step 4: Register the endpoint in Program.cs**

After `DeleteColumn.MapEndpoint(app);`, add:

```csharp
ReorderColumns.MapEndpoint(app);
```

- [x] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter ReorderColumnsTests`
Expected: PASS (3 tests).

- [x] **Step 6: Commit**

```bash
git add src/Api/Features/Boards/ReorderColumns.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Boards/ReorderColumnsTests.cs
git commit -m "feat: add ReorderColumns with per-column RowVersion concurrency"
```

---

### Task 14: Sprint-scoped authorization + CreateSprint, ListSprints, GetSprint

**Files:**
- Create: `src/Api/Common/Auth/SprintAuthorization.cs`
- Create: `src/Api/Features/Sprints/CreateSprint.cs`, `ListSprints.cs`, `GetSprint.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Sprints/SprintLifecycleTests.cs`

**Interfaces:**
- Produces: policies `"SprintView"`, `"SprintContribute"`, `"SprintManage"` — consumed by Tasks 15-16.
- Consumes: `"BoardContribute"`/`"BoardView"` (Task 10) for the boardId-routed list/create endpoints.

- [x] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Sprints/SprintLifecycleTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

public class SprintLifecycleTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public SprintLifecycleTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid ScrumBoardId, Guid KanbanBoardId)> SeedProjectAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var kanbanBoardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var kanbanBoards = await kanbanBoardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var kanbanBoardId = kanbanBoards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var scrumBoardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Scrum", type = "Scrum" });
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (client, scrumBoard.GetProperty("id").GetGuid(), kanbanBoardId);
    }

    [Fact]
    public async Task Creating_a_sprint_on_a_scrum_board_succeeds_and_it_is_listed_and_gettable()
    {
        var (client, scrumBoardId, _) = await SeedProjectAsync();

        var createResponse = await client.PostAsJsonAsync($"/api/boards/{scrumBoardId}/sprints", new
        {
            name = "Sprint 1",
            goal = "Ship the thing",
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var sprint = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sprintId = sprint.GetProperty("id").GetGuid();
        Assert.Equal("Planned", sprint.GetProperty("status").GetString());

        var listResponse = await client.GetAsync($"/api/boards/{scrumBoardId}/sprints");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(list.GetProperty("items").EnumerateArray());

        var getResponse = await client.GetAsync($"/api/sprints/{sprintId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Creating_a_sprint_on_a_kanban_board_is_rejected()
    {
        var (client, _, kanbanBoardId) = await SeedProjectAsync();

        var response = await client.PostAsJsonAsync($"/api/boards/{kanbanBoardId}/sprints", new
        {
            name = "Sprint 1",
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task End_date_not_after_start_date_is_rejected()
    {
        var (client, scrumBoardId, _) = await SeedProjectAsync();

        var response = await client.PostAsJsonAsync($"/api/boards/{scrumBoardId}/sprints", new
        {
            name = "Sprint 1",
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-14",
            plannedEndDateUtc = "2026-08-03"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

Add `using System.Text.Json;` at the top.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter SprintLifecycleTests`
Expected: FAIL — routes not mapped.

- [x] **Step 3: Write the Sprint-scoped authorization policies**

```csharp
// src/Api/Common/Auth/SprintAuthorization.cs
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Sprint's Project (resolved via "sprintId"), or Workspace Admin.</summary>
public class SprintViewRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role in (Developer, ProjectAdmin) on the Sprint's Project, or Workspace Admin. spec/08-sprints.md §14.</summary>
public class SprintContributeRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on the Sprint's Project, or Workspace Admin. spec/08-sprints.md §14 (Delete Sprint).</summary>
public class SprintManageRequirement : IAuthorizationRequirement;

file static class SprintAuthorizationQueries
{
    public static async Task<(Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid sprintId) =>
        await db.Sprints
            .Where(s => s.Id == sprintId)
            .Join(db.Projects, s => s.ProjectId, p => p.Id, (s, p) => new { p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid>(x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class SprintViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<SprintViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SprintViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var sprintId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "sprintId");
        if (sprintId is null) return;

        var resolved = await SprintAuthorizationQueries.ResolveAsync(db, sprintId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await SprintAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await SprintAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) is not null) context.Succeed(requirement);
    }
}

public class SprintContributeAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<SprintContributeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SprintContributeRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var sprintId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "sprintId");
        if (sprintId is null) return;

        var resolved = await SprintAuthorizationQueries.ResolveAsync(db, sprintId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await SprintAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        var role = await SprintAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role is ProjectRole.Developer or ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}

public class SprintManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<SprintManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SprintManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var sprintId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "sprintId");
        if (sprintId is null) return;

        var resolved = await SprintAuthorizationQueries.ResolveAsync(db, sprintId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await SprintAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await SprintAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) == ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}
```

- [x] **Step 4: Write CreateSprint, ListSprints, GetSprint**

```csharp
// src/Api/Features/Sprints/CreateSprint.cs
using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md FR-01, BR-04, BR-08; spec/05-projects.md BR-04 (archived-project write-lock).</summary>
public static class CreateSprint
{
    public record Request(string Name, string? Goal, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public record Response(Guid Id, Guid BoardId, string Name, string? Goal, string Status, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Goal).MaximumLength(500);
            RuleFor(x => x.PlannedEndDateUtc).GreaterThan(x => x.PlannedStartDateUtc)
                .WithMessage("plannedEndDateUtc must be after plannedStartDateUtc.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid boardId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var board = await db.Boards.SingleOrDefaultAsync(b => b.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            if (board.Type != BoardType.Scrum)
            {
                return Results.BadRequest(new { detail = "Sprints can only be created on Scrum-type Boards." });
            }

            var project = await db.Projects.SingleAsync(p => p.Id == board.ProjectId, cancellationToken);
            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot create a Sprint in an archived Project.");
            }

            var sprint = new Sprint
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                ProjectId = board.ProjectId,
                Name = request.Name.Trim(),
                Goal = request.Goal?.Trim(),
                Status = SprintStatus.Planned,
                PlannedStartDateUtc = request.PlannedStartDateUtc,
                PlannedEndDateUtc = request.PlannedEndDateUtc,
                CreatedByUserId = caller.GetUserId(),
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Sprints.Add(sprint);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/sprints/{sprint.Id}",
                new Response(sprint.Id, sprint.BoardId, sprint.Name, sprint.Goal, sprint.Status, sprint.PlannedStartDateUtc, sprint.PlannedEndDateUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/boards/{boardId:guid}/sprints", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardContribute")
            .WithTags("Sprints");
}
```

```csharp
// src/Api/Features/Sprints/ListSprints.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md §9 — GET /api/boards/{boardId}/sprints.</summary>
public static class ListSprints
{
    public record SprintItem(Guid Id, string Name, string Status, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public record Response(IReadOnlyList<SprintItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.Sprints
                .Where(s => s.BoardId == boardId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .Select(s => new SprintItem(s.Id, s.Name, s.Status, s.PlannedStartDateUtc, s.PlannedEndDateUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/boards/{boardId:guid}/sprints", Handler.Handle)
            .RequireAuthorization("BoardView")
            .WithTags("Sprints");
}
```

```csharp
// src/Api/Features/Sprints/GetSprint.cs
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md §9 — GET /api/sprints/{sprintId}.</summary>
public static class GetSprint
{
    public record Response(Guid Id, Guid BoardId, string Name, string? Goal, string Status, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var response = await db.Sprints
                .Where(s => s.Id == sprintId)
                .Select(s => new Response(s.Id, s.BoardId, s.Name, s.Goal, s.Status, s.PlannedStartDateUtc, s.PlannedEndDateUtc, s.StartedAtUtc, s.CompletedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);

            return response is null ? Results.NotFound() : Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/sprints/{sprintId:guid}", Handler.Handle)
            .RequireAuthorization("SprintView")
            .WithTags("Sprints");
}
```

- [x] **Step 5: Register handlers, policies, and endpoints in Program.cs**

Add `using JiraLite.Api.Features.Sprints;` to the top. After the `BoardContributeAuthorizationHandler` registration line, add:

```csharp
builder.Services.AddScoped<IAuthorizationHandler, SprintViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SprintContributeAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SprintManageAuthorizationHandler>();
```

After the `"BoardContribute"` policy line, add (moving the trailing `;`):

```csharp
    .AddPolicy("SprintView", policy => policy.RequireAuthenticatedUser().AddRequirements(new SprintViewRequirement()))
    .AddPolicy("SprintContribute", policy => policy.RequireAuthenticatedUser().AddRequirements(new SprintContributeRequirement()))
    .AddPolicy("SprintManage", policy => policy.RequireAuthenticatedUser().AddRequirements(new SprintManageRequirement()));
```

After `ReorderColumns.MapEndpoint(app);`, add:

```csharp
CreateSprint.MapEndpoint(app);
ListSprints.MapEndpoint(app);
GetSprint.MapEndpoint(app);
```

- [x] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter SprintLifecycleTests`
Expected: PASS (3 tests).

- [x] **Step 7: Commit**

```bash
git add src/Api/Common/Auth/SprintAuthorization.cs src/Api/Features/Sprints src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Sprints/SprintLifecycleTests.cs
git commit -m "feat: add Sprint-scoped authorization, CreateSprint, ListSprints, GetSprint"
```

---

### Task 15: EditSprint + StartSprint

**Files:**
- Create: `src/Api/Features/Sprints/EditSprint.cs`, `StartSprint.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Sprints/StartSprintTests.cs`

**Interfaces:**
- Consumes: `"SprintContribute"` policy from Task 14; `IX_Sprint_BoardId_ActiveOnly` filtered unique index from Task 2.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Sprints/StartSprintTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

public class StartSprintTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public StartSprintTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid ScrumBoardId)> SeedScrumBoardAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var scrumBoardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Scrum", type = "Scrum" });
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (client, scrumBoard.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateSprintAsync(HttpClient client, Guid boardId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/sprints", new
        {
            name,
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Starting_a_planned_sprint_makes_it_active()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId, "Sprint 1");

        var response = await client.PostAsync($"/api/sprints/{sprintId}/start", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("startedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Starting_a_second_sprint_while_one_is_already_active_on_the_same_board_is_rejected()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var firstSprintId = await CreateSprintAsync(client, boardId, "Sprint 1");
        var secondSprintId = await CreateSprintAsync(client, boardId, "Sprint 2");
        await client.PostAsync($"/api/sprints/{firstSprintId}/start", null);

        var response = await client.PostAsync($"/api/sprints/{secondSprintId}/start", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Editing_name_and_goal_of_a_planned_sprint_succeeds()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId, "Sprint 1");

        var response = await client.PatchAsJsonAsync($"/api/sprints/{sprintId}", new
        {
            name = "Sprint 1 - Renamed",
            goal = "New goal",
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-17"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sprint 1 - Renamed", body.GetProperty("name").GetString());
    }
}
```

Add `using System.Text.Json;` at the top.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter StartSprintTests`
Expected: FAIL — routes not mapped.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Sprints/EditSprint.cs
using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md BR-03 — planned dates editable only while Status = Planned.</summary>
public static class EditSprint
{
    public record Request(string Name, string? Goal, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public record Response(Guid Id, string Name, string? Goal, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Goal).MaximumLength(500);
            RuleFor(x => x.PlannedEndDateUtc).GreaterThan(x => x.PlannedStartDateUtc)
                .WithMessage("plannedEndDateUtc must be after plannedStartDateUtc.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            sprint.Name = request.Name.Trim();
            sprint.Goal = request.Goal?.Trim();

            if (sprint.Status == SprintStatus.Planned)
            {
                sprint.PlannedStartDateUtc = request.PlannedStartDateUtc;
                sprint.PlannedEndDateUtc = request.PlannedEndDateUtc;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(sprint.Id, sprint.Name, sprint.Goal, sprint.PlannedStartDateUtc, sprint.PlannedEndDateUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/sprints/{sprintId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
```

```csharp
// src/Api/Features/Sprints/StartSprint.cs
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/08-sprints.md FR-02, BR-01, BR-02, NFR-01. The application-level check below rejects the
/// common case with a clean 409; the filtered unique index from Task 2
/// (IX_Sprint_BoardId_ActiveOnly) is the actual source of atomicity under concurrent start calls
/// — a race that wins the check-then-update gap still fails at SaveChangesAsync and is caught here.
/// </summary>
public static class StartSprint
{
    public record Response(Guid Id, string Status, DateTime? StartedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            if (sprint.Status != SprintStatus.Planned)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/invalid-sprint-transition",
                    "Only a Planned Sprint can be started.");
            }

            var anotherActiveExists = await db.Sprints.AnyAsync(
                s => s.BoardId == sprint.BoardId && s.Id != sprintId && s.Status == SprintStatus.Active,
                cancellationToken);
            if (anotherActiveExists)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/board-already-has-active-sprint",
                    "This Board already has an Active Sprint.");
            }

            sprint.Status = SprintStatus.Active;
            sprint.StartedAtUtc = DateTime.UtcNow;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/board-already-has-active-sprint",
                    "This Board already has an Active Sprint.");
            }

            return Results.Ok(new Response(sprint.Id, sprint.Status, sprint.StartedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sprints/{sprintId:guid}/start", Handler.Handle)
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
```

- [ ] **Step 4: Register endpoints in Program.cs**

After `GetSprint.MapEndpoint(app);`, add:

```csharp
EditSprint.MapEndpoint(app);
StartSprint.MapEndpoint(app);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter StartSprintTests`
Expected: PASS (3 tests). The second test's 409 comes from the pre-check (no real concurrency needed to trigger it in a single-threaded test) — the filtered index is the backstop for genuinely concurrent calls, not exercised by this sequential test.

- [ ] **Step 6: Commit**

```bash
git add src/Api/Features/Sprints/EditSprint.cs src/Api/Features/Sprints/StartSprint.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Sprints/StartSprintTests.cs
git commit -m "feat: add EditSprint and StartSprint with single-active-sprint guard"
```

---

### Task 16: CompleteSprint + DeleteSprint

**Files:**
- Create: `src/Api/Features/Sprints/CompleteSprint.cs`, `DeleteSprint.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Sprints/CompleteDeleteSprintTests.cs`

**Interfaces:**
- Consumes: `"SprintContribute"` (Complete), `"SprintManage"` (Delete) policies from Task 14.

**Deferral reminder:** `CompleteSprint` here only performs the status transition (`Active → Completed`, sets `CompletedAtUtc`). The BR-05 carry-forward-incomplete-Issues behavior is deferred to Phase 4 (see Task 18) since it requires querying `Issue`, which doesn't exist yet.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Sprints/CompleteDeleteSprintTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

public class CompleteDeleteSprintTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CompleteDeleteSprintTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid BoardId)> SeedScrumBoardAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var scrumBoardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Scrum", type = "Scrum" });
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (client, scrumBoard.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateSprintAsync(HttpClient client, Guid boardId)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/sprints", new
        {
            name = "Sprint 1",
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Completing_an_active_sprint_transitions_it_and_sets_completed_at()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId);
        await client.PostAsync($"/api/sprints/{sprintId}/start", null);

        var response = await client.PostAsync($"/api/sprints/{sprintId}/complete", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("completedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Completing_a_sprint_that_is_not_active_is_rejected()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId);

        var response = await client.PostAsync($"/api/sprints/{sprintId}/complete", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_planned_sprint_succeeds_but_deleting_an_active_one_is_rejected()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var plannedSprintId = await CreateSprintAsync(client, boardId);
        var deleteResponse = await client.DeleteAsync($"/api/sprints/{plannedSprintId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var activeSprintId = await CreateSprintAsync(client, boardId);
        await client.PostAsync($"/api/sprints/{activeSprintId}/start", null);
        var rejectedResponse = await client.DeleteAsync($"/api/sprints/{activeSprintId}");

        Assert.Equal(HttpStatusCode.Conflict, rejectedResponse.StatusCode);
    }
}
```

Add `using System.Text.Json;` at the top.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter CompleteDeleteSprintTests`
Expected: FAIL — routes not mapped.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Sprints/CompleteSprint.cs
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/08-sprints.md FR-04, BR-02 — status transition only. BR-05 (carry-forward incomplete
/// Issues to the Product Backlog or another Sprint) is deferred to Phase 4 — no Issue entity
/// exists yet to query/move. carriedForwardIssueCount is always 0 in Phase 3.
/// </summary>
public static class CompleteSprint
{
    public record Response(Guid Id, string Status, DateTime? CompletedAtUtc, int CarriedForwardIssueCount);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            if (sprint.Status != SprintStatus.Active)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/invalid-sprint-transition",
                    "Only an Active Sprint can be completed.");
            }

            sprint.Status = SprintStatus.Completed;
            sprint.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(sprint.Id, sprint.Status, sprint.CompletedAtUtc, CarriedForwardIssueCount: 0));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sprints/{sprintId:guid}/complete", Handler.Handle)
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
```

```csharp
// src/Api/Features/Sprints/DeleteSprint.cs
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md FR-06, BR-06 — Planned only. No Issues to return to the Product Backlog yet (Phase 4).</summary>
public static class DeleteSprint
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            if (sprint.Status != SprintStatus.Planned)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/sprint-not-planned",
                    "Only a Planned Sprint can be deleted.");
            }

            db.Sprints.Remove(sprint);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/sprints/{sprintId:guid}", Handler.Handle)
            .RequireAuthorization("SprintManage")
            .WithTags("Sprints");
}
```

- [ ] **Step 4: Register endpoints in Program.cs**

After `StartSprint.MapEndpoint(app);`, add:

```csharp
CompleteSprint.MapEndpoint(app);
DeleteSprint.MapEndpoint(app);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter CompleteDeleteSprintTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Api/Features/Sprints/CompleteSprint.cs src/Api/Features/Sprints/DeleteSprint.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Sprints/CompleteDeleteSprintTests.cs
git commit -m "feat: add CompleteSprint (status transition) and DeleteSprint"
```

---

### Task 17: GetMyActivity (cursor-paginated)

**Files:**
- Create: `src/Api/Features/Users/GetMyActivity.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/JiraLite.Api.IntegrationTests/Users/GetMyActivityTests.cs`

**Interfaces:**
- Consumes: `CursorPagination` from Task 3, `ActivityLogEntry` from Task 2.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/JiraLite.Api.IntegrationTests/Users/GetMyActivityTests.cs
using System.Net;
using System.Net.Http.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Users;

public class GetMyActivityTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public GetMyActivityTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Returns_empty_page_for_a_user_with_no_activity()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);

        var response = await client.GetAsync("/api/users/me/activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.False(body.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
    }

    [Fact]
    public async Task Paginates_activity_newest_first_and_the_second_page_follows_the_cursor()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, user.AccessToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.ActivityLogEntries.Add(new ActivityLogEntry
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = user.UserId,
                    WorkspaceId = workspaceId,
                    ProjectId = null,
                    EntityType = "Workspace",
                    EntityId = workspaceId,
                    Action = "Created",
                    Summary = $"did thing {i}",
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(i)
                });
            }
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);
        var firstPageResponse = await client.GetAsync("/api/users/me/activity?limit=2");
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstItems = firstPage.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, firstItems.Count);
        Assert.Equal("did thing 2", firstItems[0].GetProperty("summary").GetString());
        Assert.True(firstPage.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        var cursor = firstPage.GetProperty("pageInfo").GetProperty("nextCursor").GetString();

        var secondPageResponse = await client.GetAsync($"/api/users/me/activity?limit=2&cursor={Uri.EscapeDataString(cursor!)}");
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondItems = secondPage.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(secondItems);
        Assert.Equal("did thing 0", secondItems[0].GetProperty("summary").GetString());
        Assert.False(secondPage.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
    }
}
```

Add `using System.Text.Json;` and `using System.Linq;` at the top.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter GetMyActivityTests`
Expected: FAIL — route not mapped.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Api/Features/Users/GetMyActivity.cs
using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>
/// spec/02-users.md FR-05, NFR-02; spec/19-api-guidelines.md §5 — cursor-paginated, newest first.
/// Deferred from Phase 1 (spec/21-roadmap.md Phase 1 note) — ActivityLogEntry needed Project to exist.
/// </summary>
public static class GetMyActivity
{
    public record ActivityItem(Guid Id, string EntityType, Guid EntityId, string Action, string Summary, DateTime OccurredAtUtc);

    public record Response(IReadOnlyList<ActivityItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            int? limit,
            string? cursor,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);
            var userId = caller.GetUserId();

            var page = await db.ActivityLogEntries
                .Where(e => e.ActorUserId == userId)
                .OrderByDescending(e => e.OccurredAtUtc)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(e => new ActivityItem(e.Id, e.EntityType, e.EntityId, e.Action, e.Summary, e.OccurredAtUtc))
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var items = page.Take(pageSize).ToList();
            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;

            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/me/activity", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
```

- [ ] **Step 4: Register the endpoint in Program.cs**

After `DeactivateAccount.MapEndpoint(app);`, add:

```csharp
GetMyActivity.MapEndpoint(app);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/JiraLite.Api.IntegrationTests --filter GetMyActivityTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Api/Features/Users/GetMyActivity.cs src/Api/Program.cs tests/JiraLite.Api.IntegrationTests/Users/GetMyActivityTests.cs
git commit -m "feat: add GetMyActivity with cursor pagination (deferred from Phase 1)"
```

---

### Task 18: Document the three Phase 4 deferrals in the roadmap

**Files:**
- Modify: `spec/21-roadmap.md`

**Interfaces:** None — documentation only.

- [ ] **Step 1: Add a deferral note to the Phase 4 section**

In `spec/21-roadmap.md`, find the Phase 4 section (`## 6. Phase 4 — Work Tracking`). Immediately after its **Goals:** line and before **Deliverables:**, add:

```markdown
**Note on carry-over from Phase 3:** Three pieces of `spec/08-sprints.md` and `spec/06-boards.md` behavior could not be built in Phase 3 because they require `Issue`, which doesn't exist until this phase — mirroring the same forward-reference logic already documented for `ActivityLogEntry` in Phase 1's note. All three must be added here, alongside `Issue`:
- **Sprint completion carry-forward** ([08-sprints.md](08-sprints.md) BR-05): Phase 3's `CompleteSprint` only performs the `Active → Completed` status transition; the "move incomplete Issues to the Product Backlog or another Sprint" logic must be retrofitted once `Issue` exists.
- **`POST/DELETE /sprints/{sprintId}/issues`** and **`GET /boards/{boardId}/issues`** ([08-sprints.md](08-sprints.md) §9, [06-boards.md](06-boards.md) §9): not implemented in Phase 3 at all — both require querying/mutating `Issue`.
- **Board/Column delete Issue-presence guards** ([06-boards.md](06-boards.md) BR-03, BR-05): Phase 3's `DeleteBoard`/`DeleteColumn` only enforce the structural guards (last-Board, last-Column) and the Sprint-reference guard (BR-09); the Issue-presence checks must be added to both handlers once `Issue` exists.
```

- [ ] **Step 2: Verify the edit renders correctly**

Run: view the file (or `git diff spec/21-roadmap.md`) and confirm the note reads correctly inline with the rest of the Phase 4 section, doesn't break any Markdown table below it, and the phrasing matches the tone of the existing Phase 1 deferral note.

- [ ] **Step 3: Commit**

```bash
git add spec/21-roadmap.md
git commit -m "docs: document Phase 3->4 deferrals (Sprint carry-forward, board/sprint issue endpoints, delete guards)"
```

---

### Task 19: Full regression pass

**Files:** None created — verification only.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: Solution builds with 0 errors/warnings (both `src/Api` and `tests/JiraLite.Api.IntegrationTests`).

- [ ] **Step 2: Run the full integration test suite**

Run: `dotnet test`
Expected: All tests across every task in this plan pass (HealthCheck, schema, pagination, Projects x5, Workspaces cascade, Boards x3, Sprints x3, Users activity — roughly 30+ tests total).

- [ ] **Step 3: Manual Swagger smoke pass per spec/21-roadmap.md Phase 3 Definition of Done**

Run: `docker compose up` (or `dotnet run --project src/Api`), open `/swagger`, and walk through: create a Project (verify default Board + 3 columns exist), archive it (verify write-lock on Board/Sprint creation), unarchive, add/change/remove a Project member, create a second Scrum Board, create and start a Sprint (verify a second Sprint on the same Board can't start while the first is Active), complete it, reorder columns on the default Board, and hit `GET /api/users/me/activity` to see it return an empty page. Confirm each matches the acceptance criteria in `spec/05-projects.md` §15, `spec/06-boards.md` §15, `spec/08-sprints.md` §15, and `spec/16-rbac.md` §15.

- [ ] **Step 4: Fix any regressions found, then commit**

If Step 2 or Step 3 surfaces a bug, fix it in the relevant task's file (not a new bolt-on file), re-run the affected test(s), and commit the fix with a message referencing which behavior was broken. If nothing is found, no commit is needed for this task.

---

## Summary

This plan delivers all 8 Phase 3 roadmap tasks (T022, T014, T028, T023, T024, T025, T027, T026) across 19 implementation tasks, introduces the project's first integration test project (Testcontainers + real SQL Server) and its first cursor-paginated endpoint, retrofits the Phase 2 `RemoveMember`/`LeaveWorkspace` handlers per the `ProjectMember` cascade they were already stubbed for, and explicitly hands off three Issue-dependent behaviors to Phase 4 with a matching roadmap note.

