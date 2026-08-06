using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Notifications;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>
/// The telling half of blocking and unblocking, shared by both slices: the assignee and the
/// reporter hear about it (spec/13-notifications.md FR-02 — a blocker nobody is told about is a
/// blocker nobody clears), and the Workspace feed records it (spec/02-users.md BR-05).
/// Neither saves — both are staged on the caller's own DbContext so they commit with the Issue.
/// </summary>
internal static class IssueBlockNotices
{
    public static async Task NotifyAsync(
        JiraLiteDbContext db,
        NotificationDispatcher notificationDispatcher,
        Issue issue,
        Guid actorUserId,
        string notificationType,
        string notificationSummary,
        string activityAction,
        string activitySummary,
        CancellationToken cancellationToken)
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
                notificationType,
                notificationSummary,
                "Issue",
                issue.Id,
                cancellationToken);
        }

        var workspaceId = await db.Projects
            .Where(p => p.Id == issue.ProjectId)
            .Select(p => p.WorkspaceId)
            .SingleAsync(cancellationToken);

        // "Blocked"/"Unblocked" are new Action values. GetMyStats counts Created/StatusChanged/
        // Commented by name (spec/14-dashboard.md BR-08), so the streak chart is unaffected.
        db.ActivityLogEntries.Add(new ActivityLogEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            WorkspaceId = workspaceId,
            ProjectId = issue.ProjectId,
            EntityType = "Issue",
            EntityId = issue.Id,
            Action = activityAction,
            Summary = activitySummary,
            OccurredAtUtc = issue.UpdatedAtUtc
        });
    }
}
