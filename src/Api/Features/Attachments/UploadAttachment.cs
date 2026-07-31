using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Features.Attachments;

/// <summary>spec/11-attachments.md FR-01, BR-01, BR-05, NFR-01–NFR-03.</summary>
public static class UploadAttachment
{
    private static readonly HashSet<string> DisallowedExtensions = [".exe", ".dll", ".sh", ".bat", ".cmd", ".msi"];

    public record Response(Guid Id, Guid IssueId, string FileName, string ContentType, long SizeBytes, UserSummary UploadedBy, DateTime CreatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid issueId,
            IFormFile file,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            IFileStorage fileStorage,
            IOptions<AttachmentOptions> options,
            CancellationToken cancellationToken)
        {
            var issue = await db.Issues.Where(i => i.Id == issueId).Select(i => new { i.ProjectId }).SingleOrDefaultAsync(cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var project = await db.Projects.SingleAsync(p => p.Id == issue.ProjectId, cancellationToken);
            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot upload an Attachment on an Issue in an archived Project.");
            }

            var extension = Path.GetExtension(file.FileName);
            if (DisallowedExtensions.Contains(extension.ToLowerInvariant()))
            {
                return Results.Problem(
                    type: "https://jiralite.dev/errors/disallowed-attachment-type",
                    title: "Unsupported Media Type",
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    detail: $"Files with extension '{extension}' are not allowed.");
            }

            if (file.Length > options.Value.MaxSizeBytes)
            {
                return Results.Problem(
                    type: "https://jiralite.dev/errors/attachment-too-large",
                    title: "Payload Too Large",
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    detail: $"Attachment exceeds the {options.Value.MaxSizeBytes} byte size limit.");
            }

            await using var stream = file.OpenReadStream();
            var storageKey = await fileStorage.SaveAsync($"attachments/{issueId}", file.FileName, stream, cancellationToken);

            var userId = caller.GetUserId();
            var attachment = new Attachment
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                UploadedByUserId = userId,
                FileName = file.FileName,
                StorageKey = storageKey,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                SizeBytes = file.Length,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(cancellationToken);

            var uploadedBy = (await db.GetUserSummaryAsync(userId, cancellationToken))!;

            return Results.Created(
                $"/api/attachments/{attachment.Id}",
                new Response(attachment.Id, attachment.IssueId, attachment.FileName, attachment.ContentType, attachment.SizeBytes, uploadedBy, attachment.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/issues/{issueId:guid}/attachments", Handler.Handle)
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .RequireAuthorization("IssueContribute")
            .WithTags("Attachments");
}
