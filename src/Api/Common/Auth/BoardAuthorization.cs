using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Board's Project (resolved via "boardId"), or Workspace Admin.</summary>
public class BoardViewRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on the Board's Project, or Workspace Admin. spec/06-boards.md §14.</summary>
public class BoardManageRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role in (Developer, ProjectAdmin) on the Board's Project, or Workspace Admin. spec/08-sprints.md §14 (Create Sprint is boardId-routed).</summary>
public class BoardContributeRequirement : IAuthorizationRequirement;

file static class BoardAuthorizationQueries
{
    public static async Task<(Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid boardId) =>
        await db.Boards
            .Where(b => b.Id == boardId)
            .Join(db.Projects, b => b.ProjectId, p => p.Id, (b, p) => new { p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid>(x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class BoardViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<BoardViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BoardViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var boardId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "boardId");
        if (boardId is null) return;

        var resolved = await BoardAuthorizationQueries.ResolveAsync(db, boardId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await BoardAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await BoardAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) is not null) context.Succeed(requirement);
    }
}

public class BoardManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<BoardManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BoardManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var boardId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "boardId");
        if (boardId is null) return;

        var resolved = await BoardAuthorizationQueries.ResolveAsync(db, boardId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await BoardAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await BoardAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) == ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}

public class BoardContributeAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<BoardContributeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BoardContributeRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var boardId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "boardId");
        if (boardId is null) return;

        var resolved = await BoardAuthorizationQueries.ResolveAsync(db, boardId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await BoardAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        var role = await BoardAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role is ProjectRole.Developer or ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}
