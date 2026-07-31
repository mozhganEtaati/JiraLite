using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller holds ProjectMember.Role = ProjectAdmin on the Label's Project (resolved via "labelId"), or Workspace Admin. spec/12-labels.md §14 — edit/delete Label definitions.</summary>
public class LabelManageRequirement : IAuthorizationRequirement;

file static class LabelAuthorizationQueries
{
    public static async Task<(Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid labelId) =>
        await db.Labels
            .Where(l => l.Id == labelId)
            .Join(db.Projects, l => l.ProjectId, p => p.Id, (l, p) => new { p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid>(x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class LabelManageAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<LabelManageRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, LabelManageRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var labelId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "labelId");
        if (labelId is null) return;

        var resolved = await LabelAuthorizationQueries.ResolveAsync(db, labelId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await LabelAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await LabelAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) == ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}
