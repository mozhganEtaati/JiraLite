using System.Security.Cryptography;
using System.Text;

namespace JiraLite.Api.Common.Auth;

/// <summary>
/// Generates and hashes Personal Access Tokens (spec/23-mcp-server.md NFR-01).
///
/// Hashing is SHA-256, matching <see cref="JwtTokenService.HashRefreshToken"/> and deliberately
/// not <see cref="Pbkdf2PasswordHasher"/>: a password hasher salts each row, which makes lookup
/// by hash impossible — and lookup by hash is exactly what authenticating a presented token
/// requires. The slow-KDF protection a password hasher buys is also pointless here, because the
/// value being hashed is 256 bits of CSPRNG output rather than a human-chosen password.
/// </summary>
public static class PersonalAccessTokenGenerator
{
    /// <summary>Distinguishes a Personal Access Token from a JWT at a glance, and makes a leaked token greppable in logs and repositories.</summary>
    public const string Prefix = "jlp_";

    public static (string RawToken, string TokenHash) Create()
    {
        var rawToken = Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return (rawToken, Hash(rawToken));
    }

    public static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
