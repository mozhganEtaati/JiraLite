using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Calendar;

/// <summary>
/// spec/15-calendar.md §15 — Due Dates and Sprint Timeline. Both views share one class (and one
/// SQL Server container) for the same reason as <see cref="Dashboard.DashboardTests"/>.
/// </summary>
public class CalendarTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CalendarTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> CreateIssueWithDueDateAsync(HttpClient client, Guid projectId, DateOnly dueDateUtc)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues",
            new { type = "Task", title = $"Task-{Guid.NewGuid():N}", dueDateUtc });
        response.EnsureSuccessStatusCode();
        var issue = await response.Content.ReadFromJsonAsync<JsonElement>();
        return issue.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateScrumBoardAsync(HttpClient client, Guid projectId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/boards", new { name, type = BoardType.Scrum });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateSprintAsync(
        HttpClient client, Guid boardId, string name, DateOnly plannedStartDateUtc, DateOnly plannedEndDateUtc)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/sprints", new { name, plannedStartDateUtc, plannedEndDateUtc });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Due_dates_returns_only_issues_inside_the_requested_range()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var inRangeIssueId = await CreateIssueWithDueDateAsync(client, seeded.ProjectId, new DateOnly(2026, 8, 10));
        var outOfRangeIssueId = await CreateIssueWithDueDateAsync(client, seeded.ProjectId, new DateOnly(2026, 9, 1));

        var response = await client.GetAsync(
            $"/api/projects/{seeded.ProjectId}/calendar/due-dates?from=2026-08-01&to=2026-08-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == inRangeIssueId);
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == outOfRangeIssueId);
    }

    [Fact]
    public async Task Due_dates_defaults_to_the_current_calendar_month_when_no_range_is_supplied()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var thisMonthIssueId = await CreateIssueWithDueDateAsync(client, seeded.ProjectId, firstOfThisMonth);
        var nextMonthIssueId = await CreateIssueWithDueDateAsync(client, seeded.ProjectId, firstOfThisMonth.AddMonths(1));

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/calendar/due-dates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == thisMonthIssueId);
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == nextMonthIssueId);
    }

    [Fact]
    public async Task Due_dates_rejects_a_to_earlier_than_from()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var response = await client.GetAsync(
            $"/api/projects/{seeded.ProjectId}/calendar/due-dates?from=2026-08-31&to=2026-08-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Calendar_is_forbidden_to_a_non_member_who_is_not_a_workspace_admin()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", outsider.AccessToken);

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/calendar/due-dates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Sprint_timeline_aggregates_two_scrum_boards_chronologically()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var boardA = await CreateScrumBoardAsync(client, seeded.ProjectId, "Scrum A");
        var boardB = await CreateScrumBoardAsync(client, seeded.ProjectId, "Scrum B");

        var laterSprintId = await CreateSprintAsync(
            client, boardA, "Sprint A1", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14));
        var earlierSprintId = await CreateSprintAsync(
            client, boardB, "Sprint B1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 14));

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/calendar/sprint-timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(earlierSprintId, items[0].GetProperty("id").GetGuid());
        Assert.Equal(laterSprintId, items[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Both_calendar_views_still_work_for_an_archived_project()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var issueId = await CreateIssueWithDueDateAsync(client, seeded.ProjectId, new DateOnly(2026, 8, 10));
        var boardId = await CreateScrumBoardAsync(client, seeded.ProjectId, "Scrum A");
        var sprintId = await CreateSprintAsync(
            client, boardId, "Sprint 1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 14));

        (await client.PostAsync($"/api/projects/{seeded.ProjectId}/archive", null)).EnsureSuccessStatusCode();

        var dueDatesResponse = await client.GetAsync(
            $"/api/projects/{seeded.ProjectId}/calendar/due-dates?from=2026-08-01&to=2026-08-31");
        Assert.Equal(HttpStatusCode.OK, dueDatesResponse.StatusCode);
        var dueDateItems = (await dueDatesResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(dueDateItems, i => i.GetProperty("id").GetGuid() == issueId);

        var timelineResponse = await client.GetAsync($"/api/projects/{seeded.ProjectId}/calendar/sprint-timeline");
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        var timelineItems = (await timelineResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(timelineItems, i => i.GetProperty("id").GetGuid() == sprintId);
    }

    [Fact]
    public async Task Sprint_timeline_for_a_nonexistent_project_returns_404()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);

        var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}/calendar/sprint-timeline");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
