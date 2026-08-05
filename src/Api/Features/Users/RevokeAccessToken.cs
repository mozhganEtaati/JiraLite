using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>
/// spec/23-mcp-server.md FR-02, BR-04 — revocation is immediate and irreversible; the user
/// creates a new token rather than reactivating this one.
/// </summary>
public static class RevokeAccessToken
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid tokenId,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            // 404 rather than 403 for another user's token id — the same reasoning as
            // spec/13-notifications.md §13: a 403 would confirm the id exists.
            var token = await db.PersonalAccessTokens
                .SingleOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, cancellationToken);
            if (token is null)
            {
                return Results.NotFound();
            }

            // Idempotent: revoking an already-revoked token keeps the original revocation time.
            if (token.RevokedAtUtc is null)
            {
                token.RevokedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/users/me/tokens/{tokenId:guid}", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
