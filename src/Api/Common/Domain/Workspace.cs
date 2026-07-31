namespace JiraLite.Api.Common.Domain;

/// <summary>Grouping of Teams/Projects within an Organization. spec/18-database.md §5, spec/03-workspaces.md.</summary>
public class Workspace
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
