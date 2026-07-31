namespace JiraLite.Api.Common.Domain;

/// <summary>User↔Team membership. spec/18-database.md §5, spec/04-teams.md.</summary>
public class TeamMember
{
    public Guid Id { get; init; }
    public Guid TeamId { get; init; }
    public Guid UserId { get; init; }
    public bool IsLead { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
