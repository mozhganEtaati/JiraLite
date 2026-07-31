namespace JiraLite.Api.Common.Domain;

/// <summary>Platform-level identity and credentials. spec/18-database.md §3.</summary>
public class User
{
    public Guid Id { get; init; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
