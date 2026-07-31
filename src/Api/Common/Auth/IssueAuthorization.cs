using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Issue's Project (resolved via "issueId"), or Workspace Admin. spec/09-issues.md §14.</summary>
public class IssueViewRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role in (Developer, ProjectAdmin) on the Issue's Project, or Workspace Admin. spec/09-issues.md §14 — most Issue-scoped writes (edit, move, rank, comments, attachments, labels).</summary>
public class IssueContributeRequirement : IAuthorizationRequirement;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on the Issue's Project, or Workspace Admin. spec/09-issues.md §14 — Delete Issue, Reporter reassignment.</summary>
public class IssueManageRequirement : IAuthorizationRequirement;

file static class IssueAuthorizationQueries
{
    public static async Task<(Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid issueId) =>
        await db.Issues
            .Where(i => i.Id == issueId)
            .Join(db.Projects, i => i.ProjectId, p => p.Id, (i, p) => new { p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid>(x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class IssueViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<IssueViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IssueViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var issueId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "issueId");
        if (issueId is null) return;

        var resolved = await IssueAuthorizationQueries.ResolveAsync(db, issueId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await IssueAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await IssueAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) is not null) context.Succeed(requirement);
    }
}

public class IssueContributeAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<IssueContributeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IssueContributeRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var issueId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "issueId");
        if (issueId is null) return;

        var resolved = await IssueAuthorizationQueries.ResolveAsync(db, issueId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await IssueAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        var role = await IssueAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role is ProjectRole.Developer or ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}

public class IssueManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<IssueManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, IssueManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var issueId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "issueId");
        if (issueId is null) return;

        var resolved = await IssueAuthorizationQueries.ResolveAsync(db, issueId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await IssueAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await IssueAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) == ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}
