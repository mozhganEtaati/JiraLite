using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Attachments;

/// <summary>spec/11-attachments.md FR-02 — list metadata only.</summary>
public static class ListAttachments
{
    public record AttachmentItem(Guid Id, string FileName, string ContentType, long SizeBytes, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<AttachmentItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid issueId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.Attachments
                .Where(a => a.IssueId == issueId)
                .OrderBy(a => a.CreatedAtUtc)
                .Select(a => new AttachmentItem(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/issues/{issueId:guid}/attachments", Handler.Handle)
            .RequireAuthorization("IssueView")
            .WithTags("Attachments");
}
