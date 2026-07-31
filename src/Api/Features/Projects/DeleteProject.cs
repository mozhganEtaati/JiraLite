using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>
/// spec/05-projects.md BR-05, BR-06, BR-07 — archive-before-delete rail, Workspace-Admin-only,
/// cascades Boards/Columns/Sprints/ProjectMembers and detaches (nulls) ActivityLogEntry.ProjectId.
/// spec/18-database.md §9 — Project/Board/Sprint use NO ACTION FKs, so this orchestration is
/// application code, not a database cascade.
/// </summary>
public static class DeleteProject
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
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

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var boardIds = await db.Boards.Where(b => b.ProjectId == projectId).Select(b => b.Id).ToListAsync(cancellationToken);
            await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync(cancellationToken);
            await db.Sprints.Where(s => s.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
            await db.Boards.Where(b => b.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
            await db.ProjectMembers.Where(m => m.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

            await db.ActivityLogEntries
                .Where(e => e.ProjectId == projectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.ProjectId, (Guid?)null), cancellationToken);

            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/projects/{projectId:guid}", Handler.Handle)
            .RequireAuthorization("ProjectWorkspaceAdmin")
            .WithTags("Projects");
}
