using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Ranking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Ranking;

public class RebalanceRanksJobTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public RebalanceRanksJobTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rebalance_renumbers_a_squeezed_list_without_changing_relative_order()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var now = DateTime.UtcNow;

        var user = new User { Id = Guid.NewGuid(), Email = $"rebalance-{Guid.NewGuid():N}@example.com", PasswordHash = "x", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", OwnerUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        var workspace = new Workspace { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "WS", CreatedByUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        var project = new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "JIRA", Name = "P1", CreatedByUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Main", Type = BoardType.Kanban, CreatedAtUtc = now, UpdatedAtUtc = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "To Do", DisplayOrder = 0, IsDefault = true, IsDoneColumn = false };
        db.AddRange(user, org, workspace, project, board, column);

        // Ranks squeezed tightly together (as if inserted between the same two neighbors repeatedly),
        // still in strictly increasing order.
        var ranks = new List<string> { "0|100000:1" };
        for (var i = 0; i < 4; i++)
        {
            ranks.Add(LexoRank.Between(ranks[^1], "0|100001:", maxRankLength: 200));
        }

        var issues = ranks.Select((rank, index) => new Issue
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Number = index + 1,
            Key = $"JIRA-{index + 1}",
            Type = IssueType.Story,
            Title = $"Issue {index}",
            Priority = IssuePriority.Medium,
            BoardColumnId = column.Id,
            Rank = rank,
            ReporterUserId = user.Id,
            CreatedByUserId = user.Id,
            CreatedAtUtc = now,
            UpdatedByUserId = user.Id,
            UpdatedAtUtc = now
        }).ToList();
        db.Issues.AddRange(issues);
        await db.SaveChangesAsync();

        var expectedOrderedIds = issues.OrderBy(i => i.Rank, StringComparer.Ordinal).Select(i => i.Id).ToList();

        var job = new RebalanceRanksJob(db);
        await job.Execute(project.Id, sprintId: null, CancellationToken.None);

        var afterRebalance = await db.Issues.Where(i => i.ProjectId == project.Id).ToListAsync();
        var actualOrderedIds = afterRebalance.OrderBy(i => i.Rank, StringComparer.Ordinal).Select(i => i.Id).ToList();

        Assert.Equal(expectedOrderedIds, actualOrderedIds);
        Assert.All(afterRebalance, i => Assert.True(i.Rank.Length < 20));
    }
}
