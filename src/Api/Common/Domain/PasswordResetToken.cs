namespace JiraLite.Api.Common.Domain;

/// <summary>
/// Single-use credential emailed to a user who cannot log in. spec/18-database.md §3,
/// spec/01-authentication.md FR-06, FR-07, BR-09–BR-12.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>
    /// Whether the token can still be redeemed. Says nothing about the owning User — BR-12
    /// (a deactivated owner cannot reset) is resolved in the handler, which is the only place
    /// that has loaded the User to check.
    /// </summary>
    public bool IsActive => UsedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}
