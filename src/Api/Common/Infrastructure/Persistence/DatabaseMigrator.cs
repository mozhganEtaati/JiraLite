using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Common.Infrastructure.Persistence;

/// <summary>
/// Task T047. spec/20-coding-guidelines.md §9 makes migration application a deliberate act outside
/// Development: an application restart must never be able to alter a production schema by itself.
/// This type is the only place in the app that calls <c>Migrate</c>, and it enforces that rule in
/// one testable decision — <see cref="ShouldMigrateOnStartup"/>.
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>Argument that makes the process migrate and exit instead of serving traffic.</summary>
    public const string MigrateArgument = "--migrate";

    /// <summary>
    /// Decides whether a normal (non-<c>--migrate</c>) startup should apply pending migrations.
    /// Throws rather than silently declining when AutoMigrate is set outside Development: an
    /// operator who wrote that into a production config believes migrations are being applied, and
    /// booting anyway would leave the app running against whatever schema happened to be there.
    /// </summary>
    public static bool ShouldMigrateOnStartup(IHostEnvironment environment, DatabaseOptions options)
    {
        if (!options.AutoMigrate)
        {
            return false;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Database:AutoMigrate is enabled in the '{environment.EnvironmentName}' environment. " +
                "spec/20-coding-guidelines.md §9 allows startup migration only in Development; elsewhere run " +
                $"the image with '{MigrateArgument}' (or `dotnet ef database update`) as its own deployment step.");
        }

        return true;
    }

    /// <summary>Returns true when the process was asked to run the migration step rather than the API.</summary>
    public static bool IsMigrateCommand(string[] args) =>
        args.Any(arg => string.Equals(arg, MigrateArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The args to hand to the host builder. <c>--migrate</c> is a bare switch with no value, which
    /// the command-line configuration provider would either reject or fold into the next argument,
    /// so it is consumed here instead of reaching configuration.
    /// </summary>
    public static string[] StripMigrateArgument(string[] args) =>
        [.. args.Where(arg => !string.Equals(arg, MigrateArgument, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Applies pending migrations, retrying while the database is still coming up.</summary>
    public static async Task MigrateAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseMigrator));
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var db = services.GetRequiredService<JiraLiteDbContext>();

        var attempts = Math.Max(1, options.MigrationRetryCount);
        var delay = TimeSpan.FromSeconds(Math.Max(0, options.MigrationRetryDelaySeconds));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count == 0)
                {
                    logger.LogInformation("Database schema is already up to date; no migrations to apply.");
                    return;
                }

                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Migrations applied successfully.");
                return;
            }
            catch (SqlException ex) when (attempt < attempts)
            {
                // Only connection-level failures are worth retrying — a bad migration will fail the
                // same way every time, and retrying it just delays the deployment failing loudly.
                logger.LogWarning(
                    ex,
                    "Database not reachable on attempt {Attempt} of {Attempts}; retrying in {Delay}s.",
                    attempt, attempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
