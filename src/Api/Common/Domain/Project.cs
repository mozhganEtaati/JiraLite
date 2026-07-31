namespace JiraLite.Api.Common.Domain;

/// <summary>Container for Boards/Sprints/Issues within a Workspace. spec/18-database.md §6, spec/05-projects.md.</summary>
public class Project
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public required string Key { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
