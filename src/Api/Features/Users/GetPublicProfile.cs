using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>spec/02-users.md FR-06, NFR-03 — never exposes email.</summary>
public static class GetPublicProfile
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid userId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var summary = await db.UserProfiles
                .Where(p => p.UserId == userId)
                .Select(p => new UserSummary(p.UserId, p.DisplayName, p.AvatarUrl))
                .SingleOrDefaultAsync(cancellationToken);

            return summary is null ? Results.NotFound() : Results.Ok(summary);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/{userId:guid}", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
