using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md §9 GET /api/users/me.</summary>
public static class GetMyProfile
{
    public record Response(Guid Id, string Email, string DisplayName, string? AvatarUrl, DateTime CreatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            var response = await db.Users
                .Where(u => u.Id == userId)
                .Join(db.UserProfiles, u => u.Id, p => p.UserId, (u, p) =>
                    new Response(u.Id, u.Email, p.DisplayName, p.AvatarUrl, u.CreatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);

            return response is null ? Results.NotFound() : Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/me", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
