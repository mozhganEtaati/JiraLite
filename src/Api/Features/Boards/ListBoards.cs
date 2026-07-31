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
