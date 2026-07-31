namespace JiraLite.Api.Common.Domain;

/// <summary>
/// In-app notification for a User, triggered by events elsewhere in the system.
/// spec/18-database.md §4, spec/13-notifications.md. Content is immutable after creation (BR-04) —
/// only IsRead/ReadAtUtc change afterward.
/// </summary>
public class Notification
{
    public Guid Id { get; init; }
    public Guid RecipientUserId { get; init; }
    public required string Type { get; init; }
    public required string Summary { get; init; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ReadAtUtc { get; set; }
}
