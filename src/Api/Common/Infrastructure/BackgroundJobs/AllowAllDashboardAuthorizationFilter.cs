using Hangfire.Dashboard;

namespace JiraLite.Api.Common.Infrastructure.BackgroundJobs;

/// <summary>
/// Allows unrestricted access to the Hangfire dashboard. Acceptable only because no
/// User/role system exists yet (spec/21-roadmap.md Phase 0). Must be replaced with a
/// real authorization filter (e.g. restricted to Workspace Admins) before any
/// non-local deployment — see spec/17-admin.md for the eventual admin authority model.
/// </summary>
public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
