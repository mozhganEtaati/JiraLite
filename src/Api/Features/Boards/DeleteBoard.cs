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
