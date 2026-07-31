using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md FR-04, BR-04.</summary>
public static class UpdateNotificationPreferences
{
    public record Request(bool EmailEnabled, bool InAppEnabled);

    public record Response(bool EmailEnabled, bool InAppEnabled);

    public class Validator : AbstractValidator<Request>
    {
        // EmailEnabled/InAppEnabled are non-nullable bool — always present once bound; no further rules needed.
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var preference = await db.NotificationPreferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (preference is null)
            {
                return Results.NotFound();
            }

            preference.EmailEnabled = request.EmailEnabled;
            preference.InAppEnabled = request.InAppEnabled;
            preference.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(preference.EmailEnabled, preference.InAppEnabled));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/users/me/notification-preferences", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization()
            .WithTags("Users");
}
