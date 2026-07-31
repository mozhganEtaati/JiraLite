using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Labels;

/// <summary>spec/12-labels.md FR-02.</summary>
public static class DetachLabel
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid issueId, Guid labelId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var issueLabel = await db.IssueLabels.SingleOrDefaultAsync(il => il.IssueId == issueId && il.LabelId == labelId, cancellationToken);
            if (issueLabel is null)
            {
                return Results.NotFound();
            }

            db.IssueLabels.Remove(issueLabel);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/issues/{issueId:guid}/labels/{labelId:guid}", Handler.Handle)
            .RequireAuthorization("IssueContribute")
            .WithTags("Labels");
}
