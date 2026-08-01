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

    // One SQL Server container for the whole test run, shared by every test class.
    //
    // Each test class gets its own JiraLiteApiFactory instance (xUnit IClassFixture), so a
    // per-instance container meant starting SQL Server 2022 from scratch ~40 times per full run.
    // That was both slow (measured: 8.3 minutes for just three classes) and the direct cause of
    // repeated "The wait operation timed out" failures during MigrateAsync — every fresh start
    // was another chance to lose the startup race, and a lost race fails the class's whole
    // InitializeAsync before a single test body runs.
    //
    // Sharing is safe here: xunit.runner.json sets parallelizeTestCollections=false, so classes
    // never run concurrently, and every test already calls DatabaseResetHelper.ResetAsync in its
    // own InitializeAsync — test isolation comes from that reset, not from container identity.
    private static readonly MsSqlContainer SqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("IntegrationTest_Passw0rd!")
        .Build();

    private static readonly SemaphoreSlim ContainerStartGate = new(1, 1);
    private static bool _containerStarted;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = SqlContainer.GetConnectionString(),
                ["Jwt:Issuer"] = "JiraLite",
                ["Jwt:Audience"] = "JiraLite",
                ["Jwt:SigningKey"] = "integration-test-signing-key-not-for-production-1234567890",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "jiralite-tests", Guid.NewGuid().ToString())
            });
        });
    }

    public async Task InitializeAsync()
    {
        // Must complete before Services is touched below — building the host reads the container's
        // connection string, which only exists once the container is running.
        await EnsureContainerStartedAsync();

        // Runs on every factory, but only the first one does real work; for later test classes the
        // schema is already present and MigrateAsync is a no-op.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        await MigrateWithRetryAsync(db);
    }

    private static async Task EnsureContainerStartedAsync()
    {
        await ContainerStartGate.WaitAsync();
        try
        {
            if (_containerStarted)
            {
                return;
            }

            await SqlContainer.StartAsync();
            await WaitForRealConnectivityAsync();
            _containerStarted = true;
        }
        finally
        {
            ContainerStartGate.Release();
        }
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

    private static async Task WaitForRealConnectivityAsync()
    {
        var probeConnectionString = new SqlConnectionStringBuilder(SqlContainer.GetConnectionString())
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
        // Deliberately does NOT dispose SqlContainer — it is shared across every test class and must
        // outlive this factory. Testcontainers' Ryuk sidecar removes it when the test process exits.
        await base.DisposeAsync();
    }
}
