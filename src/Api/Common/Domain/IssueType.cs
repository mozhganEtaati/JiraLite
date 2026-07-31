namespace JiraLite.Api.Common.Domain;

/// <summary>spec/09-issues.md — Issue.Type values; immutable after creation (BR-09).</summary>
public static class IssueType
{
    public const string Epic = "Epic";
    public const string Story = "Story";
    public const string Task = "Task";
    public const string Bug = "Bug";
    public const string Subtask = "Subtask";

    public static readonly string[] All = [Epic, Story, Task, Bug, Subtask];
}
