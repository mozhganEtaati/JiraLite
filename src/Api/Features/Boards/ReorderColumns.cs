using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md FR-03, NFR-01; spec/19-api-guidelines.md §11 (RowVersion concurrency wins
/// over the plain-id-array example in spec/06-boards.md — see this file's Task doc note).
/// </summary>
public static class ReorderColumns
{
    public record ColumnOrderEntry(Guid ColumnId, string RowVersion);

    public record Request(IReadOnlyList<ColumnOrderEntry> Columns);

    public record ResponseColumn(Guid ColumnId, int DisplayOrder, string RowVersion);

    public record Response(IReadOnlyList<ResponseColumn> Columns);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Columns).NotEmpty();
            RuleForEach(x => x.Columns).ChildRules(entry =>
            {
                entry.RuleFor(e => e.ColumnId).NotEmpty();
                entry.RuleFor(e => e.RowVersion).NotEmpty();
            });
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var currentColumns = await db.BoardColumns.Where(c => c.BoardId == boardId).ToListAsync(cancellationToken);
            if (currentColumns.Count == 0)
            {
                return Results.NotFound();
            }

            var currentIds = currentColumns.Select(c => c.Id).ToHashSet();
            var requestedIds = request.Columns.Select(e => e.ColumnId).ToHashSet();
            if (!currentIds.SetEquals(requestedIds))
            {
                return Results.BadRequest(new { detail = "The reorder payload must contain exactly the Board's current set of columns." });
            }

            for (var i = 0; i < request.Columns.Count; i++)
            {
                var entry = request.Columns[i];
                var column = currentColumns.Single(c => c.Id == entry.ColumnId);
                db.Entry(column).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(entry.RowVersion);
                column.DisplayOrder = i;
                // A column whose position happens not to change (e.g. it's already first) would
                // otherwise be dropped from EF's change set entirely, silently skipping its
                // concurrency check — force it into the UPDATE batch so every submitted RowVersion
                // is actually verified against the database, not just the ones that moved.
                db.Entry(column).Property(c => c.DisplayOrder).IsModified = true;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/concurrency-conflict",
                    "One or more columns were modified since you last loaded them. Reload and try again.");
            }

            var response = new Response(
                currentColumns
                    .OrderBy(c => c.DisplayOrder)
                    .Select(c => new ResponseColumn(c.Id, c.DisplayOrder, Convert.ToBase64String(c.RowVersion)))
                    .ToList());

            return Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/boards/{boardId:guid}/columns/reorder", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
