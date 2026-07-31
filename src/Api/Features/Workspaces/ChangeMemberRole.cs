using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-06, BR-03 — last-Admin guard.</summary>
public static class ChangeMemberRole
{
    public record Request(string Role);

    public record Response(Guid UserId, string Role);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Role).NotEmpty().Must(r => WorkspaceRole.All.Contains(r))
                .WithMessage($"Role must be one of: {string.Join(", ", WorkspaceRole.All)}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            Guid userId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var member = await db.WorkspaceMembers
                .SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            if (member.Role == WorkspaceRole.Admin && request.Role != WorkspaceRole.Admin)
            {
                var otherAdminExists = await db.WorkspaceMembers.AnyAsync(
                    m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Admin && m.UserId != userId,
                    cancellationToken);
                if (!otherAdminExists)
                {
                    return ProblemResults.Conflict(
                        "https://jiralite.dev/errors/last-admin",
                        "Cannot demote the last remaining Admin of this Workspace.");
                }
            }

            member.Role = request.Role;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(member.UserId, member.Role));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/workspaces/{workspaceId:guid}/members/{userId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Workspaces");
}
