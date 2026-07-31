namespace JiraLite.Api.Common.Domain;

/// <summary>
/// Central work-item entity (Epic/Story/Task/Bug/Subtask, distinguished by Type). Status is its
/// current BoardColumnId; backlog position is Rank + SprintId. spec/18-database.md §7, spec/09-issues.md.
/// </summary>
public class Issue
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public int Number { get; init; }
    public required string Key { get; init; }
    public required string Type { get; init; }
    public Guid? ParentIssueId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Priority { get; set; }
    public Guid BoardColumnId { get; set; }
    public Guid? SprintId { get; set; }
    public required string Rank { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public Guid ReporterUserId { get; set; }
    public DateOnly? DueDateUtc { get; set; }
    public decimal? Estimate { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
