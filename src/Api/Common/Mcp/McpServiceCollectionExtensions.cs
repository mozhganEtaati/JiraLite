using JiraLite.Api.Features.Mcp;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Common.Mcp;

/// <summary>spec/23-mcp-server.md FR-01, NFR-05.</summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MCP server unconditionally. The feature flag is enforced where it is
    /// observable — whether `/mcp` and the token endpoints are mapped at all (NFR-05) — and that
    /// decision is made after the host is built, from <see cref="IsMcpEnabled"/>. Reading the flag
    /// here instead would capture `builder.Configuration` before WebApplicationFactory layers its
    /// own values in, the same stale-config trap already documented in Program.cs for the
    /// connection string and the JWT signing key.
    /// </summary>
    public static IServiceCollection AddJiraLiteMcp(this IServiceCollection services)
    {
        services.AddScoped<McpToolGateway>();
        services.AddScoped<ReadTools>();
        services.AddScoped<WriteTools>();

        services
            .AddMcpServer()
            // Stateless: every tool call is its own authenticated POST, so the caller's identity
            // and role are resolved fresh on each one — which is exactly what BR-01 requires.
            // It also leaves no server-side session to expire, migrate, or reason about.
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<ReadTools>()
            .WithTools<WriteTools>();

        return services;
    }

    /// <summary>Reads the bound options after the host is built, never from builder.Configuration.</summary>
    public static bool IsMcpEnabled(this IServiceProvider services) =>
        services.GetRequiredService<IOptions<McpOptions>>().Value.Enabled;
}
