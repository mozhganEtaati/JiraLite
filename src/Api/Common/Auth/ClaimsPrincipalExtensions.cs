using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JiraLite.Api.Common.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Extracts the caller's UserId from the access token's sub claim (spec/01-authentication.md BR-06).</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        principal.TryGetUserId(out var userId)
            ? userId
            : throw new InvalidOperationException("Access token is missing a valid subject claim.");

    /// <summary>Non-throwing variant for use inside authorization handlers, where the principal may be anonymous.</summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out userId);
    }
}
