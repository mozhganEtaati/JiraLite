using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md BR-04 (last Board in a Project), BR-05 (Issue-presence guard), BR-09 (any
/// Sprint, including Completed, blocks delete).
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

            var columnIds = await db.BoardColumns.Where(c => c.BoardId == boardId).Select(c => c.Id).ToListAsync(cancellationToken);
            if (await db.Issues.AnyAsync(i => columnIds.Contains(i.BoardColumnId), cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/board-has-issues",
                    "This Board cannot be deleted while it has Issues currently placed on any of its columns.");
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
