using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md FR-01, BR-05 — cascades TeamMember only.</summary>
public static class DeleteTeam
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid teamId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
            if (team is null)
            {
                return Results.NotFound();
            }

            db.Teams.Remove(team); // TeamMember rows cascade at the database level (Team -> TeamMember, ON DELETE CASCADE).
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/teams/{teamId:guid}", Handler.Handle)
            .RequireAuthorization("TeamWorkspaceAdmin")
            .WithTags("Teams");
}
