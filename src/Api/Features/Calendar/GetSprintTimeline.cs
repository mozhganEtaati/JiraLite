using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Calendar;

/// <summary>spec/15-calendar.md FR-02, BR-03, BR-04 — Sprints across every Scrum Board in the Project.</summary>
public static class GetSprintTimeline
{
    public record SprintItem(
        Guid Id, Guid BoardId, string Name, string Status,
        DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc);

    public record Response(IReadOnlyList<SprintItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            DateOnly? from,
            DateOnly? to,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
            {
                return Results.NotFound();
            }

            if (from is not null && to is not null && to < from)
            {
                return Results.BadRequest(new { detail = "to must not be earlier than from." });
            }

            var query = db.Sprints.Where(s => s.ProjectId == projectId);

            if (from is not null)
            {
                query = query.Where(s => s.PlannedEndDateUtc >= from);
            }

            if (to is not null)
            {
                query = query.Where(s => s.PlannedStartDateUtc <= to);
            }

            var items = await query
                .OrderBy(s => s.PlannedStartDateUtc)
                .Select(s => new SprintItem(
                    s.Id, s.BoardId, s.Name, s.Status, s.PlannedStartDateUtc, s.PlannedEndDateUtc, s.StartedAtUtc, s.CompletedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/calendar/sprint-timeline", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Calendar");
}
