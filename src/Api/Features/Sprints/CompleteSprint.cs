using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/08-sprints.md FR-04, BR-02, BR-05 — status transition plus carry-forward: every Issue in
/// the Sprint whose current column has IsDoneColumn = false moves to the Product Backlog or the
/// specified other Planned Sprint on the same Board; Subtasks follow their parent (BR-11 mirror,
/// spec/07-backlog.md). Issues already in a Done column keep this Sprint's Id permanently.
/// </summary>
public static class CompleteSprint
{
    public record Request(Guid? MoveIncompleteIssuesToSprintId);

    public record Response(Guid Id, string Status, DateTime? CompletedAtUtc, int CarriedForwardIssueCount);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, Request? request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            if (sprint.Status != SprintStatus.Active)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/invalid-sprint-transition",
                    "Only an Active Sprint can be completed.");
            }

            var targetSprintId = request?.MoveIncompleteIssuesToSprintId;
            if (targetSprintId is not null)
            {
                var targetSprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == targetSprintId, cancellationToken);
                if (targetSprint is null || targetSprint.BoardId != sprint.BoardId || targetSprint.Status != SprintStatus.Planned)
                {
                    return Results.BadRequest(new { detail = "moveIncompleteIssuesToSprintId must reference a Planned Sprint on the same Board." });
                }
            }

            var doneColumnIds = await db.BoardColumns
                .Where(c => c.BoardId == sprint.BoardId && c.IsDoneColumn)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            var incompleteIssueIds = await db.Issues
                .Where(i => i.SprintId == sprintId && i.Type != IssueType.Subtask && !doneColumnIds.Contains(i.BoardColumnId))
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            if (incompleteIssueIds.Count > 0)
            {
                await db.Issues
                    .Where(i => incompleteIssueIds.Contains(i.Id))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, targetSprintId), cancellationToken);

                await db.Issues
                    .Where(i => i.ParentIssueId != null && incompleteIssueIds.Contains(i.ParentIssueId!.Value))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, targetSprintId), cancellationToken);
            }

            sprint.Status = SprintStatus.Completed;
            sprint.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Ok(new Response(sprint.Id, sprint.Status, sprint.CompletedAtUtc, incompleteIssueIds.Count));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sprints/{sprintId:guid}/complete", Handler.Handle)
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
