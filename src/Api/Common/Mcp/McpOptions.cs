namespace JiraLite.Api.Common.Mcp;

/// <summary>spec/23-mcp-server.md NFR-05 — the whole MCP surface is off unless deliberately enabled.</summary>
public class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>
    /// When false, neither `/mcp` nor the token-management endpoints are mapped at all, so they
    /// 404 rather than existing and refusing. Default off: enabling a write surface for automated
    /// clients is a decision a deployment makes explicitly.
    /// </summary>
    public bool Enabled { get; set; }

    public string ServerName { get; set; } = "JiraLite";

    public string ServerVersion { get; set; } = "1.0.0";
}
