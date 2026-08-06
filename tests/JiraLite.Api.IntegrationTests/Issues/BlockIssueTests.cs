using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

/// <summary>spec/09-issues.md FR-08, FR-09, BR-14..BR-17 — blocking and unblocking an Issue.</summary>
public class BlockIssueTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public BlockIssueTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> GetIssueAsync(HttpClient client, Guid issueId)
    {
        var response = await client.GetAsync($"/api/issues/{issueId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> RowVersionAsync(HttpClient client, Guid issueId) =>
        (await GetIssueAsync(client, issueId)).GetProperty("rowVersion").GetString()!;

    [Fact]
    public async Task Blocking_records_the_reason_and_starts_the_clock()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the payments vendor", rowVersion = await RowVersionAsync(client, issueId) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var issue = await GetIssueAsync(client, issueId);
        Assert.True(issue.GetProperty("isBlocked").GetBoolean());
        Assert.Equal("Waiting on the payments vendor", issue.GetProperty("blockedReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, issue.GetProperty("blockedSinceUtc").ValueKind);
    }

    [Fact]
    public async Task Re_blocking_rewrites_the_reason_but_keeps_the_original_timestamp()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var first = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the vendor", rowVersion = await RowVersionAsync(client, issueId) });
        first.EnsureSuccessStatusCode();
        var blockedSince = (await GetIssueAsync(client, issueId)).GetProperty("blockedSinceUtc").GetDateTime();

        var second = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the vendor's security review", rowVersion = await RowVersionAsync(client, issueId) });
        second.EnsureSuccessStatusCode();

        // BR-15: sharpening the wording must not restart "blocked for six days".
        var issue = await GetIssueAsync(client, issueId);
        Assert.Equal("Waiting on the vendor's security review", issue.GetProperty("blockedReason").GetString());
        Assert.Equal(blockedSince, issue.GetProperty("blockedSinceUtc").GetDateTime());
    }

    [Fact]
    public async Task Unblocking_clears_the_flag_the_reason_and_the_timestamp()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var blockResponse = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on design", rowVersion = await RowVersionAsync(client, issueId) });
        blockResponse.EnsureSuccessStatusCode();

        var unblockResponse = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/unblock",
            new { rowVersion = await RowVersionAsync(client, issueId) });
        Assert.Equal(HttpStatusCode.OK, unblockResponse.StatusCode);

        var issue = await GetIssueAsync(client, issueId);
        Assert.False(issue.GetProperty("isBlocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("blockedReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("blockedSinceUtc").ValueKind);
    }

    [Fact]
    public async Task Blocking_an_issue_in_a_done_column_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var moveResponse = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move",
            new { boardColumnId = seeded.DoneColumnId, rowVersion = await RowVersionAsync(client, issueId) });
        moveResponse.EnsureSuccessStatusCode();

        // BR-16: finished work is not blocked — the blocker would sit in the sprint report
        // describing work that is already over.
        var response = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Too late", rowVersion = await RowVersionAsync(client, issueId) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Finishing_a_blocked_issue_clears_its_blocked_state()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        (await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the vendor", rowVersion = await RowVersionAsync(client, issueId) }))
            .EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move",
            new { boardColumnId = seeded.DoneColumnId, rowVersion = await RowVersionAsync(client, issueId) }))
            .EnsureSuccessStatusCode();

        // BR-17 in reverse: the rule that a Done Issue cannot be blocked has to hold against the
        // data, not only against the block endpoint, or the card stays marked Blocked forever.
        var issue = await GetIssueAsync(client, issueId);
        Assert.False(issue.GetProperty("isBlocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("blockedReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("blockedSinceUtc").ValueKind);
    }

    [Fact]
    public async Task Unblocking_an_issue_that_is_not_blocked_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/unblock",
            new { rowVersion = await RowVersionAsync(client, issueId) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Blocking_with_a_stale_row_version_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var staleRowVersion = await RowVersionAsync(client, issueId);
        var edit = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { title = "Moved on" });
        edit.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the vendor", rowVersion = staleRowVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Blocking_without_a_reason_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "", rowVersion = await RowVersionAsync(client, issueId) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_viewer_cannot_block()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var rowVersion = await RowVersionAsync(client, issueId);

        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, ProjectRole.Viewer);
        // The helper hands the client back authenticated as the admin who did the adding.
        client.DefaultRequestHeaders.Authorization =
            AuthenticationHeaderValue.Parse($"Bearer {viewer.AccessToken}");

        var readResponse = await client.GetAsync($"/api/issues/{issueId}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var response = await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the vendor", rowVersion });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Blocking_and_unblocking_are_recorded_in_the_activity_log()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        (await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the vendor", rowVersion = await RowVersionAsync(client, issueId) }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/unblock",
            new { rowVersion = await RowVersionAsync(client, issueId) }))
            .EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var actions = await db.ActivityLogEntries
            .Where(e => e.EntityId == issueId)
            .Select(e => e.Action)
            .ToListAsync();

        Assert.Contains("Blocked", actions);
        Assert.Contains("Unblocked", actions);
    }

    [Fact]
    public async Task A_blocked_issue_is_marked_on_the_board()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        (await client.PostAsJsonAsync(
            $"/api/issues/{issueId}/block",
            new { reason = "Waiting on the vendor", rowVersion = await RowVersionAsync(client, issueId) }))
            .EnsureSuccessStatusCode();

        var boardResponse = await client.GetAsync($"/api/boards/{seeded.BoardId}/issues");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();

        var card = board.GetProperty("columns").EnumerateArray()
            .SelectMany(c => c.GetProperty("issues").EnumerateArray())
            .Single(i => i.GetProperty("id").GetGuid() == issueId);

        Assert.True(card.GetProperty("isBlocked").GetBoolean());
    }
}
