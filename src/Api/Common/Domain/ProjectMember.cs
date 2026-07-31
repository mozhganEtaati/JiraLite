namespace JiraLite.Api.Common.Domain;

/// <summary>User↔Project membership with role. spec/18-database.md §6, spec/05-projects.md.</summary>
public class ProjectMember
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid UserId { get; init; }
    public required string Role { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
