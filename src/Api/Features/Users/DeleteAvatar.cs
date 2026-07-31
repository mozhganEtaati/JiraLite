using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md §13 — idempotent, 204 whether or not an avatar existed.</summary>
public static class DeleteAvatar
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            IFileStorage fileStorage,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (profile is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrEmpty(profile.AvatarStorageKey))
            {
                await fileStorage.DeleteAsync(profile.AvatarStorageKey, cancellationToken);
                profile.AvatarStorageKey = null;
                profile.AvatarUrl = null;
                profile.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/users/me/avatar", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
