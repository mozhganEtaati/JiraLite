using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

/// <summary>
/// spec/24-reports.md §15 — GET /api/sprints/{sprintId}/report. One class, one SQL Server
/// container: every case here is a read over data the same seeding path produces, so splitting
/// them across classes would multiply suite runtime for no isolation benefit.
/// </summary>
public class SprintReportTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public SprintReportTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Seeded(
        HttpClient Client,
        TestDataHelper.RegisteredUser Admin,
        Guid WorkspaceId,
        Guid ProjectId,
        Guid BoardId,
        Guid ToDoColumnId,
        Guid InProgressColumnId,
        Guid DoneColumnId);

    /// <summary>A Project with a Scrum Board of its own — Sprints only exist on Scrum Boards (BR-08).</summary>
    private async Task<Seeded> SeedAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var project = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var boardResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/boards", new { name = "Scrum", type = "Scrum" });
        boardResponse.EnsureSuccessStatusCode();
        var boardId = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // A Scrum Board starts with only To Do and Done (spec/06-boards.md BR-02 is all it has to
        // satisfy). A middle lane is added here, and because AddColumn appends, it lands *after*
        // Done in DisplayOrder — which is what makes the ordering assertions below prove the
        // done-last rule rather than just echoing the column order back.
        (await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns",
            new { name = "In Progress", isDefault = false, isDoneColumn = false })).EnsureSuccessStatusCode();

        var board = await (await client.GetAsync($"/api/boards/{boardId}")).Content.ReadFromJsonAsync<JsonElement>();
        var columns = board.GetProperty("columns").EnumerateArray().ToList();
        Guid ColumnNamed(string name) => columns.First(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

        return new Seeded(
            client, admin, project.WorkspaceId, project.ProjectId, boardId,
            ColumnNamed("To Do"), ColumnNamed("In Progress"), ColumnNamed("Done"));
    }

    private static async Task<Guid> CreateSprintAsync(
        HttpClient client, Guid boardId, DateOnly start, DateOnly end, bool started = true)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/sprints", new
        {
            name = $"Sprint-{Guid.NewGuid():N}",
            goal = "Ship it",
            plannedStartDateUtc = start.ToString("yyyy-MM-dd"),
            plannedEndDateUtc = end.ToString("yyyy-MM-dd")
        });
        response.EnsureSuccessStatusCode();
        var sprintId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        if (started)
        {
            (await client.PostAsync($"/api/sprints/{sprintId}/start", null)).EnsureSuccessStatusCode();
        }

        return sprintId;
    }

    private static async Task<string> RowVersionAsync(HttpClient client, Guid issueId)
    {
        var issue = await (await client.GetAsync($"/api/issues/{issueId}")).Content.ReadFromJsonAsync<JsonElement>();
        return issue.GetProperty("rowVersion").GetString()!;
    }

    /// <summary>Creates an Issue, puts it in the Sprint, and applies whichever of the optional facets the case needs.</summary>
    private static async Task<Guid> AddIssueAsync(
        HttpClient client,
        Guid projectId,
        Guid sprintId,
        string type = "Story",
        Guid? assigneeUserId = null,
        decimal? estimate = null,
        DateOnly? dueDateUtc = null,
        Guid? moveToColumnId = null,
        string? blockedReason = null,
        Guid? parentIssueId = null)
    {
        var issueId = await TestDataHelper.CreateIssueAsync(client, projectId, type, parentIssueId);

        if (parentIssueId is null)
        {
            (await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId })).EnsureSuccessStatusCode();
        }

        if (assigneeUserId is not null || estimate is not null || dueDateUtc is not null)
        {
            (await client.PatchAsJsonAsync($"/api/issues/{issueId}", new
            {
                assigneeUserId,
                estimate,
                dueDateUtc = dueDateUtc?.ToString("yyyy-MM-dd")
            })).EnsureSuccessStatusCode();
        }

        if (moveToColumnId is not null)
        {
            (await client.PatchAsJsonAsync($"/api/issues/{issueId}/move", new
            {
                boardColumnId = moveToColumnId,
                rowVersion = await RowVersionAsync(client, issueId)
            })).EnsureSuccessStatusCode();
        }

        if (blockedReason is not null)
        {
            (await client.PostAsJsonAsync($"/api/issues/{issueId}/block", new
            {
                reason = blockedReason,
                rowVersion = await RowVersionAsync(client, issueId)
            })).EnsureSuccessStatusCode();
        }

        return issueId;
    }

    private static async Task<JsonElement> ReportAsync(HttpClient client, Guid sprintId)
    {
        var response = await client.GetAsync($"/api/sprints/{sprintId}/report");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] ReasonCodes(JsonElement report) =>
        report.GetProperty("health").GetProperty("reasons").EnumerateArray()
            .Select(r => r.GetProperty("code").GetString()!)
            .ToArray();

    /// <summary>A sprint long enough that being at 0% on day one is not itself a problem.</summary>
    private static (DateOnly Start, DateOnly End) RoomyWindow()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return (today, today.AddDays(100));
    }

    [Fact]
    public async Task Subtasks_are_excluded_from_every_count()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        var parentId = await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        // A Subtask rides into the Sprint on its parent (spec/07-backlog.md BR-06) — counting it
        // beside the parent would count the same work twice.
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, type: "Subtask", parentIssueId: parentId);

        var report = await ReportAsync(s.Client, sprintId);

        Assert.Equal(1, report.GetProperty("progress").GetProperty("issues").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Points_are_summed_and_unestimated_issues_are_reported_beside_them()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId, estimate: 5m, moveToColumnId: s.DoneColumnId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, estimate: 3m);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId);

        var report = await ReportAsync(s.Client, sprintId);
        var points = report.GetProperty("progress").GetProperty("points");

        Assert.Equal(8m, points.GetProperty("total").GetDecimal());
        Assert.Equal(5m, points.GetProperty("done").GetDecimal());
        Assert.Equal(3m, points.GetProperty("open").GetDecimal());
        Assert.Equal(1, points.GetProperty("unestimatedIssues").GetInt32());
        // 5 of 8 points is 63%; 1 of 3 issues is 33%. Both are reported, because a sprint whose
        // finished work carries the estimates reads very differently by each measure.
        Assert.Equal(33, report.GetProperty("progress").GetProperty("donePercentByIssues").GetInt32());
        Assert.Equal(63, report.GetProperty("progress").GetProperty("donePercentByPoints").GetInt32());
    }

    [Fact]
    public async Task Status_buckets_group_by_column_name_with_done_last()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, moveToColumnId: s.InProgressColumnId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, moveToColumnId: s.InProgressColumnId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, moveToColumnId: s.DoneColumnId);

        var report = await ReportAsync(s.Client, sprintId);
        var buckets = report.GetProperty("byStatus").EnumerateArray().ToList();

        Assert.Equal(["To Do", "In Progress", "Done"], buckets.Select(b => b.GetProperty("name").GetString()));
        Assert.Equal([1, 2, 1], buckets.Select(b => b.GetProperty("count").GetInt32()));
        Assert.True(buckets[^1].GetProperty("isDone").GetBoolean());
    }

    [Fact]
    public async Task Unassigned_work_gets_its_own_row_rather_than_being_dropped()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId, assigneeUserId: s.Admin.UserId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId);

        var report = await ReportAsync(s.Client, sprintId);
        var rows = report.GetProperty("byAssignee").EnumerateArray().ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(s.Admin.UserId, rows[0].GetProperty("user").GetProperty("id").GetGuid());
        // Unassigned sorts last and is exactly what someone opening this page is looking for.
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("user").ValueKind);
        Assert.Equal(1, rows[1].GetProperty("total").GetInt32());
        Assert.Equal(1, report.GetProperty("risks").GetProperty("unassignedCount").GetInt32());
    }

    [Fact]
    public async Task A_planned_sprint_has_no_pace_and_no_verdict()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end, started: false);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId);

        var report = await ReportAsync(s.Client, sprintId);

        Assert.Equal(JsonValueKind.Null, report.GetProperty("pace").ValueKind);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("health").GetProperty("state").ValueKind);
        Assert.Empty(report.GetProperty("health").GetProperty("reasons").EnumerateArray());
    }

    [Fact]
    public async Task An_empty_sprint_is_on_track_rather_than_an_error()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        var report = await ReportAsync(s.Client, sprintId);

        Assert.Equal("OnTrack", report.GetProperty("health").GetProperty("state").GetString());
        Assert.Equal(["EmptySprint"], ReasonCodes(report));
        Assert.Equal(0, report.GetProperty("progress").GetProperty("issues").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_sprint_on_its_last_day_with_nothing_done_is_off_track()
    {
        var s = await SeedAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // The planned window must span at least a day (spec/08-sprints.md BR-04), but pace is
        // measured from StartedAtUtc (BR-03), which starting it now sets to today — so the sprint
        // is on the last day of a one-day run.
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, today.AddDays(-1), today);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId);

        var report = await ReportAsync(s.Client, sprintId);

        Assert.Equal(100, report.GetProperty("pace").GetProperty("expectedPercent").GetInt32());
        Assert.Equal("OffTrack", report.GetProperty("health").GetProperty("state").GetString());
        // One reason per concern, at the severity it actually reached — never WellBehindPace and
        // BehindPace together, which would say the same thing twice.
        Assert.Equal(["WellBehindPace"], ReasonCodes(report));
    }

    [Fact]
    public async Task One_blocked_issue_among_many_puts_the_sprint_at_risk()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        for (var i = 0; i < 9; i++)
        {
            await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        }
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, blockedReason: "Waiting on the payments vendor");

        var report = await ReportAsync(s.Client, sprintId);

        Assert.Equal("AtRisk", report.GetProperty("health").GetProperty("state").GetString());
        Assert.Equal(["BlockedWork"], ReasonCodes(report));

        var blocked = report.GetProperty("risks").GetProperty("blocked").EnumerateArray().Single();
        Assert.Equal("Waiting on the payments vendor", blocked.GetProperty("blockedReason").GetString());
        Assert.Equal(0, blocked.GetProperty("blockedDays").GetInt32());
    }

    [Fact]
    public async Task Two_blockers_above_a_fifth_of_open_work_is_off_track()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, blockedReason: "Waiting on the vendor");
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, blockedReason: "Waiting on the auth library bump");

        var report = await ReportAsync(s.Client, sprintId);

        Assert.Equal("OffTrack", report.GetProperty("health").GetProperty("state").GetString());
        Assert.Equal(["HeavilyBlocked"], ReasonCodes(report));
    }

    [Fact]
    public async Task A_lone_blocker_stays_at_risk_however_small_the_sprint()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, blockedReason: "Waiting on the vendor");

        var report = await ReportAsync(s.Client, sprintId);

        // Half the sprint by share, but one incident by count — and a verdict of OffTrack for a
        // single blocker is one people learn to ignore (BR-07's count floor).
        Assert.Equal("AtRisk", report.GetProperty("health").GetProperty("state").GetString());
        Assert.Equal(["BlockedWork"], ReasonCodes(report));
    }

    [Fact]
    public async Task Overdue_work_and_work_due_after_the_sprint_are_both_reported()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId, dueDateUtc: start.AddDays(-3));
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, dueDateUtc: end.AddDays(5));
        await AddIssueAsync(s.Client, s.ProjectId, sprintId);

        var report = await ReportAsync(s.Client, sprintId);
        var risks = report.GetProperty("risks");

        Assert.Equal(1, risks.GetProperty("overdueCount").GetInt32());
        Assert.Equal(1, risks.GetProperty("dueAfterSprintEndCount").GetInt32());
        Assert.Equal("AtRisk", report.GetProperty("health").GetProperty("state").GetString());
        // Every distinct problem is named, not just the first one found.
        Assert.Equal(["OverdueWork", "DueAfterSprintEnd"], ReasonCodes(report));
    }

    [Fact]
    public async Task A_healthy_sprint_reports_on_track_with_nothing_to_say()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId, assigneeUserId: s.Admin.UserId, estimate: 3m);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId, assigneeUserId: s.Admin.UserId, estimate: 2m,
            moveToColumnId: s.DoneColumnId);

        var report = await ReportAsync(s.Client, sprintId);

        Assert.Equal("OnTrack", report.GetProperty("health").GetProperty("state").GetString());
        Assert.Empty(report.GetProperty("health").GetProperty("reasons").EnumerateArray());
    }

    [Fact]
    public async Task A_completed_sprint_reports_what_was_carried_out_of_it()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        await AddIssueAsync(s.Client, s.ProjectId, sprintId, moveToColumnId: s.DoneColumnId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId);
        await AddIssueAsync(s.Client, s.ProjectId, sprintId);

        (await s.Client.PostAsJsonAsync(
            $"/api/sprints/{sprintId}/complete",
            new { moveIncompleteIssuesToSprintId = (Guid?)null })).EnsureSuccessStatusCode();

        var report = await ReportAsync(s.Client, sprintId);

        // Completion empties the sprint of everything unfinished (spec/08-sprints.md BR-05), so
        // what is left reads 100% — the carried-forward figure is what stops that being a lie.
        Assert.Equal(1, report.GetProperty("progress").GetProperty("issues").GetProperty("total").GetInt32());
        Assert.Equal(100, report.GetProperty("progress").GetProperty("donePercentByIssues").GetInt32());
        Assert.Equal(2, report.GetProperty("sprint").GetProperty("carriedForwardIssueCount").GetInt32());
    }

    [Fact]
    public async Task A_non_member_cannot_read_the_report()
    {
        var s = await SeedAsync();
        var (start, end) = RoomyWindow();
        var sprintId = await CreateSprintAsync(s.Client, s.BoardId, start, end);

        var outsider = await TestDataHelper.RegisterAndLoginAsync(s.Client);
        s.Client.DefaultRequestHeaders.Authorization =
            AuthenticationHeaderValue.Parse($"Bearer {outsider.AccessToken}");

        var response = await s.Client.GetAsync($"/api/sprints/{sprintId}/report");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_sprint_is_a_404()
    {
        var s = await SeedAsync();

        var response = await s.Client.GetAsync($"/api/sprints/{Guid.NewGuid()}/report");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
