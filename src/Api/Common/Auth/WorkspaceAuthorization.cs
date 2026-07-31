using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller must be any WorkspaceMember (Admin or Member) of the Workspace named by the "workspaceId" route value.</summary>
public class WorkspaceMemberRequirement : IAuthorizationRequirement;

/// <summary>Caller must hold WorkspaceMember.Role = Admin on the Workspace named by the "workspaceId" route value. spec/16-rbac.md §14.</summary>
public class WorkspaceAdminRequirement : IAuthorizationRequirement;

public class WorkspaceMemberAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<WorkspaceMemberRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceMemberRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return;
        }

        var workspaceId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "workspaceId");
        if (workspaceId is null)
        {
            return;
        }

        // Workspace doesn't exist — defer to the endpoint handler's own 404 rather than failing
        // closed here, which would incorrectly surface as 403.
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            context.Succeed(requirement);
            return;
        }

        var isMember = await db.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId);
        if (isMember)
        {
            context.Succeed(requirement);
        }
    }
}

public class WorkspaceAdminAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<WorkspaceAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceAdminRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return;
        }

        var workspaceId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "workspaceId");
        if (workspaceId is null)
        {
            return;
        }

        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            context.Succeed(requirement);
            return;
        }

        var isAdmin = await db.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);
        if (isAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
