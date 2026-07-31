using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>
/// spec/06-boards.md FR-03, FR-04, BR-02. Partial update: only fields present are changed.
/// Setting IsDefault=true steals the flag from the board's previous default column.
/// Setting IsDefault=false or IsDoneColumn=false on the sole holder of that flag is rejected.
/// </summary>
public static class EditColumn
{
    public record Request(string? Name, bool? IsDefault, bool? IsDoneColumn);

    public record Response(Guid Id, string Name, bool IsDefault, bool IsDoneColumn);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Name).MaximumLength(100).When(x => x.Name is not null);
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid boardId,
            Guid columnId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var column = await db.BoardColumns.SingleOrDefaultAsync(c => c.Id == columnId && c.BoardId == boardId, cancellationToken);
            if (column is null)
            {
                return Results.NotFound();
            }

            if (request.IsDefault == false && column.IsDefault)
            {
                var anotherDefaultExists = await db.BoardColumns.AnyAsync(c => c.BoardId == boardId && c.Id != columnId && c.IsDefault, cancellationToken);
                if (!anotherDefaultExists)
                {
                    return Results.BadRequest(new { detail = "Cannot unset the only default column without setting another." });
                }
            }

            if (request.IsDoneColumn == false && column.IsDoneColumn)
            {
                var anotherDoneExists = await db.BoardColumns.AnyAsync(c => c.BoardId == boardId && c.Id != columnId && c.IsDoneColumn, cancellationToken);
                if (!anotherDoneExists)
                {
                    return Results.BadRequest(new { detail = "Cannot unset the only Done column without setting another." });
                }
            }

            if (request.IsDefault == true)
            {
                await db.BoardColumns.Where(c => c.BoardId == boardId && c.Id != columnId && c.IsDefault)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.IsDefault, false), cancellationToken);
            }

            if (request.Name is not null)
            {
                column.Name = request.Name.Trim();
            }
            if (request.IsDefault is not null)
            {
                column.IsDefault = request.IsDefault.Value;
            }
            if (request.IsDoneColumn is not null)
            {
                column.IsDoneColumn = request.IsDoneColumn.Value;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(column.Id, column.Name, column.IsDefault, column.IsDoneColumn));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/boards/{boardId:guid}/columns/{columnId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
