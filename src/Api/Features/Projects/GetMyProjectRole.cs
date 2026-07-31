using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/16-rbac.md FR-02, BR-02, §9-11 — GET /api/projects/{projectId}/my-role.</summary>
public static class GetMyProjectRole
{
    public record Response(Guid ProjectId, string? EffectiveRole, bool ViaWorkspaceAdmin);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            var workspaceId = await db.Projects
                .Where(p => p.Id == projectId)
                .Select(p => (Guid?)p.WorkspaceId)
                .SingleOrDefaultAsync(cancellationToken);
            if (workspaceId is null)
            {
                return Results.NotFound();
            }

            var isWorkspaceAdmin = await db.WorkspaceMembers.AnyAsync(
                m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin,
                cancellationToken);
            if (isWorkspaceAdmin)
            {
                return Results.Ok(new Response(projectId, WorkspaceRole.Admin, true));
            }

            var role = await db.ProjectMembers
                .Where(m => m.ProjectId == projectId && m.UserId == userId)
                .Select(m => (string?)m.Role)
                .SingleOrDefaultAsync(cancellationToken);

            return Results.Ok(new Response(projectId, role, false));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/my-role", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Projects");
}
