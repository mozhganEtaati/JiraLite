using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md FR-01.</summary>
public static class CreateTeam
{
    public record Request(string Name, string? Description);

    public record Response(Guid Id, Guid WorkspaceId, string Name, string? Description, DateTime CreatedAtUtc);

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
            Guid workspaceId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
            {
                return Results.NotFound();
            }

            var now = DateTime.UtcNow;
            var team = new Team
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                CreatedByUserId = caller.GetUserId(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/teams/{team.Id}",
                new Response(team.Id, team.WorkspaceId, team.Name, team.Description, team.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/workspaces/{workspaceId:guid}/teams", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Teams");
}
