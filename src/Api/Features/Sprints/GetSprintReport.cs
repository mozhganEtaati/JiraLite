using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/24-reports.md — the whole-team read of one Sprint: how far the work has travelled against
/// how far through its calendar it is, where it sits, who is carrying it, and what is at risk.
///
/// Everything here composes reads over Issue, BoardColumn and Sprint; no entity is touched (BR-01).
/// Aggregation happens in memory rather than in SQL on purpose — a Sprint is tens of Issues, and
/// the pace/health rules need the same rows three ways. The one thing this must never do is count
/// Subtasks (BR-02): counting a Subtask beside its parent counts the same work twice and quietly
/// makes every percentage on the page wrong.
/// </summary>
public static class GetSprintReport
{
    public record SprintInfo(
        Guid Id, string Name, string? Goal, string Status,
        DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc,
        DateTime? StartedAtUtc, DateTime? CompletedAtUtc, int? CarriedForwardIssueCount);

    public record Pace(int TotalDays, int ElapsedDays, int RemainingDays, int ExpectedPercent);

    public record IssueCounts(int Total, int Done, int Open);

    public record PointCounts(decimal Total, decimal Done, decimal Open, int UnestimatedIssues);

    public record Progress(IssueCounts Issues, PointCounts Points, int DonePercentByIssues, int DonePercentByPoints);

    public record StatusBucket(string Name, int Count, decimal Points, bool IsDone);

    public record AssigneeBucket(UserSummary? User, int Total, int Done, int Open, decimal Points, int Blocked);

    public record BlockedIssue(Guid Id, string Key, string Title, string? BlockedReason, DateTime? BlockedSinceUtc, int BlockedDays);

    public record Risks(
        IReadOnlyList<BlockedIssue> Blocked,
        int OverdueCount,
        int DueAfterSprintEndCount,
        int UnassignedCount,
        int UnestimatedCount);

    public record HealthReason(string Code, string Detail);

    public record Health(string? State, IReadOnlyList<HealthReason> Reasons);

    public record Response(
        SprintInfo Sprint,
        Pace? Pace,
        Progress Progress,
        IReadOnlyList<StatusBucket> ByStatus,
        IReadOnlyList<AssigneeBucket> ByAssignee,
        Risks Risks,
        Health Health);

    private const string OnTrack = "OnTrack";
    private const string AtRisk = "AtRisk";
    private const string OffTrack = "OffTrack";

    /// <summary>How far behind the calendar the work may fall before it is worth saying so.</summary>
    private const int BehindPaceThreshold = 10;
    private const int WellBehindPaceThreshold = 25;

    /// <summary>Below half-elapsed, a gap against the pace line is normal rather than alarming.</summary>
    private const int WellBehindMinimumElapsedPercent = 50;

    /// <summary>Blocked work above this share of open work is the Sprint's problem, not an incident.</summary>
    private const int HeavilyBlockedPercent = 20;

    /// <summary>
    /// A floor under the share, because on a small Sprint the share alone is twitchy: one blocker
    /// among five open Issues is already a fifth of the work, and calling that Sprint OffTrack for
    /// a single incident is the kind of verdict people learn to ignore.
    /// </summary>
    private const int HeavilyBlockedMinimumCount = 2;

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid sprintId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            var rows = await db.Issues
                .Where(i => i.SprintId == sprintId && i.Type != IssueType.Subtask)
                .Join(db.BoardColumns, i => i.BoardColumnId, c => c.Id, (i, c) => new
                {
                    i.Id,
                    i.Key,
                    i.Title,
                    i.Priority,
                    i.AssigneeUserId,
                    i.DueDateUtc,
                    i.Estimate,
                    i.IsBlocked,
                    i.BlockedReason,
                    i.BlockedSinceUtc,
                    ColumnName = c.Name,
                    c.IsDoneColumn,
                    c.DisplayOrder
                })
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);

            var open = rows.Where(r => !r.IsDoneColumn).ToList();
            var done = rows.Where(r => r.IsDoneColumn).ToList();

            var issueCounts = new IssueCounts(rows.Count, done.Count, open.Count);
            var pointCounts = new PointCounts(
                rows.Sum(r => r.Estimate ?? 0m),
                done.Sum(r => r.Estimate ?? 0m),
                open.Sum(r => r.Estimate ?? 0m),
                rows.Count(r => r.Estimate is null));

            var donePercentByIssues = Percent(done.Count, rows.Count);
            var donePercentByPoints = Percent(pointCounts.Done, pointCounts.Total);
            var progress = new Progress(issueCounts, pointCounts, donePercentByIssues, donePercentByPoints);

            // Grouped by Column *name*, done columns last — the same rule the dashboard's status
            // breakdown follows (spec/14-dashboard.md BR-10). A Sprint whose Board was renamed, or
            // an Issue moved to a second Board mid-Sprint, should still read as one lane per name.
            var byStatus = rows
                .GroupBy(r => new { r.ColumnName, r.IsDoneColumn })
                .Select(g => new
                {
                    Bucket = new StatusBucket(g.Key.ColumnName, g.Count(), g.Sum(r => r.Estimate ?? 0m), g.Key.IsDoneColumn),
                    FirstPosition = g.Min(r => r.DisplayOrder)
                })
                .OrderBy(x => x.Bucket.IsDone)
                .ThenBy(x => x.FirstPosition)
                .ThenBy(x => x.Bucket.Name)
                .Select(x => x.Bucket)
                .ToList();

            var assignees = await db.GetUserSummariesAsync(
                rows.Where(r => r.AssigneeUserId is not null).Select(r => r.AssigneeUserId!.Value),
                cancellationToken);

            // Unassigned work gets its own row rather than being dropped: it is precisely what
            // someone opening this page is looking for.
            var byAssignee = rows
                .GroupBy(r => r.AssigneeUserId)
                .Select(g => new AssigneeBucket(
                    g.Key is not null && assignees.TryGetValue(g.Key.Value, out var summary) ? summary : null,
                    g.Count(),
                    g.Count(r => r.IsDoneColumn),
                    g.Count(r => !r.IsDoneColumn),
                    g.Sum(r => r.Estimate ?? 0m),
                    // Open only, so this agrees with risks.blocked rather than counting a
                    // stale flag on work that has since finished (BR-12).
                    g.Count(r => r.IsBlocked && !r.IsDoneColumn)))
                .OrderBy(b => b.User is null)
                .ThenByDescending(b => b.Total)
                .ThenBy(b => b.User?.DisplayName)
                .ToList();

            var blocked = open
                .Where(r => r.IsBlocked)
                .OrderBy(r => r.BlockedSinceUtc)
                .Select(r => new BlockedIssue(
                    r.Id, r.Key, r.Title, r.BlockedReason, r.BlockedSinceUtc,
                    r.BlockedSinceUtc is null ? 0 : (int)(now - r.BlockedSinceUtc.Value).TotalDays))
                .ToList();

            var overdueCount = open.Count(r => r.DueDateUtc is not null && r.DueDateUtc < today);
            var dueAfterSprintEndCount = open.Count(r => r.DueDateUtc is not null && r.DueDateUtc > sprint.PlannedEndDateUtc);

            // Both unestimated figures are deliberate and they differ (BR-11): the one on `points`
            // qualifies point totals that include finished work, so it counts every Issue; the one
            // here is work someone could still go and estimate, so it counts only open Issues.
            var risks = new Risks(
                blocked,
                overdueCount,
                dueAfterSprintEndCount,
                open.Count(r => r.AssigneeUserId is null),
                open.Count(r => r.Estimate is null));

            var pace = BuildPace(sprint, today);
            var health = Judge(sprint, pace, rows.Count, open.Count, donePercentByIssues, risks);

            var info = new SprintInfo(
                sprint.Id, sprint.Name, sprint.Goal, sprint.Status,
                sprint.PlannedStartDateUtc, sprint.PlannedEndDateUtc,
                sprint.StartedAtUtc, sprint.CompletedAtUtc, sprint.CarriedForwardIssueCount);

            return Results.Ok(new Response(info, pace, progress, byStatus, byAssignee, risks, health));
        }

        /// <summary>
        /// BR-04. A Planned Sprint has no pace — nothing has elapsed, so there is nothing to be
        /// behind. An Active one is measured from StartedAtUtc, which spec/08-sprints.md BR-03
        /// makes the source of truth for when the Sprint actually began, to its planned end.
        /// A Completed one is simply over, whatever the calendar said.
        /// </summary>
        private static Pace? BuildPace(Sprint sprint, DateOnly today)
        {
            if (sprint.Status == SprintStatus.Planned)
            {
                return null;
            }

            var start = sprint.StartedAtUtc is null
                ? sprint.PlannedStartDateUtc
                : DateOnly.FromDateTime(sprint.StartedAtUtc.Value);

            var end = sprint.Status == SprintStatus.Completed && sprint.CompletedAtUtc is not null
                ? DateOnly.FromDateTime(sprint.CompletedAtUtc.Value)
                : sprint.PlannedEndDateUtc;

            // Inclusive of both ends: a Sprint that starts and ends on the same day is one day long,
            // not zero — and a zero here would divide the pace percentage by nothing.
            var totalDays = Math.Max(end.DayNumber - start.DayNumber + 1, 1);

            var elapsedDays = sprint.Status == SprintStatus.Completed
                ? totalDays
                : Math.Clamp(today.DayNumber - start.DayNumber + 1, 0, totalDays);

            return new Pace(totalDays, elapsedDays, totalDays - elapsedDays, Percent(elapsedDays, totalDays));
        }

        /// <summary>
        /// BR-05..BR-07. Every distinct problem is reported, each at the severity it actually
        /// reached — a Sprint far behind pace says so once, as WellBehindPace, rather than saying
        /// the same thing twice at two severities. The state is the worst reason present, and the
        /// reasons are returned with it so the reader can see exactly what produced it.
        /// </summary>
        private static Health Judge(
            Sprint sprint, Pace? pace, int totalIssues, int openIssues, int donePercentByIssues, Risks risks)
        {
            if (sprint.Status == SprintStatus.Planned)
            {
                return new Health(null, []);
            }

            if (totalIssues == 0)
            {
                return new Health(OnTrack, [new HealthReason("EmptySprint", "No issues in this sprint yet.")]);
            }

            var reasons = new List<HealthReason>();
            var state = OnTrack;

            void Raise(string toState, string code, string detail)
            {
                reasons.Add(new HealthReason(code, detail));
                if (toState == OffTrack || (toState == AtRisk && state == OnTrack))
                {
                    state = toState;
                }
            }

            if (pace is not null)
            {
                var behindBy = pace.ExpectedPercent - donePercentByIssues;
                var paceDetail = $"{donePercentByIssues}% done, {pace.ExpectedPercent}% of the sprint elapsed";

                if (pace.ExpectedPercent >= WellBehindMinimumElapsedPercent && behindBy > WellBehindPaceThreshold)
                {
                    Raise(OffTrack, "WellBehindPace", paceDetail);
                }
                else if (behindBy > BehindPaceThreshold)
                {
                    Raise(AtRisk, "BehindPace", paceDetail);
                }
            }

            var blockedCount = risks.Blocked.Count;
            if (blockedCount > 0)
            {
                var blockedDetail = blockedCount == 1 ? "1 blocked issue" : $"{blockedCount} blocked issues";

                if (blockedCount >= HeavilyBlockedMinimumCount
                    && openIssues > 0
                    && blockedCount * 100 >= openIssues * HeavilyBlockedPercent)
                {
                    Raise(OffTrack, "HeavilyBlocked", $"{blockedDetail} — {Percent(blockedCount, openIssues)}% of open work");
                }
                else
                {
                    Raise(AtRisk, "BlockedWork", blockedDetail);
                }
            }

            if (risks.OverdueCount > 0)
            {
                Raise(AtRisk, "OverdueWork",
                    risks.OverdueCount == 1 ? "1 open issue is past its due date" : $"{risks.OverdueCount} open issues are past their due date");
            }

            if (risks.DueAfterSprintEndCount > 0)
            {
                Raise(AtRisk, "DueAfterSprintEnd",
                    risks.DueAfterSprintEndCount == 1
                        ? "1 open issue is due after the sprint ends"
                        : $"{risks.DueAfterSprintEndCount} open issues are due after the sprint ends");
            }

            return new Health(state, reasons);
        }

        private static int Percent(int part, int whole) =>
            whole == 0 ? 0 : (int)Math.Round(part * 100.0 / whole, MidpointRounding.AwayFromZero);

        private static int Percent(decimal part, decimal whole) =>
            whole == 0 ? 0 : (int)Math.Round(part * 100m / whole, MidpointRounding.AwayFromZero);
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/sprints/{sprintId:guid}/report", Handler.Handle)
            .RequireAuthorization("SprintView")
            .WithTags("Sprints");
}
