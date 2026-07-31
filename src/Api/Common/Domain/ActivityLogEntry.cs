namespace JiraLite.Api.Common.Domain;

/// <summary>
/// Immutable, append-only record of platform actions. spec/18-database.md §4, spec/02-users.md.
/// Written only by feature handlers (BR-05) — no endpoint creates/edits/deletes these directly.
/// </summary>
public class ActivityLogEntry
{
    public Guid Id { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid WorkspaceId { get; init; }
    public Guid? ProjectId { get; set; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public required string Action { get; init; }
    public required string Summary { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}
