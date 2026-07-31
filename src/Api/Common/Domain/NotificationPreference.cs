namespace JiraLite.Api.Common.Domain;

/// <summary>Per-user notification channel toggles. spec/18-database.md §3, spec/02-users.md.</summary>
public class NotificationPreference
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public bool EmailEnabled { get; set; } = true;
    public bool InAppEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
