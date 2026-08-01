using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Xunit;

namespace JiraLite.Api.IntegrationTests;

public class JiraLiteApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    static JiraLiteApiFactory()
    {
        // Observed on this machine: the first SqlClient connections opened from inside this
        // process (EF's migration, Hangfire's startup connection) intermittently time out even
        // though the exact same container is reachable in under 200ms from a separate process.
        // That pattern matches thread-pool starvation — async completions queued behind the
        // pool's default ramp-up rate while Kestrel/EF/Hangfire/xUnit all start up at once.
        // Raise the floor so the pool doesn't need to grow on demand during that window.
        ThreadPool.SetMinThreads(100, 100);
    }

    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("IntegrationTest_Passw0rd!")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _sqlContainer.GetConnectionString(),
                ["Jwt:Issuer"] = "JiraLite",
                ["Jwt:Audience"] = "JiraLite",
                ["Jwt:SigningKey"] = "integration-test-signing-key-not-for-production-1234567890",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "jiralite-tests", Guid.NewGuid().ToString())
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        // The MsSql container's wait strategy is satisfied once its readiness log line appears,
        // but the very first real client connection right after that can still hang against a
        // socket that isn't actually accepting yet. If that first attempt happens through the
        // app's pooled ADO.NET connection (via EF's migration or Hangfire's own startup
        // connection), the hang poisons that process's connection pool and every later attempt
        // queues behind it instead of hitting the network again — even though a brand new
        // connection from outside the process succeeds immediately. Probing here first, with
        // pooling disabled, absorbs that initial hang before the host (and Hangfire) ever starts.
        await WaitForRealConnectivityAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        await MigrateWithRetryAsync(db);
    }

    private static async Task MigrateWithRetryAsync(JiraLiteDbContext db)
    {
        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                return;
            }
            catch (SqlException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
            }
        }
    }

    private async Task WaitForRealConnectivityAsync()
    {
        var probeConnectionString = new SqlConnectionStringBuilder(_sqlContainer.GetConnectionString())
        {
            ConnectTimeout = 5,
            Pooling = false
        }.ConnectionString;

        const int maxAttempts = 30;
        var consecutiveSuccesses = 0;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var connection = new SqlConnection(probeConnectionString);
                await connection.OpenAsync();
                consecutiveSuccesses++;
                // Require two consecutive successful opens a few seconds apart — the image can
                // briefly accept a connection, log its readiness message, then still be mid-recovery
                // (observed: a transient "Login failed... error occurred while evaluating the
                // password" right after the readiness log line) before truly stabilizing.
                if (consecutiveSuccesses >= 2)
                {
                    return;
                }
            }
            catch (SqlException)
            {
                consecutiveSuccesses = 0;
                if (attempt == maxAttempts)
                {
                    throw;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    public new async Task DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
