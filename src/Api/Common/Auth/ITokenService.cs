using JiraLite.Api.Common.Domain;

namespace JiraLite.Api.Common.Auth;

public interface ITokenService
{
    /// <summary>Issues a short-lived JWT carrying identity claims only (sub, email) — no roles/permissions (BR-06).</summary>
    (string AccessToken, DateTime ExpiresAtUtc) CreateAccessToken(User user);

    /// <summary>Generates a new cryptographically random refresh token and its storable hash.</summary>
    (string RawToken, string TokenHash, DateTime ExpiresAtUtc) CreateRefreshToken();

    /// <summary>Hashes a raw refresh token the same way as CreateRefreshToken, for lookup by presented value.</summary>
    string HashRefreshToken(string rawToken);
}
