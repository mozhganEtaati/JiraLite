using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md §9, §12 — Type is immutable, only Name is editable.</summary>
public static class RenameBoard
{
    public record Request(string Name);

    public record Response(Guid Id, string Name, DateTime UpdatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var board = await db.Boards.SingleOrDefaultAsync(b => b.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            board.Name = request.Name.Trim();
            board.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(board.Id, board.Name, board.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/boards/{boardId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardManage")
            .WithTags("Boards");
}
