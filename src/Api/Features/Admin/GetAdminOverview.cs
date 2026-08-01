using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Admin;

/// <summary>spec/17-admin.md FR-01 — Workspace Admin summary counts.</summary>
public static class GetAdminOverview
{
    public record Response(
        Guid WorkspaceId, int MemberCount, int TeamCount, int ProjectCount,
        int ActiveProjectCount, int ArchivedProjectCount, int PendingInvitationCount);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid workspaceId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
            {
                return Results.NotFound();
            }

            var memberCount = await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId, cancellationToken);
            var teamCount = await db.Teams.CountAsync(t => t.WorkspaceId == workspaceId, cancellationToken);
            var activeProjectCount = await db.Projects.CountAsync(p => p.WorkspaceId == workspaceId && !p.IsArchived, cancellationToken);
            var archivedProjectCount = await db.Projects.CountAsync(p => p.WorkspaceId == workspaceId && p.IsArchived, cancellationToken);
            var pendingInvitationCount = await db.Invitations
                .CountAsync(i => i.WorkspaceId == workspaceId && i.Status == InvitationStatus.Pending, cancellationToken);

            return Results.Ok(new Response(
                workspaceId, memberCount, teamCount, activeProjectCount + archivedProjectCount,
                activeProjectCount, archivedProjectCount, pendingInvitationCount));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/admin/overview", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Admin");
}
