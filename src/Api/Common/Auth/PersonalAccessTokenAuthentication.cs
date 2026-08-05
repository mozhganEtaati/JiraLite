using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace JiraLite.Api.Common.Auth;

public static class PersonalAccessTokenDefaults
{
    /// <summary>
    /// Registered alongside — never instead of — the JWT bearer scheme. `/mcp` requires this
    /// scheme and `/api/*` requires the JWT one, which is what makes the two credential types
    /// non-interchangeable (spec/23-mcp-server.md BR-02).
    /// </summary>
    public const string Scheme = "Pat";
}

/// <summary>
/// Authenticates a Personal Access Token presented as `Authorization: Bearer jlp_...`
/// (spec/23-mcp-server.md FR-04, BR-01, BR-07, BR-08).
///
/// The principal it issues carries the same `sub` claim shape the JWT scheme issues, so every
/// existing authorization handler and <see cref="ClaimsPrincipalExtensions.GetUserId"/> call
/// works against an MCP caller without modification — the credential differs, the identity does not.
/// </summary>
public class PersonalAccessTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    JiraLiteDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// How stale <see cref="Domain.PersonalAccessToken.LastUsedAtUtc"/> may get before it is
    /// refreshed. Purely informational (BR-08), so it is not worth an UPDATE on every single
    /// request an active client makes.
    /// </summary>
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var header = headerValues.ToString();
        const string bearerPrefix = "Bearer ";
        if (!header.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var rawToken = header[bearerPrefix.Length..].Trim();

        // A JWT access token presented here lands on this branch and is rejected (BR-02).
        if (!rawToken.StartsWith(PersonalAccessTokenGenerator.Prefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Not a personal access token.");
        }

        var tokenHash = PersonalAccessTokenGenerator.Hash(rawToken);
        var now = DateTime.UtcNow;

        // BR-07 — the owning User's IsActive is part of the same query, so a deactivated user's
        // tokens stop authenticating without anyone having to revoke them one by one.
        var match = await db.PersonalAccessTokens
            .Where(t => t.TokenHash == tokenHash)
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new
            {
                t.Id,
                t.UserId,
                t.ExpiresAtUtc,
                t.RevokedAtUtc,
                t.LastUsedAtUtc,
                u.Email,
                u.IsActive
            })
            .SingleOrDefaultAsync(Context.RequestAborted);

        if (match is null || match.RevokedAtUtc is not null || match.ExpiresAtUtc <= now || !match.IsActive)
        {
            return AuthenticateResult.Fail("Invalid personal access token.");
        }

        if (match.LastUsedAtUtc is null || now - match.LastUsedAtUtc.Value > LastUsedWriteInterval)
        {
            await db.PersonalAccessTokens
                .Where(t => t.Id == match.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.LastUsedAtUtc, now), Context.RequestAborted);
        }

        // Recorded on every authenticated MCP request so an invocation can always be traced back
        // to a specific credential, not just a user (spec/23-mcp-server.md NFR-02).
        Context.Items[McpCallerContext.TokenIdItemKey] = match.Id;

        var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, match.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, match.Email)
            ],
            PersonalAccessTokenDefaults.Scheme);

        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, PersonalAccessTokenDefaults.Scheme));
    }
}

/// <summary>Carries the authenticating token's id from the auth handler to the logging in the tool layer.</summary>
public static class McpCallerContext
{
    public const string TokenIdItemKey = "JiraLite.PersonalAccessTokenId";

    public static Guid? GetTokenId(HttpContext? httpContext) =>
        httpContext?.Items.TryGetValue(TokenIdItemKey, out var value) == true && value is Guid tokenId
            ? tokenId
            : null;
}
