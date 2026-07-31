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
