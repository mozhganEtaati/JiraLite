namespace JiraLite.Api.Common.Domain;

/// <summary>Time-boxed iteration on a Scrum Board. spec/18-database.md §6, spec/08-sprints.md.</summary>
public class Sprint
{
    public Guid Id { get; init; }
    public Guid BoardId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public string? Goal { get; set; }
    public required string Status { get; set; }
    public DateOnly PlannedStartDateUtc { get; set; }
    public DateOnly PlannedEndDateUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    /// <summary>Issues carried out of the Sprint on completion (BR-05). Null on Sprints completed before this was recorded.</summary>
    public int? CarriedForwardIssueCount { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
