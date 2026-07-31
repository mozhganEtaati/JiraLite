namespace JiraLite.Api.Common.Domain;

/// <summary>Reusable Project-scoped tag. spec/18-database.md §7, spec/12-labels.md.</summary>
public class Label
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public required string Color { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
