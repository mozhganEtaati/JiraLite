using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md FR-02.</summary>
public static class UpdateMyProfile
{
    public record Request(string DisplayName);

    public record Response(Guid Id, string DisplayName, DateTime UpdatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        }
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
            var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (profile is null)
            {
                return Results.NotFound();
            }

            profile.DisplayName = request.DisplayName.Trim();
            profile.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(profile.UserId, profile.DisplayName, profile.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/users/me", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization()
            .WithTags("Users");
}
