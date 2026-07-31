using Microsoft.AspNetCore.Http;

namespace JiraLite.Api.Common.Auth;

internal static class RouteValueHelper
{
    public static Guid? GetGuidRouteValue(HttpContext? httpContext, string name)
    {
        if (httpContext is not null
            && httpContext.Request.RouteValues.TryGetValue(name, out var value)
            && Guid.TryParse(value?.ToString(), out var id))
        {
            return id;
        }

        return null;
    }
}
