namespace JiraLite.Api.Common.Domain;

/// <summary>User↔Workspace membership with role. spec/18-database.md §5, spec/03-workspaces.md BR-02.</summary>
public class WorkspaceMember
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public Guid UserId { get; init; }
    public required string Role { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
