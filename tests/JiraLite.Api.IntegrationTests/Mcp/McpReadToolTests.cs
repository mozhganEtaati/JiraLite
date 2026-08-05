using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Mcp;

/// <summary>
/// spec/23-mcp-server.md §15 — the advertised tool list matches §14 exactly, nothing excluded by
/// BR-06 is reachable, and a read tool returns what its HTTP counterpart returns.
/// </summary>
public class McpReadToolTests : IClassFixture<McpEnabledApiFactory>, IAsyncLifetime
{
    private readonly McpEnabledApiFactory _factory;

    public McpReadToolTests(McpEnabledApiFactory factory) => _factory = factory;

    /// <summary>spec/23-mcp-server.md §14, verbatim.</summary>
    private static readonly string[] ExpectedTools =
    [
        "add_comment", "create_issue", "get_issue", "list_board", "list_comments",
        "list_issues", "list_my_issues", "list_projects", "list_sprints", "move_issue", "update_issue"
    ];

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_advertised_tool_list_matches_the_specification_exactly()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var pat = await McpTestClient.IssueTokenAsync(client, user.AccessToken);

        await using var mcp = await McpTestClient.ConnectAsync(_factory, pat);
        var tools = (await mcp.ListToolsAsync()).Select(t => t.Name).OrderBy(n => n).ToArray();

        Assert.Equal(ExpectedTools, tools);
    }

    [Fact]
    public async Task No_destructive_or_administrative_tool_is_advertised()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var pat = await McpTestClient.IssueTokenAsync(client, user.AccessToken);

        await using var mcp = await McpTestClient.ConnectAsync(_factory, pat);
        var tools = (await mcp.ListToolsAsync()).Select(t => t.Name).ToArray();

        // BR-06 — asserted rather than left to a reviewer noticing a new tool slipping in.
        Assert.DoesNotContain(tools, name => name.StartsWith("delete_", StringComparison.Ordinal));
        foreach (var forbidden in new[]
                 {
                     "delete_issue", "delete_comment", "delete_project", "delete_sprint", "delete_board",
                     "add_project_member", "remove_project_member", "change_project_member_role",
                     "create_board", "add_column", "create_label", "create_sprint", "start_sprint",
                     "complete_sprint", "upload_attachment", "download_attachment", "admin_overview"
                 })
        {
            Assert.DoesNotContain(forbidden, tools);
        }
    }

    [Fact]
    public async Task List_my_issues_returns_the_same_issues_as_the_dashboard_endpoint()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, user.AccessToken);

        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var assign = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = user.UserId });
        assign.EnsureSuccessStatusCode();

        var httpTasks = await (await client.GetAsync("/api/dashboard/my-tasks")).Content.ReadFromJsonAsync<JsonElement>();
        var httpIds = httpTasks.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).OrderBy(id => id).ToArray();

        var pat = await McpTestClient.IssueTokenAsync(client, user.AccessToken);
        await using var mcp = await McpTestClient.ConnectAsync(_factory, pat);
        var toolResult = await mcp.CallAsync("list_my_issues", new Dictionary<string, object?>());
        var toolIds = toolResult.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).OrderBy(id => id).ToArray();

        Assert.Equal(httpIds, toolIds);
        Assert.Contains(issueId, toolIds);
    }

    [Fact]
    public async Task Get_issue_returns_the_row_version_move_issue_needs()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, user.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Readable");

        var pat = await McpTestClient.IssueTokenAsync(client, user.AccessToken);
        await using var mcp = await McpTestClient.ConnectAsync(_factory, pat);
        var issue = await mcp.CallAsync("get_issue", new Dictionary<string, object?> { ["issueId"] = issueId });

        Assert.Equal("Readable", issue.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(issue.GetProperty("rowVersion").GetString()));
    }

    [Fact]
    public async Task A_viewer_can_read_a_project()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, owner.AccessToken);
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {owner.AccessToken}");
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Visible to viewers");

        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, owner.AccessToken, "Viewer");

        var pat = await McpTestClient.IssueTokenAsync(client, viewer.AccessToken);
        await using var mcp = await McpTestClient.ConnectAsync(_factory, pat);
        var issue = await mcp.CallAsync("get_issue", new Dictionary<string, object?> { ["issueId"] = issueId });

        Assert.Equal("Visible to viewers", issue.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_non_member_is_refused_rather_than_shown_the_issue()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, owner.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var strangerClient = _factory.CreateClient();
        var stranger = await TestDataHelper.RegisterAndLoginAsync(strangerClient);
        var pat = await McpTestClient.IssueTokenAsync(strangerClient, stranger.AccessToken);

        await using var mcp = await McpTestClient.ConnectAsync(_factory, pat);
        var error = await mcp.CallExpectingErrorAsync(
            "get_issue", new Dictionary<string, object?> { ["issueId"] = issueId });

        Assert.Contains("not a member", error, StringComparison.OrdinalIgnoreCase);
    }
}
