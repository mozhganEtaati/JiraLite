using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.IntegrationTests;

/// <summary>Truncates every table in FK-safe (child-before-parent) order between tests.</summary>
public static class DatabaseResetHelper
{
    private static readonly string[] TablesInDeleteOrder =
    [
        "ActivityLogEntry",
        "IssueLabel", "Comment", "Attachment", "Label", "Issue",
        "Sprint", "BoardColumn", "Board",
        "ProjectMember", "Project",
        "TeamMember", "Team",
        "Invitation", "WorkspaceMember", "Workspace", "Organization",
        "RefreshToken", "NotificationPreference", "UserProfile", "User"
    ];

    public static async Task ResetAsync(JiraLiteDbContext db)
    {
        foreach (var table in TablesInDeleteOrder)
        {
            // Table names come from a fixed internal allowlist, never user input — safe to use with ExecuteSqlRaw.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM [{table}]");
#pragma warning restore EF1002
        }
    }
}
