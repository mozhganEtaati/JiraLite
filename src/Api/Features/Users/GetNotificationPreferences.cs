using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md §9 GET /api/users/me/notification-preferences.</summary>
public static class GetNotificationPreferences
{
    public record Response(bool EmailEnabled, bool InAppEnabled);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var preference = await db.NotificationPreferences
                .Where(p => p.UserId == userId)
                .Select(p => new Response(p.EmailEnabled, p.InAppEnabled))
                .SingleOrDefaultAsync(cancellationToken);

            return preference is null ? Results.NotFound() : Results.Ok(preference);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/me/notification-preferences", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
