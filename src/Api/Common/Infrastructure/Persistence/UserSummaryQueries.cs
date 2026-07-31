using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Infrastructure.Persistence;

/// <summary>Shared UserSummary lookups (spec/19-api-guidelines.md §7), reused by every Work Tracking
/// list/detail endpoint that surfaces an assignee/reporter/author/uploader.</summary>
public static class UserSummaryQueries
{
    public static Task<UserSummary?> GetUserSummaryAsync(this JiraLiteDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.UserProfiles
            .Where(p => p.UserId == userId)
            .Select(p => new UserSummary(p.UserId, p.DisplayName, p.AvatarUrl))
            .SingleOrDefaultAsync(cancellationToken);

    public static Task<Dictionary<Guid, UserSummary>> GetUserSummariesAsync(this JiraLiteDbContext db, IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return db.UserProfiles
            .Where(p => ids.Contains(p.UserId))
            .Select(p => new UserSummary(p.UserId, p.DisplayName, p.AvatarUrl))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
    }
}
