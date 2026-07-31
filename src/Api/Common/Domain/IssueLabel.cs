namespace JiraLite.Api.Common.Domain;

/// <summary>Issue↔Label join (composite PK, no audit fields). spec/18-database.md §7, spec/12-labels.md.</summary>
public class IssueLabel
{
    public Guid IssueId { get; init; }
    public Guid LabelId { get; init; }
}
