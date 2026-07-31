using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Sprint's Project (resolved via "sprintId"), or Workspace Admin.</summary>
public class SprintViewRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role in (Developer, ProjectAdmin) on the Sprint's Project, or Workspace Admin. spec/08-sprints.md §14.</summary>
public class SprintContributeRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on the Sprint's Project, or Workspace Admin. spec/08-sprints.md §14 (Delete Sprint).</summary>
public class SprintManageRequirement : IAuthorizationRequirement;

file static class SprintAuthorizationQueries
{
    public static async Task<(Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid sprintId) =>
        await db.Sprints
            .Where(s => s.Id == sprintId)
            .Join(db.Projects, s => s.ProjectId, p => p.Id, (s, p) => new { p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid>(x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class SprintViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<SprintViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SprintViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var sprintId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "sprintId");
        if (sprintId is null) return;

        var resolved = await SprintAuthorizationQueries.ResolveAsync(db, sprintId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await SprintAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await SprintAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) is not null) context.Succeed(requirement);
    }
}

public class SprintContributeAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<SprintContributeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SprintContributeRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var sprintId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "sprintId");
        if (sprintId is null) return;

        var resolved = await SprintAuthorizationQueries.ResolveAsync(db, sprintId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await SprintAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        var role = await SprintAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role is ProjectRole.Developer or ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}

public class SprintManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<SprintManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SprintManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var sprintId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "sprintId");
        if (sprintId is null) return;

        var resolved = await SprintAuthorizationQueries.ResolveAsync(db, sprintId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await SprintAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await SprintAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) == ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}
