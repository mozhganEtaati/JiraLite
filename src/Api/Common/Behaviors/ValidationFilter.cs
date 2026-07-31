using FluentValidation;

namespace JiraLite.Api.Common.Behaviors;

/// <summary>
/// Runs the registered FluentValidation validator for <typeparamref name="TRequest"/>
/// before the endpoint handler executes, per spec/20-coding-guidelines.md §5.
/// Short-circuits with a 400 Problem Details response on failure; never reaches the handler.
/// If no validator is registered for TRequest, validation is skipped.
/// </summary>
public class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (validator is null)
        {
            return await next(context);
        }

        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return await next(context);
        }

        var validationResult = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => char.ToLowerInvariant(g.Key[0]) + g.Key[1..],
                    g => g.Select(e => e.ErrorMessage).ToArray());

            return Results.ValidationProblem(
                errors,
                title: "Validation Failed",
                type: "https://jiralite.dev/errors/validation-failed",
                detail: "One or more fields are invalid.");
        }

        return await next(context);
    }
}

public static class ValidationFilterEndpointExtensions
{
    /// <summary>Applies FluentValidation to the endpoint's TRequest parameter.</summary>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<TRequest>>();
}
