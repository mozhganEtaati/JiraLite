using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md FR-07, BR-07, BR-08, BR-09. Self-service only, effectively permanent in V1.</summary>
public static class DeactivateAccount
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (user.IsActive)
            {
                user.IsActive = false;
                user.UpdatedAtUtc = DateTime.UtcNow;

                var activeTokens = await db.RefreshTokens
                    .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
                    .ToListAsync(cancellationToken);
                foreach (var token in activeTokens)
                {
                    token.RevokedAtUtc = DateTime.UtcNow;
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/users/me/deactivate", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
