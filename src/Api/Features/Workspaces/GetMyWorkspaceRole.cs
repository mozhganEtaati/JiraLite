using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/16-rbac.md FR-02, §9 — GET /api/workspaces/{workspaceId}/my-role.</summary>
public static class GetMyWorkspaceRole
{
    public record Response(Guid WorkspaceId, string? EffectiveRole);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            var role = await db.WorkspaceMembers
                .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId)
                .Select(m => (string?)m.Role)
                .SingleOrDefaultAsync(cancellationToken);

            return Results.Ok(new Response(workspaceId, role));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/my-role", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Workspaces");
}
