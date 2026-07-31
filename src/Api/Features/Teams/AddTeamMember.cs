using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md FR-02, BR-01 — target must already be a WorkspaceMember of the same Workspace.</summary>
public static class AddTeamMember
{
    public record Request(Guid UserId);

    public record Response(Guid UserId, bool IsLead);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.UserId).NotEmpty();
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid teamId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
            if (team is null)
            {
                return Results.NotFound();
            }

            var isWorkspaceMember = await db.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == team.WorkspaceId && m.UserId == request.UserId, cancellationToken);
            if (!isWorkspaceMember)
            {
                return Results.Problem(
                    type: "https://jiralite.dev/errors/not-a-workspace-member",
                    title: "Bad Request",
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: "The user must be a Workspace member before joining a Team.");
            }

            var alreadyOnTeam = await db.TeamMembers
                .AnyAsync(m => m.TeamId == teamId && m.UserId == request.UserId, cancellationToken);
            if (alreadyOnTeam)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/already-on-team",
                    "This user is already on the Team.");
            }

            var member = new TeamMember
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                UserId = request.UserId,
                IsLead = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.TeamMembers.Add(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/teams/{teamId}/members/{member.UserId}", new Response(member.UserId, member.IsLead));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/teams/{teamId:guid}/members", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("TeamManagement")
            .WithTags("Teams");
}
