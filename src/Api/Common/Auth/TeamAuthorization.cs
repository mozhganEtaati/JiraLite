using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>
/// Caller must be a WorkspaceMember of the Team's Workspace, resolved from the "teamId" route
/// value since Team routes are flat (spec/04-teams.md §9) — any Workspace member may view any
/// Team, regardless of personal Team membership (spec/16-rbac.md §14 Team-scoped table).
/// </summary>
public class TeamViewRequirement : IAuthorizationRequirement;

/// <summary>
/// Caller must be WorkspaceMember.Role = Admin OR TeamMember.IsLead = true on the Team named by
/// the "teamId" route value. spec/04-teams.md §14 (Add/remove Team members, toggle IsLead).
/// </summary>
public class TeamManagementRequirement : IAuthorizationRequirement;

/// <summary>
/// Caller must be WorkspaceMember.Role = Admin on the Team's Workspace, resolved from the
/// "teamId" route value. spec/04-teams.md §14 (Create/rename/delete Team — Admin only, no Lead fallback).
/// </summary>
public class TeamWorkspaceAdminRequirement : IAuthorizationRequirement;

file static class TeamAuthorizationQueries
{
    public static async Task<Guid?> GetWorkspaceIdAsync(JiraLiteDbContext db, Guid teamId) =>
        await db.Teams.Where(t => t.Id == teamId).Select(t => (Guid?)t.WorkspaceId).SingleOrDefaultAsync();
}

public class TeamViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<TeamViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TeamViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return;
        }

        var teamId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "teamId");
        if (teamId is null)
        {
            return;
        }

        var workspaceId = await TeamAuthorizationQueries.GetWorkspaceIdAsync(db, teamId.Value);
        if (workspaceId is null)
        {
            // Team doesn't exist — defer to the endpoint handler's own 404 (spec/04-teams.md §13)
            // rather than failing closed here, which would incorrectly surface as 403.
            context.Succeed(requirement);
            return;
        }

        var isMember = await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId);
        if (isMember)
        {
            context.Succeed(requirement);
        }
    }
}

public class TeamWorkspaceAdminAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<TeamWorkspaceAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TeamWorkspaceAdminRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return;
        }

        var teamId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "teamId");
        if (teamId is null)
        {
            return;
        }

        var workspaceId = await TeamAuthorizationQueries.GetWorkspaceIdAsync(db, teamId.Value);
        if (workspaceId is null)
        {
            // Team doesn't exist — defer to the endpoint handler's own 404 (spec/04-teams.md §13)
            // rather than failing closed here, which would incorrectly surface as 403.
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

public class TeamManagementAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<TeamManagementRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TeamManagementRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return;
        }

        var teamId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "teamId");
        if (teamId is null)
        {
            return;
        }

        var workspaceId = await TeamAuthorizationQueries.GetWorkspaceIdAsync(db, teamId.Value);
        if (workspaceId is null)
        {
            // Team doesn't exist — defer to the endpoint handler's own 404 (spec/04-teams.md §13)
            // rather than failing closed here, which would incorrectly surface as 403.
            context.Succeed(requirement);
            return;
        }

        var isAdmin = await db.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);
        if (isAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        var isLead = await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId && m.IsLead);
        if (isLead)
        {
            context.Succeed(requirement);
        }
    }
}
