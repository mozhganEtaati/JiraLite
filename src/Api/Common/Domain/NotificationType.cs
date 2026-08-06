namespace JiraLite.Api.Common.Domain;

/// <summary>spec/13-notifications.md §7 — Notification.Type values.</summary>
public static class NotificationType
{
    public const string IssueAssigned = "IssueAssigned";
    public const string IssueStatusChanged = "IssueStatusChanged";
    public const string CommentAdded = "CommentAdded";
    public const string IssueBlocked = "IssueBlocked";
    public const string IssueUnblocked = "IssueUnblocked";
}
