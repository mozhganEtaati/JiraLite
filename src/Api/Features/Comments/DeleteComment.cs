using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Comments;

/// <summary>spec/10-comments.md FR-03, BR-03 (hard delete), BR-04. Authorization enforced by the CommentDelete policy.</summary>
public static class DeleteComment
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid commentId, JiraLiteDbContext db, CancellationToken cancellationToken)
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
                    "Cannot delete a Comment on an Issue in an archived Project.");
            }

            db.Comments.Remove(comment);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/comments/{commentId:guid}", Handler.Handle)
            .RequireAuthorization("CommentDelete")
            .WithTags("Comments");
}
