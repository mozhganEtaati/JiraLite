using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Persistence;

/// <summary>
/// Task T046. IndexCoverageTests proves the specced indexes exist; this proves the optimizer
/// actually picks them for the queries they were declared for. Runs against a seeded volume
/// (two Projects × 5,000 Issues) because at test-fixture scale every plan is a scan and the
/// assertion would be meaningless.
/// </summary>
public class QueryPlanTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private const int IssuesPerProject = 5_000;

    private readonly JiraLiteApiFactory _factory;

    public QueryPlanTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Product_backlog_query_seeks_the_ProjectId_SprintId_Rank_index()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var (projectId, _) = await SeedTwoProjectsOfIssuesAsync(db);

        // The exact query GetProductBacklog runs (spec/07-backlog.md), taken from EF itself
        // rather than hand-written, so the plan reflects the shipped SQL.
        var sql = db.Issues
            .Where(i => i.ProjectId == projectId && i.SprintId == null && i.Type != IssueType.Subtask)
            .OrderBy(i => i.Rank)
            .Skip(0)
            .Take(51)
            .Select(i => new { i.Id, i.Key, i.Title, i.Type, i.Priority, i.Rank, i.AssigneeUserId })
            .ToQueryString();

        var plan = await GetQueryPlanAsync(db, sql);

        Assert.Contains("IX_Issue_ProjectId_SprintId_Rank", plan);
        Assert.DoesNotContain("<TableScan", plan);
    }

    [Fact]
    public async Task Assignee_filtered_issue_query_seeks_the_ProjectId_AssigneeUserId_index()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var (projectId, assigneeUserId) = await SeedTwoProjectsOfIssuesAsync(db);

        var sql = db.Issues
            .Where(i => i.ProjectId == projectId && i.AssigneeUserId == assigneeUserId)
            .Select(i => new { i.Id, i.Key })
            .ToQueryString();

        var plan = await GetQueryPlanAsync(db, sql);

        Assert.Contains("IX_Issue_ProjectId_AssigneeUserId", plan);
    }

    /// <summary>
    /// Captures the estimated plan without executing the statement. SET SHOWPLAN_XML has to be
    /// the only statement in its batch, hence the separate command.
    /// </summary>
    private static async Task<string> GetQueryPlanAsync(JiraLiteDbContext db, string sql)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var on = new SqlCommand("SET SHOWPLAN_XML ON", connection))
        {
            await on.ExecuteNonQueryAsync();
        }

        try
        {
            var plan = new System.Text.StringBuilder();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                {
                    plan.Append(reader.GetString(0));
                }
            }
            while (await reader.NextResultAsync());

            return plan.ToString();
        }
        finally
        {
            await using var off = new SqlCommand("SET SHOWPLAN_XML OFF", connection);
            await off.ExecuteNonQueryAsync();
        }
    }

    private static async Task<(Guid ProjectId, Guid AssigneeUserId)> SeedTwoProjectsOfIssuesAsync(JiraLiteDbContext db)
    {
        var now = DateTime.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"plan-{Guid.NewGuid():N}@example.com", PasswordHash = "x", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", OwnerUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        var workspace = new Workspace { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "WS", CreatedByUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(user, org, workspace);

        var projectIds = new List<Guid>();
        var columnIds = new List<Guid>();
        foreach (var key in new[] { "PLAN", "OTHR" })
        {
            var project = new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = key, Name = key, CreatedByUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Main", Type = BoardType.Kanban, CreatedAtUtc = now, UpdatedAtUtc = now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "To Do", DisplayOrder = 0, IsDefault = true, IsDoneColumn = false };
            db.AddRange(project, board, column);
            projectIds.Add(project.Id);
            columnIds.Add(column.Id);
        }

        await db.SaveChangesAsync();

        for (var i = 0; i < projectIds.Count; i++)
        {
            await BulkInsertIssuesAsync(db, projectIds[i], columnIds[i], user.Id);
        }

        // Without this the optimizer is costing against the row counts from before the bulk
        // insert, which is exactly the fixture-scale situation this test exists to avoid.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [Issue] WITH FULLSCAN");
#pragma warning restore EF1002

        return (projectIds[0], user.Id);
    }

    private static async Task BulkInsertIssuesAsync(JiraLiteDbContext db, Guid projectId, Guid columnId, Guid userId)
    {
        // Set-based insert off a ROW_NUMBER generator — 10,000 round-tripped EF inserts would
        // dominate the runtime of the whole suite.
        const string sql = """
            INSERT INTO [Issue]
                (Id, ProjectId, Number, [Key], [Type], Title, [Priority], BoardColumnId, SprintId, Rank,
                 AssigneeUserId, ReporterUserId, CreatedByUserId, CreatedAtUtc, UpdatedByUserId, UpdatedAtUtc)
            SELECT
                NEWID(), @projectId, g.n, CONCAT('K-', g.n), 'Story', CONCAT('Issue ', g.n), 'Medium', @columnId, NULL,
                RIGHT(REPLICATE('0', 10) + CAST(g.n AS varchar(10)), 10),
                CASE WHEN g.n % 10 = 0 THEN @userId ELSE NULL END, @userId, @userId, SYSUTCDATETIME(), @userId, SYSUTCDATETIME()
            FROM (
                SELECT TOP (@count) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b
            ) g
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            new SqlParameter("@projectId", projectId),
            new SqlParameter("@columnId", columnId),
            new SqlParameter("@userId", userId),
            new SqlParameter("@count", IssuesPerProject));
    }
}
