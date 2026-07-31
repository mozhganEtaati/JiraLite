using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Ranking;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/08-sprints.md FR-06, BR-06 — Planned only; moves any assigned Issues back to the Product
/// Backlog first, since Issue.SprintId is a NO ACTION FK (spec/18-database.md §9) that would
/// otherwise reject the delete outright while any Issue still references this Sprint.
/// </summary>
public static class DeleteSprint
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            if (sprint.Status != SprintStatus.Planned)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/sprint-not-planned",
                    "Only a Planned Sprint can be deleted.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Appended one at a time (preserving relative order) rather than a single bulk update,
            // since each Issue's Rank is scoped per-list (spec/07-backlog.md BR-01) and must be
            // recomputed for the Product Backlog list, not carried over from the deleted Sprint's list.
            var sprintIssueIds = await db.Issues
                .Where(i => i.SprintId == sprintId && i.Type != IssueType.Subtask)
                .OrderBy(i => i.Rank)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            if (sprintIssueIds.Count > 0)
            {
                var lastBacklogRank = await db.Issues
                    .Where(i => i.ProjectId == sprint.ProjectId && i.SprintId == null)
                    .OrderByDescending(i => i.Rank)
                    .Select(i => i.Rank)
                    .FirstOrDefaultAsync(cancellationToken);

                foreach (var movedIssueId in sprintIssueIds)
                {
                    var newRank = lastBacklogRank is null ? LexoRank.Initial() : LexoRank.Next(lastBacklogRank);
                    await db.Issues.Where(i => i.Id == movedIssueId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, (Guid?)null).SetProperty(i => i.Rank, newRank), cancellationToken);
                    lastBacklogRank = newRank;
                }

                await db.Issues
                    .Where(i => i.ParentIssueId != null && sprintIssueIds.Contains(i.ParentIssueId!.Value))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, (Guid?)null), cancellationToken);
            }

            db.Sprints.Remove(sprint);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/sprints/{sprintId:guid}", Handler.Handle)
            .RequireAuthorization("SprintManage")
            .WithTags("Sprints");
}
