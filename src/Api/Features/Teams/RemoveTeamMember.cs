using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md FR-02.</summary>
public static class RemoveTeamMember
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid teamId,
            Guid userId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var member = await db.TeamMembers
                .SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            db.TeamMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/teams/{teamId:guid}/members/{userId:guid}", Handler.Handle)
            .RequireAuthorization("TeamManagement")
            .WithTags("Teams");
}
