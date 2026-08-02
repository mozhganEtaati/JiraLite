namespace JiraLite.Api.Common.Infrastructure.Persistence;

/// <summary>spec/20-coding-guidelines.md §9 — see <see cref="DatabaseMigrator"/> for how AutoMigrate is constrained.</summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Apply pending migrations during startup. Honoured only in Development (which includes the
    /// local Docker Compose stack, since that runs with ASPNETCORE_ENVIRONMENT=Development); setting
    /// it anywhere else is a configuration error and fails startup rather than being ignored.
    /// </summary>
    public bool AutoMigrate { get; set; }

    /// <summary>How many times the migrate step retries a connection failure before giving up.</summary>
    public int MigrationRetryCount { get; set; } = 10;

    /// <summary>Delay between those retries.</summary>
    public int MigrationRetryDelaySeconds { get; set; } = 5;
}
