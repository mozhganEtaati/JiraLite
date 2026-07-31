using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>
/// spec/09-issues.md FR-05, BR-05, BR-06. Comment/Attachment/IssueLabel rows cascade at the DB
/// level (FK ON DELETE CASCADE), but Attachment files on disk don't — their StorageKeys are
/// collected before the delete and removed from IFileStorage after it commits.
/// </summary>
public static class DeleteIssue
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid issueId, JiraLiteDbContext db, IFileStorage fileStorage, CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var issueIdsToDelete = new List<Guid> { issue.Id };

            if (issue.Type == IssueType.Epic)
            {
                // BR-06: detach children rather than deleting them.
                await db.Issues
                    .Where(i => i.ParentIssueId == issue.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.ParentIssueId, (Guid?)null), cancellationToken);
            }
            else
            {
                // BR-05: a Subtask has no meaning without its parent — hard-delete cascades to them.
                var subtaskIds = await db.Issues
                    .Where(i => i.ParentIssueId == issue.Id)
                    .Select(i => i.Id)
                    .ToListAsync(cancellationToken);
                issueIdsToDelete.AddRange(subtaskIds);
            }

            var storageKeys = await db.Attachments
                .Where(a => issueIdsToDelete.Contains(a.IssueId))
                .Select(a => a.StorageKey)
                .ToListAsync(cancellationToken);

            await db.Issues.Where(i => issueIdsToDelete.Contains(i.Id)).ExecuteDeleteAsync(cancellationToken);

            foreach (var storageKey in storageKeys)
            {
                await fileStorage.DeleteAsync(storageKey, cancellationToken);
            }

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/issues/{issueId:guid}", Handler.Handle)
            .RequireAuthorization("IssueManage")
            .WithTags("Issues");
}
