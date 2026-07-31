using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md BR-01 (last column on a Board), BR-02 (deleting the sole Default or sole
/// Done column would leave the Board without one, same invariant EditColumn enforces on unset),
/// and BR-03 (Issue-presence guard).
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

            if (await db.Issues.AnyAsync(i => i.BoardColumnId == columnId, cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/column-has-issues",
                    "This Column cannot be deleted while it has Issues currently placed on it.");
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
