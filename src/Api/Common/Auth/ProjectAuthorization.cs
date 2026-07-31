using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Project named by "projectId", or Workspace Admin. spec/16-rbac.md BR-02.</summary>
public class ProjectViewRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on "projectId", or Workspace Admin. spec/16-rbac.md BR-02/BR-03.</summary>
public class ProjectManageRequirement : IAuthorizationRequirement;

/// <summary>Caller is WorkspaceMember.Role = Admin on the Workspace owning "projectId" — no ProjectAdmin fallback. spec/05-projects.md BR-07.</summary>
public class ProjectWorkspaceAdminRequirement : IAuthorizationRequirement;

file static class ProjectAuthorizationQueries
{
    public static async Task<Guid?> GetWorkspaceIdAsync(JiraLiteDbContext db, Guid projectId) =>
        await db.Projects.Where(p => p.Id == projectId).Select(p => (Guid?)p.WorkspaceId).SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class ProjectViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<ProjectViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var projectId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "projectId");
        if (projectId is null) return;

        var workspaceId = await ProjectAuthorizationQueries.GetWorkspaceIdAsync(db, projectId.Value);
        if (workspaceId is null)
        {
            // Project doesn't exist — defer to the endpoint handler's own 404 rather than
            // failing closed here, which would incorrectly surface as 403.
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.IsWorkspaceAdminAsync(db, workspaceId.Value, userId))
        {
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.GetProjectRoleAsync(db, projectId.Value, userId) is not null)
        {
            context.Succeed(requirement);
        }
    }
}

public class ProjectManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<ProjectManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var projectId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "projectId");
        if (projectId is null) return;

        var workspaceId = await ProjectAuthorizationQueries.GetWorkspaceIdAsync(db, projectId.Value);
        if (workspaceId is null)
        {
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.IsWorkspaceAdminAsync(db, workspaceId.Value, userId))
        {
            context.Succeed(requirement);
            return;
        }

        var role = await ProjectAuthorizationQueries.GetProjectRoleAsync(db, projectId.Value, userId);
        if (role == ProjectRole.ProjectAdmin)
        {
            context.Succeed(requirement);
        }
    }
}

public class ProjectWorkspaceAdminAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<ProjectWorkspaceAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectWorkspaceAdminRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var projectId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "projectId");
        if (projectId is null) return;

        var workspaceId = await ProjectAuthorizationQueries.GetWorkspaceIdAsync(db, projectId.Value);
        if (workspaceId is null)
        {
            context.Succeed(requirement);
            return;
        }

        if (await ProjectAuthorizationQueries.IsWorkspaceAdminAsync(db, workspaceId.Value, userId))
        {
            context.Succeed(requirement);
        }
    }
}
