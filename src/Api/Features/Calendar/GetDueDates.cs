using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Calendar;

/// <summary>spec/15-calendar.md FR-01, BR-01, BR-02, BR-04 — Issues with a due date in the requested range.</summary>
public static class GetDueDates
{
    public record DueDateItem(Guid Id, string Key, string Title, string Type, DateOnly DueDateUtc, UserSummary? Assignee);

    public record Response(IReadOnlyList<DueDateItem> Items);

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

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var rangeStart = from ?? new DateOnly(today.Year, today.Month, 1);
            var rangeEnd = to ?? rangeStart.AddMonths(1).AddDays(-1);

            var pageItems = await db.Issues
                .Where(i => i.ProjectId == projectId && i.DueDateUtc != null)
                .Where(i => i.DueDateUtc >= rangeStart && i.DueDateUtc <= rangeEnd)
                .OrderBy(i => i.DueDateUtc)
                .Select(i => new { i.Id, i.Key, i.Title, i.Type, DueDateUtc = i.DueDateUtc!.Value, i.AssigneeUserId })
                .ToListAsync(cancellationToken);

            var assignees = await db.GetUserSummariesAsync(
                pageItems.Where(i => i.AssigneeUserId is not null).Select(i => i.AssigneeUserId!.Value), cancellationToken);

            var items = pageItems
                .Select(i => new DueDateItem(
                    i.Id, i.Key, i.Title, i.Type, i.DueDateUtc,
                    i.AssigneeUserId is not null && assignees.TryGetValue(i.AssigneeUserId.Value, out var summary) ? summary : null))
                .ToList();

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/calendar/due-dates", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Calendar");
}
