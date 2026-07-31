using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.IntegrationTests;

/// <summary>Truncates every table in FK-safe (child-before-parent) order between tests.</summary>
public static class DatabaseResetHelper
{
    private static readonly string[] TablesInDeleteOrder =
    [
        "ActivityLogEntry",
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
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM [{table}]");
        }
    }
}
