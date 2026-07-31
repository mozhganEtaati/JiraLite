namespace JiraLite.Api.Common.Domain;

/// <summary>spec/09-issues.md BR-01–BR-04 — valid Type/ParentIssueId.Type combinations.</summary>
public static class IssueHierarchyRules
{
    /// <summary>Returns an error message if the combination is invalid, or null if valid.</summary>
    public static string? Validate(string issueType, Guid? parentIssueId, string? parentIssueType)
    {
        switch (issueType)
        {
            case IssueType.Epic:
                return parentIssueId is not null
                    ? "An Epic can never have a parent Issue."
                    : null;

            case IssueType.Story:
            case IssueType.Task:
            case IssueType.Bug:
                if (parentIssueId is null) return null;
                return parentIssueType == IssueType.Epic
                    ? null
                    : "A Story, Task, or Bug may only have an Epic as its parent.";

            case IssueType.Subtask:
                if (parentIssueId is null) return "A Subtask must have a parent Issue.";
                return parentIssueType is IssueType.Story or IssueType.Task or IssueType.Bug
                    ? null
                    : "A Subtask's parent must be a Story, Task, or Bug.";

            default:
                return "Unknown Issue type.";
        }
    }
}
