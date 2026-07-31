using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Attachments;

/// <summary>spec/11-attachments.md BR-03 — available for every Attachment regardless of content type, served as a download.</summary>
public static class DownloadAttachment
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

            var stream = await fileStorage.OpenReadAsync(attachment.StorageKey, cancellationToken);
            if (stream is null)
            {
                return Results.NotFound();
            }

            return Results.File(stream, attachment.ContentType, attachment.FileName);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/attachments/{attachmentId:guid}/download", Handler.Handle)
            .RequireAuthorization("AttachmentView")
            .WithTags("Attachments");
}
