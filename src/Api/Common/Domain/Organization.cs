namespace JiraLite.Api.Common.Domain;

/// <summary>Top-level tenant boundary. spec/18-database.md §5, spec/03-workspaces.md.</summary>
public class Organization
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public Guid OwnerUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
