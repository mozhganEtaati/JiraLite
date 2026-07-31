using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Comments;

/// <summary>spec/10-comments.md FR-04 — oldest first, paginated.</summary>
public static class ListComments
{
    public record CommentItem(Guid Id, UserSummary Author, string Body, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

    public record Response(IReadOnlyList<CommentItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid issueId,
            int? limit,
            string? cursor,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);

            var page = await db.Comments
                .Where(c => c.IssueId == issueId)
                .OrderBy(c => c.CreatedAtUtc)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(c => new { c.Id, c.AuthorUserId, c.Body, c.CreatedAtUtc, c.UpdatedAtUtc })
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var pageItems = page.Take(pageSize).ToList();
            var authors = await db.GetUserSummariesAsync(pageItems.Select(c => c.AuthorUserId), cancellationToken);

            var items = pageItems
                .Select(c => new CommentItem(c.Id, authors[c.AuthorUserId], c.Body, c.CreatedAtUtc, c.UpdatedAtUtc))
                .ToList();

            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;
            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/issues/{issueId:guid}/comments", Handler.Handle)
            .RequireAuthorization("IssueView")
            .WithTags("Comments");
}
