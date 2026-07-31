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
