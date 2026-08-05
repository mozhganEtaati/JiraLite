using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Dashboard;

/// <summary>
/// spec/14-dashboard.md FR-04, BR-07, BR-08 — counts behind the dashboard charts. Same scope as
/// GetMyTasks (assigned to the caller, in Projects they belong to, archived Projects excluded), but
/// Done-column Issues are counted rather than hidden: a completion figure needs its denominator.
/// The priority breakdown is the one exception, and counts open work only (BR-11).
/// Read-only, like every view in this document (BR-05).
/// </summary>
public static class GetMyStats
{
    public record Totals(int Assigned, int Open, int Done, int Overdue, int DueSoon);

    public record StatusBucket(string Name, int Count, bool IsDone);

    public record PriorityBucket(string Priority, int Count);

    public record ActivityDay(DateOnly Date, int Created, int Moved, int Commented);

    public record Response(
        int Days,
        Totals Totals,
        IReadOnlyList<StatusBucket> ByStatus,
        IReadOnlyList<PriorityBucket> ByPriority,
        IReadOnlyList<ActivityDay> Activity);

    /// <summary>Issue actions the streak counts. Everything else the caller did is out of frame (BR-08).</summary>
    private const string Created = "Created";
    private const string StatusChanged = "StatusChanged";
    private const string Commented = "Commented";

    public static class Handler
    {
        public static async Task<IResult> Handle(
            int? days,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var window = Math.Clamp(days ?? 14, 7, 90);

            // Day boundaries are UTC, matching Issue.DueDateUtc and every other date in the API.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dueSoonCutoff = today.AddDays(7);
            var firstDay = today.AddDays(-(window - 1));

            var assigned = db.Issues
                .Where(i => i.AssigneeUserId == userId)
                .Where(i => db.ProjectMembers.Any(m => m.ProjectId == i.ProjectId && m.UserId == userId))
                .Join(db.Projects, i => i.ProjectId, p => p.Id, (i, p) => new { Issue = i, Project = p })
                .Where(x => !x.Project.IsArchived)
                .Join(db.BoardColumns, x => x.Issue.BoardColumnId, c => c.Id, (x, c) => new { x.Issue, Column = c });

            // Columns are status (spec/06-boards.md), so a status breakdown is a Column breakdown.
            // Names are grouped across Boards: two Projects both called their lane "In Progress"
            // is one bar to the person reading it, not two.
            var statusGroups = await assigned
                .GroupBy(x => new { x.Column.Name, x.Column.IsDoneColumn })
                .Select(g => new
                {
                    g.Key.Name,
                    g.Key.IsDoneColumn,
                    Count = g.Count(),
                    FirstPosition = g.Min(x => x.Column.DisplayOrder)
                })
                .ToListAsync(cancellationToken);

            var byStatus = statusGroups
                .OrderBy(b => b.IsDoneColumn)
                .ThenBy(b => b.FirstPosition)
                .ThenBy(b => b.Name)
                .Select(b => new StatusBucket(b.Name, b.Count, b.IsDoneColumn))
                .ToList();

            var open = assigned.Where(x => !x.Column.IsDoneColumn);

            // Open work only (BR-11). Priority is a claim on what to pick up next, which finished
            // work makes no claim on — and counting it would put a total here that disagrees with
            // the open total sitting right beside it.
            var priorityCounts = await open
                .GroupBy(x => x.Issue.Priority)
                .Select(g => new { Priority = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            // Every priority is returned, including the empty ones — a chart with a missing row
            // reads as "no Critical work" only if the row is there to be empty.
            var byPriority = IssuePriority.All
                .Reverse()
                .Select(p => new PriorityBucket(p, priorityCounts.FirstOrDefault(c => c.Priority == p)?.Count ?? 0))
                .ToList();

            var overdue = await open.CountAsync(
                x => x.Issue.DueDateUtc != null && x.Issue.DueDateUtc < today, cancellationToken);
            var dueSoon = await open.CountAsync(
                x => x.Issue.DueDateUtc != null && x.Issue.DueDateUtc >= today && x.Issue.DueDateUtc <= dueSoonCutoff,
                cancellationToken);

            var doneCount = byStatus.Where(b => b.IsDone).Sum(b => b.Count);
            var assignedCount = byStatus.Sum(b => b.Count);
            var totals = new Totals(assignedCount, assignedCount - doneCount, doneCount, overdue, dueSoon);

            // The caller's own actions, so no Workspace or Project filter applies — the same
            // actor-scoping GET /api/users/me/activity uses (spec/02-users.md FR-05).
            var activityCounts = await db.ActivityLogEntries
                .Where(e => e.ActorUserId == userId
                    && e.EntityType == "Issue"
                    && e.OccurredAtUtc >= firstDay.ToDateTime(TimeOnly.MinValue))
                .GroupBy(e => new { e.OccurredAtUtc.Date, e.Action })
                .Select(g => new { g.Key.Date, g.Key.Action, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var byDay = activityCounts
                .GroupBy(c => DateOnly.FromDateTime(c.Date))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Dense: a day with nothing on it is a zero-height bar, never a gap in the axis.
            var activity = Enumerable.Range(0, window)
                .Select(offset =>
                {
                    var date = firstDay.AddDays(offset);
                    var entries = byDay.TryGetValue(date, out var found) ? found : [];
                    int CountOf(string action) => entries.FirstOrDefault(e => e.Action == action)?.Count ?? 0;
                    return new ActivityDay(date, CountOf(Created), CountOf(StatusChanged), CountOf(Commented));
                })
                .ToList();

            return Results.Ok(new Response(window, totals, byStatus, byPriority, activity));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard/my-stats", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Dashboard");
}
