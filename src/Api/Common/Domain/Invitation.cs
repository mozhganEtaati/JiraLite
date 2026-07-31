namespace JiraLite.Api.Common.Domain;

/// <summary>Pending offer to join a Workspace. spec/18-database.md §5, spec/03-workspaces.md.</summary>
public class Invitation
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required string Token { get; init; }
    public required string Status { get; set; }
    public Guid InvitedByUserId { get; init; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid? AcceptedByUserId { get; set; }
}
