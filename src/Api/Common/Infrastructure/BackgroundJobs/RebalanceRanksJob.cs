using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Ranking;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Infrastructure.BackgroundJobs;

/// <summary>
/// spec/07-backlog.md BR-03 — evenly renumbers a Product/Sprint Backlog's Rank values without
/// changing relative order, once repeated insertions have exhausted LexoRank precision for that
/// list. Enqueued via IBackgroundJobClient (never invoked in-process), resolved Scoped by Hangfire.
/// spec/20-coding-guidelines.md §7.
/// </summary>
public class RebalanceRanksJob(JiraLiteDbContext db)
{
    public async Task Execute(Guid projectId, Guid? sprintId, CancellationToken cancellationToken)
    {
        var issues = await db.Issues
            .Where(i => i.ProjectId == projectId && i.SprintId == sprintId && i.Type != IssueType.Subtask)
            .OrderBy(i => i.Rank)
            .ToListAsync(cancellationToken);

        string? previousRank = null;
        foreach (var issue in issues)
        {
            var newRank = previousRank is null ? LexoRank.Initial() : LexoRank.Next(previousRank);
            issue.Rank = newRank;
            previousRank = newRank;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
