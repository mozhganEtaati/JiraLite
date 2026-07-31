using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Labels;

/// <summary>spec/12-labels.md FR-02, BR-03.</summary>
public static class AttachLabel
{
    public record Request(Guid LabelId);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.LabelId).NotEmpty();
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid issueId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var issue = await db.Issues.Where(i => i.Id == issueId).Select(i => new { i.ProjectId }).SingleOrDefaultAsync(cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var label = await db.Labels.SingleOrDefaultAsync(l => l.Id == request.LabelId, cancellationToken);
            if (label is null || label.ProjectId != issue.ProjectId)
            {
                return Results.BadRequest(new { detail = "labelId must reference a Label in the same Project as the Issue." });
            }

            if (await db.IssueLabels.AnyAsync(il => il.IssueId == issueId && il.LabelId == request.LabelId, cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/label-already-attached",
                    "This Label is already attached to the Issue.");
            }

            db.IssueLabels.Add(new IssueLabel { IssueId = issueId, LabelId = request.LabelId });
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/issues/{issueId}/labels/{request.LabelId}", new { issueId, labelId = request.LabelId });
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/issues/{issueId:guid}/labels", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Labels");
}
