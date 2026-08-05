using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol;

namespace JiraLite.Api.Common.Mcp;

/// <summary>
/// The one adapter between an MCP tool invocation and a feature slice
/// (spec/23-mcp-server.md FR-05, FR-07, FR-08, NFR-02, NFR-04).
///
/// Tools contain no domain logic of their own; everything they need — authorization, validation,
/// and turning a slice's <see cref="IResult"/> into a tool result — happens here, once, so the
/// MCP surface cannot drift from the HTTP surface it mirrors.
/// </summary>
public class McpToolGateway(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService,
    IServiceProvider services,
    ILogger<McpToolGateway> logger)
{
    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext
        ?? throw new McpException("This tool can only be invoked over an authenticated MCP request.");

    public ClaimsPrincipal User => HttpContext.User;

    public Guid UserId => User.GetUserId();

    /// <summary>
    /// Runs a tool whose backing endpoint needs no policy beyond being authenticated, because the
    /// slice already scopes its results to the caller.
    /// </summary>
    public Task<object?> InvokeAsync(string toolName, Func<Task<IResult>> handler) =>
        InvokeCoreAsync(toolName, policy: null, deniedMessage: null, [], null, handler);

    /// <summary>
    /// Runs a read tool: authorize, invoke the slice handler, unwrap.
    /// </summary>
    public Task<object?> InvokeAsync(
        string toolName,
        string? policy,
        string deniedMessage,
        (string Name, Guid Value)[] routeValues,
        Func<Task<IResult>> handler) =>
        InvokeCoreAsync(toolName, policy, deniedMessage, routeValues, null, handler);

    /// <summary>
    /// Runs a write tool: authorize, run the slice's own FluentValidation validator, invoke, unwrap.
    /// The validator is the one the HTTP endpoint already uses (spec/23-mcp-server.md §12) — there
    /// is no second, tool-specific validation path to keep in sync.
    /// </summary>
    public Task<object?> InvokeAsync<TRequest>(
        string toolName,
        string? policy,
        string deniedMessage,
        (string Name, Guid Value)[] routeValues,
        TRequest request,
        Func<Task<IResult>> handler) =>
        InvokeCoreAsync(toolName, policy, deniedMessage, routeValues, () => ValidateAsync(request), handler);

    private async Task<object?> InvokeCoreAsync(
        string toolName,
        string? policy,
        string? deniedMessage,
        (string Name, Guid Value)[] routeValues,
        Func<Task>? validate,
        Func<Task<IResult>> handler)
    {
        // spec/23-mcp-server.md NFR-02 — who, through which credential, invoked what.
        logger.LogInformation(
            "MCP tool {ToolName} invoked by user {UserId} with token {TokenId}",
            toolName, UserId, McpCallerContext.GetTokenId(HttpContext));

        // The existing authorization handlers read their entity id from the request's route values
        // (see RouteValueHelper). Over MCP that id arrives as a tool argument instead, so the
        // gateway puts it where those handlers already look. This is the whole of the adaptation:
        // the policies themselves, and therefore spec/16-rbac.md BR-02, are reused untouched.
        foreach (var (name, value) in routeValues)
        {
            HttpContext.Request.RouteValues[name] = value.ToString();
        }

        if (policy is not null)
        {
            var authorization = await authorizationService.AuthorizeAsync(User, resource: null, policy);
            if (!authorization.Succeeded)
            {
                throw new McpException(deniedMessage ?? "You do not have permission to perform this action.");
            }
        }

        if (validate is not null)
        {
            await validate();
        }

        return Unwrap(await handler());
    }

    private async Task ValidateAsync<TRequest>(TRequest request)
    {
        var validator = services.GetService<IValidator<TRequest>>();
        if (validator is null || request is null)
        {
            return;
        }

        var result = await validator.ValidateAsync(request, HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            throw new McpException(string.Join(" ", result.Errors.Select(e => e.ErrorMessage)));
        }
    }

    /// <summary>
    /// Turns a slice's <see cref="IResult"/> into either a tool payload or a tool error carrying
    /// the message the HTTP response would have carried (spec/23-mcp-server.md FR-08).
    ///
    /// Matching on the framework's own <see cref="IStatusCodeHttpResult"/>/<see cref="IValueHttpResult"/>
    /// interfaces rather than on each concrete result type keeps this working for every slice,
    /// including ones written after this file.
    /// </summary>
    private static object? Unwrap(IResult result)
    {
        var statusCode = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        var value = (result as IValueHttpResult)?.Value;

        if (statusCode is >= 200 and < 300)
        {
            return value;
        }

        throw new McpException(DescribeFailure(statusCode, value));
    }

    private static string DescribeFailure(int statusCode, object? value) => value switch
    {
        HttpValidationProblemDetails validation when validation.Errors.Count > 0 =>
            string.Join(" ", validation.Errors.SelectMany(e => e.Value)),
        ProblemDetails { Detail.Length: > 0 } problem => problem.Detail,
        _ => statusCode switch
        {
            StatusCodes.Status404NotFound => "Not found, or not visible to you.",
            StatusCodes.Status403Forbidden => "You do not have permission to perform this action.",
            StatusCodes.Status409Conflict => "The request conflicts with the current state; re-read the entity and retry.",
            _ => $"The request failed with status {statusCode}."
        }
    };
}
