using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md §9, §11 — GET /api/teams/{teamId}, includes member list.</summary>
public static class GetTeam
{
    public record MemberItem(Guid UserId, string DisplayName, string? AvatarUrl, bool IsLead, DateTime JoinedAtUtc);

    public record Response(Guid Id, Guid WorkspaceId, string Name, string? Description, IReadOnlyList<MemberItem> Members);

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

            // Order on the join's raw columns before projecting into the record — ordering by a
            // property of a record constructed in the Join projection does not translate to SQL.
            var members = await db.TeamMembers
                .Where(m => m.TeamId == teamId)
                .Join(db.UserProfiles, m => m.UserId, p => p.UserId, (m, p) => new { m, p })
                .OrderBy(x => x.p.DisplayName)
                .Select(x => new MemberItem(x.m.UserId, x.p.DisplayName, x.p.AvatarUrl, x.m.IsLead, x.m.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(team.Id, team.WorkspaceId, team.Name, team.Description, members));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/teams/{teamId:guid}", Handler.Handle)
            .RequireAuthorization("TeamView")
            .WithTags("Teams");
}
