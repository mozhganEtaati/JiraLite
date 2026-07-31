namespace JiraLite.Api.Common.Infrastructure.FileStorage;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Physical root directory files are written under (a Docker-mounted volume in V1).</summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Base URL to prefix storage keys with when building public URLs (e.g. "https://api.jiralite.local").
    /// If empty, URLs are built relative to the current request.
    /// </summary>
    public string PublicBaseUrl { get; init; } = "";
}
