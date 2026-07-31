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
