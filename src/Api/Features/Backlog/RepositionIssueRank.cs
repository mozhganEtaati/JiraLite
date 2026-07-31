using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Ranking;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Backlog;

/// <summary>
/// spec/07-backlog.md FR-03, BR-02, BR-04, BR-07. Only the moved Issue's Rank changes (NFR-01).
/// If LexoRank precision is exhausted for this pair of neighbors, the request still completes with
/// a longer-but-valid rank and a background rebalance is enqueued (BR-03) rather than failing.
/// </summary>
public static class RepositionIssueRank
{
    public record Request(Guid? AfterIssueId, string RowVersion);

    public record Response(Guid Id, string Rank, string RowVersion);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.RowVersion).NotEmpty();
    }

    public static class Handler
    {
        private const int FallbackMaxRankLength = 250;

        public static async Task<IResult> Handle(
            Guid issueId,
            Request request,
            JiraLiteDbContext db,
            IBackgroundJobClient backgroundJobClient,
            CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            if (issue.Type == IssueType.Subtask)
            {
                return Results.BadRequest(new { detail = "Subtask-type Issues are never independently ranked." });
            }

            string? lowerRank = null;
            if (request.AfterIssueId is not null)
            {
                var afterIssue = await db.Issues
                    .Where(i => i.Id == request.AfterIssueId)
                    .Select(i => new { i.ProjectId, i.SprintId, i.Rank })
                    .SingleOrDefaultAsync(cancellationToken);
                if (afterIssue is null || afterIssue.ProjectId != issue.ProjectId || afterIssue.SprintId != issue.SprintId)
                {
                    return Results.BadRequest(new { detail = "afterIssueId must reference an Issue in the same list (same Project and Sprint) as the Issue being moved." });
                }

                lowerRank = afterIssue.Rank;
            }

            var siblingRanks = await db.Issues
                .Where(i => i.ProjectId == issue.ProjectId && i.SprintId == issue.SprintId && i.Type != IssueType.Subtask && i.Id != issue.Id)
                .OrderBy(i => i.Rank)
                .Select(i => i.Rank)
                .ToListAsync(cancellationToken);

            var upperRank = lowerRank is null
                ? siblingRanks.FirstOrDefault()
                : siblingRanks.FirstOrDefault(r => string.CompareOrdinal(r, lowerRank) > 0);

            string newRank;
            try
            {
                newRank = LexoRank.Between(lowerRank, upperRank);
            }
            catch (RankPrecisionExhaustedException)
            {
                newRank = LexoRank.Between(lowerRank, upperRank, FallbackMaxRankLength);
                backgroundJobClient.Enqueue<RebalanceRanksJob>(job => job.Execute(issue.ProjectId, issue.SprintId, CancellationToken.None));
            }

            byte[] originalRowVersion;
            try
            {
                originalRowVersion = Convert.FromBase64String(request.RowVersion);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { detail = "rowVersion is not valid base64." });
            }

            db.Entry(issue).Property(i => i.RowVersion).OriginalValue = originalRowVersion;
            issue.Rank = newRank;
            // A no-op reposition (e.g. the sole issue in an empty list) would otherwise produce
            // zero modified properties, so EF Core would skip the UPDATE entirely and silently miss
            // the concurrency check — force it into the statement, same as ReorderColumns.cs.
            db.Entry(issue).Property(i => i.Rank).IsModified = true;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/concurrency-conflict",
                    "This Issue was modified since you last loaded it. Reload and try again.");
            }

            return Results.Ok(new Response(issue.Id, issue.Rank, Convert.ToBase64String(issue.RowVersion)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/issues/{issueId:guid}/rank", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Backlog");
}
