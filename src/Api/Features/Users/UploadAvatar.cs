using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md FR-03, BR-02, BR-03, §12 validation.</summary>
public static class UploadAvatar
{
    private static readonly HashSet<string> AllowedContentTypes = ["image/png", "image/jpeg", "image/webp"];
    private const long MaxSizeBytes = 5 * 1024 * 1024;

    public record Response(string AvatarUrl);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            IFormFile file,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            IFileStorage fileStorage,
            CancellationToken cancellationToken)
        {
            if (!AllowedContentTypes.Contains(file.ContentType))
            {
                return Results.Problem(
                    type: "https://jiralite.dev/errors/unsupported-avatar-type",
                    title: "Bad Request",
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: "Avatar must be image/png, image/jpeg, or image/webp.");
            }

            if (file.Length > MaxSizeBytes)
            {
                return Results.Problem(
                    type: "https://jiralite.dev/errors/avatar-too-large",
                    title: "Payload Too Large",
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    detail: "Avatar exceeds the 5 MB size limit.");
            }

            var userId = caller.GetUserId();
            var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (profile is null)
            {
                return Results.NotFound();
            }

            var previousStorageKey = profile.AvatarStorageKey;

            await using var stream = file.OpenReadStream();
            var storageKey = await fileStorage.SaveAsync("avatars", file.FileName, stream, cancellationToken);

            profile.AvatarStorageKey = storageKey;
            profile.AvatarUrl = fileStorage.GetPublicUrl(storageKey);
            profile.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrEmpty(previousStorageKey))
            {
                await fileStorage.DeleteAsync(previousStorageKey, cancellationToken);
            }

            return Results.Ok(new Response(profile.AvatarUrl));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/users/me/avatar", Handler.Handle)
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .RequireAuthorization()
            .WithTags("Users");
}
