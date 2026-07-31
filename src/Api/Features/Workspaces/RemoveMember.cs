using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>
/// spec/03-workspaces.md FR-06, BR-03, BR-08. Admin-only (self-removal is the separate
/// LeaveWorkspace endpoint). Cascades to Team membership (spec/04-teams.md BR-06).
/// Cascades to ProjectMember (BR-08) now that Project/ProjectMember exist (Phase 3).
/// </summary>
public static class RemoveMember
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            Guid userId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
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
                        "Cannot remove the last remaining Admin of this Workspace.");
                }
            }

            // spec/04-teams.md BR-06: removing a user from a Workspace also removes them from its Teams.
            var teamMemberships = await db.TeamMembers
                .Where(tm => tm.UserId == userId && db.Teams.Any(t => t.Id == tm.TeamId && t.WorkspaceId == workspaceId))
                .ToListAsync(cancellationToken);
            db.TeamMembers.RemoveRange(teamMemberships);

            // spec/03-workspaces.md BR-08: removing a WorkspaceMember cascades to their ProjectMember
            // records within this Workspace's Projects — a user cannot retain project-level access
            // after losing workspace membership. No-op until now (Project/ProjectMember didn't exist
            // before Phase 3 — see the note this comment replaces).
            var projectMemberships = await db.ProjectMembers
                .Where(pm => pm.UserId == userId && db.Projects.Any(p => p.Id == pm.ProjectId && p.WorkspaceId == workspaceId))
                .ToListAsync(cancellationToken);
            db.ProjectMembers.RemoveRange(projectMemberships);

            db.WorkspaceMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/workspaces/{workspaceId:guid}/members/{userId:guid}", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Workspaces");
}
