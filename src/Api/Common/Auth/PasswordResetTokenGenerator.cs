using System.Security.Cryptography;
using System.Text;

namespace JiraLite.Api.Common.Auth;

/// <summary>
/// Generates and hashes password reset tokens (spec/01-authentication.md NFR-05).
///
/// Same reasoning as <see cref="PersonalAccessTokenGenerator"/>: SHA-256 rather than
/// <see cref="Pbkdf2PasswordHasher"/>, because redeeming a presented token means looking it up by
/// hash, which a per-row salt makes impossible — and the slow-KDF protection buys nothing over 256
/// bits of CSPRNG output. Lowercase hex so the value survives a round trip through a URL and an
/// email client untouched.
/// </summary>
public static class PasswordResetTokenGenerator
{
    public static (string RawToken, string TokenHash) Create()
    {
        var rawToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return (rawToken, Hash(rawToken));
    }

    public static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
