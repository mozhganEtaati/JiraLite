using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md FR-03, BR-02 — zero, one, or multiple Leads allowed.</summary>
public static class SetTeamLead
{
    public record Request(bool IsLead);

    public record Response(Guid UserId, bool IsLead);

    public class Validator : AbstractValidator<Request>;

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid teamId,
            Guid userId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var member = await db.TeamMembers
                .SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            member.IsLead = request.IsLead;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(member.UserId, member.IsLead));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/teams/{teamId:guid}/members/{userId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("TeamManagement")
            .WithTags("Teams");
}
