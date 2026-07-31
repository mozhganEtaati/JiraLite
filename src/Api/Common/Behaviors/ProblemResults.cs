namespace JiraLite.Api.Common.Behaviors;

/// <summary>
/// Consistent RFC 7807 Problem Details helpers for non-validation error responses,
/// per spec/19-api-guidelines.md §9-10. traceId is added automatically by the global
/// ProblemDetails customization configured in Program.cs.
/// </summary>
public static class ProblemResults
{
    public static IResult Conflict(string type, string detail) =>
        Results.Problem(type: type, title: "Conflict", statusCode: StatusCodes.Status409Conflict, detail: detail);

    public static IResult NotFound(string type, string detail) =>
        Results.Problem(type: type, title: "Not Found", statusCode: StatusCodes.Status404NotFound, detail: detail);

    public static IResult Forbidden(string type, string detail) =>
        Results.Problem(type: type, title: "Forbidden", statusCode: StatusCodes.Status403Forbidden, detail: detail);

    public static IResult Unauthorized(string type, string detail) =>
        Results.Problem(type: type, title: "Unauthorized", statusCode: StatusCodes.Status401Unauthorized, detail: detail);

    public static IResult Gone(string type, string detail) =>
        Results.Problem(type: type, title: "Gone", statusCode: StatusCodes.Status410Gone, detail: detail);
}
