namespace JiraLite.Api.Common.Infrastructure.FileStorage;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Physical root directory files are written under (a Docker-mounted volume in V1).</summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Base URL to prefix storage keys with when building public URLs (e.g. "https://cdn.jiralite.local").
    /// Leave empty unless files are served from a different origin than the API: the URLs built from it
    /// are stored, so an absolute one only belongs here when that host is stable.
    /// </summary>
    public string PublicBaseUrl { get; init; } = "";
}
