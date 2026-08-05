using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>
/// spec/23-mcp-server.md FR-02, §11 — the caller's own tokens, newest first. Metadata only:
/// the plaintext value existed once, in the creation response, and is not recoverable (FR-03).
/// </summary>
public static class ListAccessTokens
{
    public record TokenItem(
        Guid Id,
        string Name,
        DateTime CreatedAtUtc,
        DateTime ExpiresAtUtc,
        DateTime? LastUsedAtUtc,
        bool IsActive);

    public record Response(IReadOnlyList<TokenItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            int? limit,
            string? cursor,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);
            var userId = caller.GetUserId();
            var now = DateTime.UtcNow;

            var page = await db.PersonalAccessTokens
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAtUtc)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(t => new TokenItem(
                    t.Id,
                    t.Name,
                    t.CreatedAtUtc,
                    t.ExpiresAtUtc,
                    t.LastUsedAtUtc,
                    t.RevokedAtUtc == null && t.ExpiresAtUtc > now))
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var items = page.Take(pageSize).ToList();
            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;

            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/me/tokens", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
