using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Notifications;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>
/// spec/09-issues.md FR-04, BR-10, BR-11. Moving onto a Kanban Board's column clears SprintId
/// (BR-10) and cascades that clear to the moved Issue's Subtasks (BR-11) in the same transaction —
/// ExecuteUpdateAsync runs immediately rather than at SaveChanges, so both the subtask clear and
/// the concurrency-checked Issue update must commit or roll back together.
/// </summary>
public static class MoveIssue
{
    public record Request(Guid BoardColumnId, string RowVersion);

    public record Response(Guid Id, Guid BoardColumnId, Guid? SprintId, string RowVersion);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.BoardColumnId).NotEmpty();
            RuleFor(x => x.RowVersion).NotEmpty();
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid issueId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            NotificationDispatcher notificationDispatcher,
            CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var targetColumn = await db.BoardColumns
                .Where(c => c.Id == request.BoardColumnId)
                .Join(db.Boards, c => c.BoardId, b => b.Id, (c, b) => new { Column = c, Board = b })
                .SingleOrDefaultAsync(cancellationToken);
            if (targetColumn is null || targetColumn.Board.ProjectId != issue.ProjectId)
            {
                return Results.BadRequest(new { detail = "boardColumnId must belong to a Board in the same Project as the Issue." });
            }

            byte[] originalRowVersion;
            try
            {
                originalRowVersion = Convert.FromBase64String(request.RowVersion);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { detail = "rowVersion is not valid base64." });
            }

            db.Entry(issue).Property(i => i.RowVersion).OriginalValue = originalRowVersion;

            var previousColumnId = issue.BoardColumnId;
            var actorUserId = caller.GetUserId();

            issue.BoardColumnId = targetColumn.Column.Id;
            issue.UpdatedByUserId = actorUserId;
            issue.UpdatedAtUtc = DateTime.UtcNow;

            // spec/09-issues.md BR-17 — an Issue in a Done column cannot be blocked. Blocking is
            // refused there, so finishing blocked work must clear the flag too; otherwise the
            // rule holds only against the endpoint and the card stays marked Blocked forever.
            if (targetColumn.Column.IsDoneColumn && issue.IsBlocked)
            {
                issue.IsBlocked = false;
                issue.BlockedReason = null;
                issue.BlockedSinceUtc = null;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            if (targetColumn.Board.Type == BoardType.Kanban)
            {
                issue.SprintId = null;
                await db.Issues
                    .Where(i => i.ParentIssueId == issue.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, (Guid?)null), cancellationToken);
            }

            // spec/13-notifications.md FR-02: assignee and reporter, excluding whoever moved it.
            if (previousColumnId != issue.BoardColumnId)
            {
                var recipients = new HashSet<Guid> { issue.ReporterUserId };
                if (issue.AssigneeUserId is not null)
                {
                    recipients.Add(issue.AssigneeUserId.Value);
                }

                foreach (var recipientUserId in recipients)
                {
                    await notificationDispatcher.NotifyAsync(
                        recipientUserId,
                        actorUserId,
                        NotificationType.IssueStatusChanged,
                        $"{issue.Key} moved to {targetColumn.Column.Name}",
                        "Issue",
                        issue.Id,
                        cancellationToken);
                }

                // spec/02-users.md BR-05/BR-06 — a representative Activity entry for Phase 4 (named
                // explicitly as an example handler in BR-05).
                var workspaceId = await db.Projects
                    .Where(p => p.Id == issue.ProjectId)
                    .Select(p => p.WorkspaceId)
                    .SingleAsync(cancellationToken);
                db.ActivityLogEntries.Add(new ActivityLogEntry
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = actorUserId,
                    WorkspaceId = workspaceId,
                    ProjectId = issue.ProjectId,
                    EntityType = "Issue",
                    EntityId = issue.Id,
                    Action = "StatusChanged",
                    Summary = $"moved Issue {issue.Key} to {targetColumn.Column.Name}",
                    OccurredAtUtc = issue.UpdatedAtUtc
                });
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/concurrency-conflict",
                    "This Issue was modified since you last loaded it. Reload and try again.");
            }

            await transaction.CommitAsync(cancellationToken);

            return Results.Ok(new Response(issue.Id, issue.BoardColumnId, issue.SprintId, Convert.ToBase64String(issue.RowVersion)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/issues/{issueId:guid}/move", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Issues");
}
