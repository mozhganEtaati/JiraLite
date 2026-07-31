using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Attachments;

/// <summary>spec/11-attachments.md FR-04, BR-04 (hard delete: row + file), BR-05.</summary>
public static class DeleteAttachment
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid attachmentId, JiraLiteDbContext db, IFileStorage fileStorage, CancellationToken cancellationToken)
        {
            var attachment = await db.Attachments.SingleOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
            if (attachment is null)
            {
                return Results.NotFound();
            }

            var issue = await db.Issues.Where(i => i.Id == attachment.IssueId).Select(i => new { i.ProjectId }).SingleAsync(cancellationToken);
            var project = await db.Projects.SingleAsync(p => p.Id == issue.ProjectId, cancellationToken);
            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot delete an Attachment on an Issue in an archived Project.");
            }

            db.Attachments.Remove(attachment);
            await db.SaveChangesAsync(cancellationToken);
            await fileStorage.DeleteAsync(attachment.StorageKey, cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/attachments/{attachmentId:guid}", Handler.Handle)
            .RequireAuthorization("AttachmentDelete")
            .WithTags("Attachments");
}
