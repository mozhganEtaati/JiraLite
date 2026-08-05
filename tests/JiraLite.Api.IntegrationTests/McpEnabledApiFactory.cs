using Microsoft.Extensions.Configuration;

namespace JiraLite.Api.IntegrationTests;

/// <summary>
/// The base factory leaves <c>Mcp:Enabled</c> at its default of false, which is what
/// <see cref="Mcp.McpDisabledTests"/> asserts on. Everything that exercises the MCP surface —
/// including the Personal Access Token endpoints, which are only mapped alongside it
/// (spec/23-mcp-server.md NFR-05) — uses this one instead.
/// </summary>
public class McpEnabledApiFactory : JiraLiteApiFactory
{
    protected override void ApplyAdditionalConfiguration(IConfigurationBuilder configBuilder) =>
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true"
        });
}
