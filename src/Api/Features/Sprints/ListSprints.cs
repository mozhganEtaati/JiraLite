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
