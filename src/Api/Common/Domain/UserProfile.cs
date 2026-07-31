namespace JiraLite.Api.Common.Domain;

/// <summary>Display identity for a User. spec/18-database.md §3, spec/02-users.md.</summary>
public class UserProfile
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? AvatarStorageKey { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
