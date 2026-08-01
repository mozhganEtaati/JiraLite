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

            // spec/02-users.md BR-05/BR-06 — a representative Activity entry for Phase 3.
            db.ActivityLogEntries.Add(new ActivityLogEntry
            {
                Id = Guid.NewGuid(),
                ActorUserId = userId,
                WorkspaceId = workspaceId,
                ProjectId = project.Id,
                EntityType = "Project",
                EntityId = project.Id,
                Action = "Created",
                Summary = $"created Project {project.Key}",
                OccurredAtUtc = now
            });

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
