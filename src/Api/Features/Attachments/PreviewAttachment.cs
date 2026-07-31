using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace JiraLite.Api.Features.Attachments;

/// <summary>spec/11-attachments.md FR-03, BR-02 — inline preview, images/PDF only, 415 otherwise.</summary>
public static class PreviewAttachment
{
    private static readonly HashSet<string> PreviewableContentTypes = ["image/png", "image/jpeg", "image/gif", "image/webp", "application/pdf"];

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid attachmentId, HttpContext httpContext, JiraLiteDbContext db, IFileStorage fileStorage, CancellationToken cancellationToken)
        {
            var attachment = await db.Attachments.SingleOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
            if (attachment is null)
            {
                return Results.NotFound();
            }

            if (!PreviewableContentTypes.Contains(attachment.ContentType))
            {
                return Results.Problem(
                    type: "https://jiralite.dev/errors/attachment-not-previewable",
                    title: "Unsupported Media Type",
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    detail: $"Content type '{attachment.ContentType}' does not support preview.");
            }

            var stream = await fileStorage.OpenReadAsync(attachment.StorageKey, cancellationToken);
            if (stream is null)
            {
                return Results.NotFound();
            }

            httpContext.Response.Headers[HeaderNames.ContentDisposition] =
                new ContentDispositionHeaderValue("inline") { FileNameStar = attachment.FileName }.ToString();

            return Results.Stream(stream, attachment.ContentType);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/attachments/{attachmentId:guid}/preview", Handler.Handle)
            .RequireAuthorization("AttachmentView")
            .WithTags("Attachments");
}
