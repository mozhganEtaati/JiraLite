# 20 — Coding Guidelines

## 1. Overview

This document defines how the Vertical Slice Architecture described in [00-project-overview.md](00-project-overview.md) §6 is implemented in code: folder structure, naming, DTOs, validation, logging, DI, configuration, and migrations. It governs `src/Api` for all features in [01](01-authentication.md)–[17](17-admin.md).

## 2. Folder Structure

```
src/
  Api/
    Features/
      Auth/                 Login.cs, Register.cs, Logout.cs, RefreshToken.cs
      Users/                 GetProfile.cs, UpdateProfile.cs, UploadAvatar.cs, ...
      Workspaces/            CreateWorkspace.cs, InviteMember.cs, AcceptInvitation.cs, ...
      Teams/                 CreateTeam.cs, AddTeamMember.cs, SetTeamLead.cs, ...
      Projects/              CreateProject.cs, ArchiveProject.cs, DeleteProject.cs, ...
      Boards/                 CreateBoard.cs, AddColumn.cs, ReorderColumns.cs, ...
      Backlog/                GetProductBacklog.cs, RepositionIssue.cs, ...
      Sprints/                CreateSprint.cs, StartSprint.cs, CompleteSprint.cs, ...
      Issues/                 CreateIssue.cs, MoveIssue.cs, DeleteIssue.cs, ...
      Comments/               AddComment.cs, EditComment.cs, DeleteComment.cs
      Attachments/            UploadAttachment.cs, DownloadAttachment.cs, ...
      Labels/                 CreateLabel.cs, AttachLabel.cs, ...
      Notifications/          ListNotifications.cs, MarkNotificationRead.cs, ...
      Dashboard/              GetMyTasks.cs, GetMyProjects.cs, GetRecentActivity.cs
      Calendar/               GetDueDates.cs, GetSprintTimeline.cs
      Admin/                  GetWorkspaceOverview.cs, ListWorkspaceUsers.cs, ...
    Common/
      Domain/                 Entity classes ([18-database.md](18-database.md)), enums, domain constants
      Infrastructure/
        Persistence/           JiraLiteDbContext.cs, EF Core entity configurations, migrations
        FileStorage/            IFileStorage.cs, LocalDiskFileStorage.cs
        Email/                  IEmailSender.cs, SmtpEmailSender.cs
        BackgroundJobs/         Hangfire job classes ([13-notifications.md](13-notifications.md))
      Auth/                    JWT issuance/validation, authorization policy definitions ([16-rbac.md](16-rbac.md))
      Behaviors/                Validation pipeline behavior, logging behavior, exception-to-ProblemDetails middleware
      Pagination/               Cursor encode/decode helpers ([19-api-guidelines.md](19-api-guidelines.md))
    Program.cs
```

A slice never references another slice's types directly. Cross-slice needs go through `Common/Domain` and the shared `DbContext` only.

## 3. Naming Conventions

- **One file per use case**, named `{Verb}{Entity}.cs` (e.g., `CreateIssue.cs`, `MoveIssue.cs`, `AcceptInvitation.cs`), matching the endpoint's primary action.
- Each file contains a single static class named after the use case, with nested types:
  ```csharp
  public static class CreateIssue
  {
      public record Request(string Title, string Type, ...);
      public record Response(Guid Id, string Key, ...);
      public class Validator : AbstractValidator<Request> { ... }
      public static class Handler
      {
          public static async Task<Response> Handle(Request request, JiraLiteDbContext db, ...) { ... }
      }
      public static void MapEndpoint(IEndpointRouteBuilder app) =>
          app.MapPost("/api/projects/{projectId}/issues", ...);
  }
  ```
- C# standard conventions: `PascalCase` for classes/methods/properties, `camelCase` for parameters/locals, `_camelCase` for private fields.
- Entity classes in `Common/Domain` are named identically to their [18-database.md](18-database.md) table name (`Issue`, `BoardColumn`, `WorkspaceMember`).
- Route/DTO field names are `camelCase` in JSON; C# properties are `PascalCase` (handled automatically by `System.Text.Json`'s default policy — see [19-api-guidelines.md](19-api-guidelines.md) §7).

## 4. DTO Rules

- `Request` and `Response` types are C# `record` types — immutable, structurally comparable, one pair per use case. They are never shared across slices, even when two slices return similar-looking data (a small amount of duplication here is preferred over a shared DTO that couples unrelated slices — [00-project-overview.md](00-project-overview.md) §5 principle 3).
- EF Core entities are never returned directly from an endpoint. Every `Handler` projects entities into a `Response` explicitly (LINQ `Select`, or a constructor call) — no reflection-based auto-mapping library (AutoMapper, Mapster). Explicit projection is preferred both for clarity and because it lets EF Core translate the projection into a single efficient SQL query.
- Nested entity summaries (assignee, reporter, author — [19-api-guidelines.md](19-api-guidelines.md) §7) are small shared `record`s in `Common/Domain` (e.g., `UserSummary(Guid Id, string DisplayName, string? AvatarUrl)`), since they are genuinely identical across every slice that references a User.

## 5. Validation Strategy

- Every `Request` has a colocated `Validator : AbstractValidator<Request>` using FluentValidation, defined in the same file.
- A single shared pipeline behavior (`Common/Behaviors/ValidationBehavior`) runs the matching validator before the handler executes, for every request — no handler performs its own field-presence validation.
- Business-rule validation that requires a database lookup (e.g., "email already registered," "last Admin cannot be removed") happens inside the `Handler`, not the `Validator` — validators check shape/format only; handlers check state.
- Validation failures short-circuit into the `400` Problem Details response defined in [19-api-guidelines.md](19-api-guidelines.md) §9 without ever reaching the handler.

## 6. Logging Rules

- Serilog structured logging throughout; every log call uses message templates with named properties (`Log.Information("Issue {IssueId} moved to column {ColumnId}", issueId, columnId)`), never string interpolation.
- Every request is enriched with a correlation id that matches the `traceId` returned in any Problem Details error response ([19-api-guidelines.md](19-api-guidelines.md) §9).
- Log levels:
  - `Information` — successful state-changing operations (created, moved, deleted, archived).
  - `Warning` — rejected business-rule violations (409s) and authorization denials (403s) — these are expected traffic, not bugs, but worth surfacing in aggregate.
  - `Error` — unhandled exceptions (500s) only.
- Never log `PasswordHash`, raw refresh token values, or full `Email` in bulk operations — log `UserId` as the correlating identifier instead.
- Sinks: Console (structured JSON) always; a file or Seq sink is added per environment via configuration (§8), not hardcoded.

## 7. Dependency Injection

- The built-in ASP.NET Core DI container is used — no third-party container.
- Each bounded context registers its own services via an extension method called from `Program.cs` (e.g., `builder.Services.AddWorkTrackingInfrastructure(...)`), keeping `Program.cs` a thin composition root, not a dumping ground of `AddScoped` calls.
- `JiraLiteDbContext` is registered `Scoped` (EF Core default) — one instance per HTTP request.
- `IFileStorage` and `IEmailSender` are registered as interfaces with a single concrete implementation selected by configuration (`LocalDiskFileStorage` in V1 — [11-attachments.md](11-attachments.md)); no handler ever new()s a concrete infrastructure class.
- Hangfire job classes are registered as `Scoped` and resolved by Hangfire's DI integration at execution time, never invoked directly from a request handler (jobs are enqueued via `IBackgroundJobClient`, never called in-process).

## 8. Configuration

- `appsettings.json` for defaults, `appsettings.{Environment}.json` for overrides, environment variables for secrets and Docker/deployment-specific values (connection strings, JWT signing key) — never committed secrets in `appsettings.json`.
- Each cross-cutting concern binds to a strongly-typed options class via `IOptions<T>` (`JwtOptions`, `FileStorageOptions`, `HangfireOptions`, `EmailOptions`) — no handler or service reads `IConfiguration["SomeKey"]` directly by magic string.
- Example: refresh token lifetime ([01-authentication.md](01-authentication.md)), max attachment size ([11-attachments.md](11-attachments.md) NFR-01), and invitation expiry ([03-workspaces.md](03-workspaces.md) BR-07) are all configuration values, not hardcoded constants, so they can be tuned per environment without a code change.

## 9. Migration Strategy

- EF Core Code-First migrations; the `Common/Domain` entity classes and their Fluent API configurations (in `Common/Infrastructure/Persistence`) are the source of truth — the database schema in [18-database.md](18-database.md) is generated from them, not maintained by hand.
- One migration per meaningful schema change, named descriptively (`AddIssueRankColumn`, `AddBoardColumnDoneFlag`) — migrations are never squashed or renamed after being merged.
- Migrations are applied automatically on startup only in `Development` and the Docker Compose local environment. In any other environment, applying migrations is an explicit, separate deployment step (`dotnet ef database update` or an equivalent CI/CD job) — this is a deliberate safety rule to prevent an application restart from silently altering a production schema.
- Every migration that changes a column referenced by a business rule with a database constraint (e.g., the unique index in [12-labels.md](12-labels.md) BR-01, or the FK actions in [18-database.md](18-database.md) §9) must be reviewed against that rule before merging.

## 10. Related Documents

- [00-project-overview.md](00-project-overview.md) §6 — the architectural rationale for these conventions
- [18-database.md](18-database.md) — schema these entity configurations produce
- [19-api-guidelines.md](19-api-guidelines.md) — conventions these DTOs/handlers implement
