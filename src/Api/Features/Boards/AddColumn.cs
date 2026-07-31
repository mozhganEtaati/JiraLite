using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md FR-03, BR-02 — a new IsDefault column steals the flag from the previous holder.</summary>
public static class AddColumn
{
    public record Request(string Name, bool IsDefault, bool IsDoneColumn);

    public record Response(Guid Id, string Name, int DisplayOrder, bool IsDefault, bool IsDoneColumn);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            if (!await db.Boards.AnyAsync(b => b.Id == boardId, cancellationToken))
            {
                return Results.NotFound();
            }

            if (request.IsDefault)
            {
                await db.BoardColumns.Where(c => c.BoardId == boardId && c.IsDefault)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.IsDefault, false), cancellationToken);
            }

            var nextDisplayOrder = await db.BoardColumns
                .Where(c => c.BoardId == boardId)
                .Select(c => (int?)c.DisplayOrder)
                .MaxAsync(cancellationToken) ?? -1;

            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                Name = request.Name.Trim(),
                DisplayOrder = nextDisplayOrder + 1,
                IsDefault = request.IsDefault,
                IsDoneColumn = request.IsDoneColumn
            };
            db.BoardColumns.Add(column);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/boards/{boardId}/columns/{column.Id}",
                new Response(column.Id, column.Name, column.DisplayOrder, column.IsDefault, column.IsDoneColumn));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/boards/{boardId:guid}/columns", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
