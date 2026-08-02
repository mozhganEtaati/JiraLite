using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Deployment;

/// <summary>
/// Task T047. The acceptance criterion is "migrations are not auto-applied outside Development"
/// (spec/20-coding-guidelines.md §9) — a property of how the app is configured and packaged, so it
/// is checked against the decision function and the shipped deployment files rather than by
/// standing a container up. Deliberately has no database fixture: these must run anywhere.
/// </summary>
public class DeploymentConfigurationTests
{
    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "JiraLite.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("QA")]
    public void AutoMigrate_outside_Development_fails_startup_instead_of_being_ignored(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseMigrator.ShouldMigrateOnStartup(
            new StubEnvironment(environmentName), new DatabaseOptions { AutoMigrate = true }));

        Assert.Contains(environmentName, exception.Message);
    }

    [Fact]
    public void Development_with_AutoMigrate_enabled_migrates_on_startup()
    {
        Assert.True(DatabaseMigrator.ShouldMigrateOnStartup(
            new StubEnvironment(Environments.Development), new DatabaseOptions { AutoMigrate = true }));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void AutoMigrate_off_never_migrates_on_startup(string environmentName)
    {
        Assert.False(DatabaseMigrator.ShouldMigrateOnStartup(
            new StubEnvironment(environmentName), new DatabaseOptions { AutoMigrate = false }));
    }

    [Fact]
    public void The_migrate_switch_is_recognised_and_kept_out_of_host_configuration()
    {
        string[] args = ["--urls", "http://+:9000", "--migrate"];

        Assert.True(DatabaseMigrator.IsMigrateCommand(args));
        Assert.Equal(["--urls", "http://+:9000"], DatabaseMigrator.StripMigrateArgument(args));
        Assert.False(DatabaseMigrator.IsMigrateCommand(["--urls", "http://+:9000"]));
    }

    [Fact]
    public void Default_appsettings_does_not_enable_auto_migration()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("src/Api/appsettings.json")));

        var database = document.RootElement.GetProperty("Database");

        Assert.False(database.GetProperty("AutoMigrate").GetBoolean());
    }

    [Fact]
    public void The_production_image_runs_as_a_non_root_user_and_defaults_to_Production()
    {
        var dockerfile = File.ReadAllText(RepoPath("src/Api/Dockerfile"));

        Assert.Contains("USER $APP_UID", dockerfile);
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", dockerfile);
        Assert.Contains("HEALTHCHECK", dockerfile);
        Assert.Contains("/health", dockerfile);
    }

    [Fact]
    public void The_dev_signing_key_is_excluded_from_the_image()
    {
        var dockerignore = File.ReadAllText(RepoPath("src/Api/.dockerignore"));

        Assert.Contains("appsettings.Development.json", dockerignore);
    }

    [Fact]
    public void The_production_stack_migrates_in_a_separate_step_the_api_waits_on()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.prod.yml"));

        // The migration runs as its own service, invoked through the same image's --migrate switch.
        Assert.Contains("command: [\"--migrate\"]", compose);
        Assert.Contains("restart: \"no\"", compose);
        // …and the API is gated on that service exiting 0, not merely on it having started.
        Assert.Contains("service_completed_successfully", compose);
        Assert.Contains("Database__AutoMigrate: \"false\"", compose);
    }

    [Fact]
    public void The_migration_step_is_not_given_the_jwt_signing_key()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.prod.yml"));
        var migrator = ServiceBlock(compose, "migrator");

        // It never issues or validates a token, so it has no reason to hold the signing secret —
        // and Program.cs correspondingly skips the "Jwt:SigningKey is not configured" fail-fast on
        // the --migrate path. Adding the key back here would quietly widen where it is stored.
        Assert.DoesNotContain("Jwt__SigningKey", migrator);
        Assert.Contains("ConnectionStrings__Default", migrator);
    }

    /// <summary>Returns one service's YAML block: from its two-space key to the next one.</summary>
    private static string ServiceBlock(string compose, string serviceName)
    {
        var lines = compose.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => line == $"  {serviceName}:");
        Assert.True(start >= 0, $"No '{serviceName}' service in docker-compose.prod.yml.");

        var end = Array.FindIndex(lines, start + 1, line => line.Length > 0 && !char.IsWhiteSpace(line[0]) || line.StartsWith("  ") && !line.StartsWith("   ") && line.TrimEnd().EndsWith(':'));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    [Fact]
    public void The_production_stack_has_no_default_credentials()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.prod.yml"));

        // Every secret uses ${VAR:?message}, which makes compose fail rather than substitute a
        // default — the dev stack's Dev_OnlyPassw0rd! must never reach a production deployment.
        foreach (var variable in new[] { "JIRALITE_SA_PASSWORD", "JIRALITE_JWT_SIGNING_KEY", "JIRALITE_CONNECTION_STRING" })
        {
            Assert.Contains($"${{{variable}:?", compose);
        }

        Assert.DoesNotContain("Dev_OnlyPassw0rd", compose);
        Assert.DoesNotContain("dev-only-signing-key", compose);
    }

    /// <summary>
    /// Walks up from the test binary to the directory holding the solution file. Test assemblies
    /// run out of bin/, so a path relative to the working directory would break the moment the
    /// target framework or configuration changes.
    /// </summary>
    private static string RepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JiraLite.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
