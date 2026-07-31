namespace JiraLite.Api.Common.Domain;

/// <summary>Sub-grouping of Workspace members. spec/18-database.md §5, spec/04-teams.md.</summary>
public class Team
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
