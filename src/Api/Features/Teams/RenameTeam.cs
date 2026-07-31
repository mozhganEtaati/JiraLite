using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md FR-01 — Admin only (TeamWorkspaceAdminRequirement).</summary>
public static class RenameTeam
{
    public record Request(string Name, string? Description);

    public record Response(Guid Id, string Name, string? Description, DateTime UpdatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Description).MaximumLength(500);
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

            team.Name = request.Name.Trim();
            team.Description = request.Description?.Trim();
            team.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(team.Id, team.Name, team.Description, team.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/teams/{teamId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("TeamWorkspaceAdmin")
            .WithTags("Teams");
}
