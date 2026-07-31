using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>
/// Caller must be the Organization.OwnerUserId of the Organization named by the "orgId" route value.
/// spec/03-workspaces.md BR-01 — deliberately separate from WorkspaceAdminRequirement: Organization
/// ownership and Workspace-Admin role are distinct, simpler mechanisms (spec/16-rbac.md).
/// </summary>
public class OrganizationOwnerRequirement : IAuthorizationRequirement;

public class OrganizationOwnerAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<OrganizationOwnerRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OrganizationOwnerRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return;
        }

        var orgId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "orgId");
        if (orgId is null)
        {
            return;
        }

        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == orgId);
        if (organization is null)
        {
            // Organization doesn't exist — defer to the endpoint handler's own 404 rather than
            // failing closed here, which would incorrectly surface as 403.
            context.Succeed(requirement);
            return;
        }

        if (organization.OwnerUserId == userId)
        {
            context.Succeed(requirement);
        }
    }
}
