using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-10, BR-10 — dedicated verb-route, any WorkspaceMember, last-Admin guard applies.</summary>
public static class LeaveWorkspace
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            var member = await db.WorkspaceMembers
                .SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            if (member.Role == WorkspaceRole.Admin)
            {
                var otherAdminExists = await db.WorkspaceMembers.AnyAsync(
                    m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Admin && m.UserId != userId,
                    cancellationToken);
                if (!otherAdminExists)
                {
                    return ProblemResults.Conflict(
                        "https://jiralite.dev/errors/last-admin",
                        "The sole remaining Admin cannot leave — promote another member to Admin first.");
                }
            }

            var teamMemberships = await db.TeamMembers
                .Where(tm => tm.UserId == userId && db.Teams.Any(t => t.Id == tm.TeamId && t.WorkspaceId == workspaceId))
                .ToListAsync(cancellationToken);
            db.TeamMembers.RemoveRange(teamMemberships);

            db.WorkspaceMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/workspaces/{workspaceId:guid}/leave", Handler.Handle)
            .RequireAuthorization("WorkspaceMember")
            .WithTags("Workspaces");
}
