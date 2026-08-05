namespace JiraLite.Api.Common.Domain;

/// <summary>
/// Long-lived machine credential for MCP clients, which cannot participate in the short-lived
/// access/refresh exchange. spec/18-database.md §3, spec/23-mcp-server.md §7, BR-02–BR-05, BR-08.
/// </summary>
public class PersonalAccessToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string Name { get; init; }
    public required string TokenHash { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Whether the token itself is usable. Deliberately says nothing about the owning User —
    /// BR-07 (a deactivated owner's tokens stop working) is resolved in the authentication query,
    /// which is the only place that has the User to check.
    /// </summary>
    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}
