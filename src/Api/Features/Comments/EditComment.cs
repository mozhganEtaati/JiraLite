using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Text;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Comments;

/// <summary>spec/10-comments.md FR-02, BR-01, BR-04, BR-06. Authorization (author + current role) enforced by the CommentEdit policy.</summary>
public static class EditComment
{
    public record Request(string Body);

    public record Response(Guid Id, string Body, DateTime? UpdatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(10000);
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid commentId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId, cancellationToken);
            if (comment is null)
            {
                return Results.NotFound();
            }

            var issue = await db.Issues.Where(i => i.Id == comment.IssueId).Select(i => new { i.ProjectId }).SingleAsync(cancellationToken);
            var project = await db.Projects.SingleAsync(p => p.Id == issue.ProjectId, cancellationToken);
            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot edit a Comment on an Issue in an archived Project.");
            }

            comment.Body = MarkdownSanitizer.Strip(request.Body.Trim())!;
            comment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(comment.Id, comment.Body, comment.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/comments/{commentId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("CommentEdit")
            .WithTags("Comments");
}
