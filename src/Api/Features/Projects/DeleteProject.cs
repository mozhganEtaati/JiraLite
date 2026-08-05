using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>
/// spec/05-projects.md BR-05, BR-06, BR-07 — archive-before-delete rail, Workspace-Admin-only,
/// cascades Issues (with their Comments, Attachments and stored files), Labels, Boards, Columns,
/// Sprints and ProjectMembers, and detaches (nulls) ActivityLogEntry.ProjectId.
/// spec/18-database.md §9 — Project/Board/Sprint/Issue use NO ACTION FKs, so this orchestration is
/// application code, not a database cascade.
/// </summary>
public static class DeleteProject
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, IFileStorage fileStorage, CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (!project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-not-archived",
                    "A Project must be archived before it can be permanently deleted.");
            }

            // Read the stored-file keys before the rows naming them are gone. The files
            // themselves are removed after the transaction commits, mirroring DeleteAttachment
            // (spec/11-attachments.md BR-04): losing a file for a row that survived a rollback
            // is unrecoverable, whereas an orphaned file is not.
            var storageKeys = await db.Attachments
                .Where(a => db.Issues.Any(i => i.Id == a.IssueId && i.ProjectId == projectId))
                .Select(a => a.StorageKey)
                .ToListAsync(cancellationToken);

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Issues go first: Issue.BoardColumnId, Issue.SprintId and Issue.ParentIssueId are all
            // NO ACTION (spec/18-database.md §9), so deleting Boards/Columns/Sprints while any Issue
            // still points at them fails on the foreign key. Comments, Attachments and IssueLabels
            // hang off Issue with ON DELETE CASCADE, so the database removes those rows with it.
            //
            // ParentIssueId is self-referencing, so it is detached first rather than relying on the
            // order rows happen to be removed in within the one DELETE statement.
            await db.Issues
                .Where(i => i.ProjectId == projectId && i.ParentIssueId != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.ParentIssueId, (Guid?)null), cancellationToken);
            await db.Issues.Where(i => i.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

            var boardIds = await db.Boards.Where(b => b.ProjectId == projectId).Select(b => b.Id).ToListAsync(cancellationToken);
            await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync(cancellationToken);
            await db.Sprints.Where(s => s.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
            await db.Boards.Where(b => b.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
            await db.Labels.Where(l => l.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
            await db.ProjectMembers.Where(m => m.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

            await db.ActivityLogEntries
                .Where(e => e.ProjectId == projectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.ProjectId, (Guid?)null), cancellationToken);

            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            foreach (var storageKey in storageKeys)
            {
                await fileStorage.DeleteAsync(storageKey, cancellationToken);
            }

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/projects/{projectId:guid}", Handler.Handle)
            .RequireAuthorization("ProjectWorkspaceAdmin")
            .WithTags("Projects");
}
