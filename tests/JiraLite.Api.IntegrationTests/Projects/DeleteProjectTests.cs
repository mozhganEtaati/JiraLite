using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class DeleteProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public DeleteProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_a_non_archived_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_archived_project_cascades_and_detaches_activity_log_entries()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        // No activity entry is seeded here: CreateProject already writes a Project/"Created" entry
        // carrying this ProjectId, which is exactly what the detach assertion below needs. Seeding a
        // second one made the lookup ambiguous.
        await client.PostAsync($"/api/projects/{projectId}/archive", null);
        var response = await client.DeleteAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await verifyDb.Projects.AnyAsync(p => p.Id == projectId));
        Assert.False(await verifyDb.Boards.AnyAsync(b => b.ProjectId == projectId));
        // Assert over every matching entry rather than a single one, so adding another Project-scoped
        // activity write later strengthens this test instead of breaking it.
        var activityEntries = await verifyDb.ActivityLogEntries
            .Where(e => e.EntityId == projectId && e.EntityType == "Project")
            .ToListAsync();
        Assert.NotEmpty(activityEntries);
        Assert.All(activityEntries, entry =>
        {
            Assert.Null(entry.ProjectId);
            Assert.Equal(workspaceId, entry.WorkspaceId);
        });
    }

    /// <summary>
    /// spec/05-projects.md BR-06 — the cascade must reach the Project's contents, not just its
    /// Boards. The two tests above delete an *empty* Project, which never exercises the
    /// Issue -> BoardColumn / Sprint / parent-Issue foreign keys (all NO ACTION per
    /// spec/18-database.md §9), so a Project with any Issue in it used to fail with a 500.
    /// </summary>
    [Fact]
    public async Task Deleting_an_archived_project_cascades_its_issues_and_their_contents()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var projectId = seeded.ProjectId;

        // A Story with a Subtask under it, so the self-referencing Issue.ParentIssueId FK is live.
        var storyId = await TestDataHelper.CreateIssueAsync(client, projectId, "Story");
        var subtaskId = await TestDataHelper.CreateIssueAsync(client, projectId, "Subtask", parentIssueId: storyId);

        // A comment, a label and an attachment — each is a separate FK into Issue.
        var commentResponse = await client.PostAsJsonAsync($"/api/issues/{storyId}/comments", new { body = "Looking at this now." });
        commentResponse.EnsureSuccessStatusCode();

        var labelResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/labels", new { name = "backend", color = "#8B5CF6" });
        labelResponse.EnsureSuccessStatusCode();
        var labelId = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/issues/{storyId}/labels", new { labelId })).EnsureSuccessStatusCode();

        var upload = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(fileContent, "file", "screenshot.png");
        var attachmentResponse = await client.PostAsync($"/api/issues/{storyId}/attachments", upload);
        attachmentResponse.EnsureSuccessStatusCode();

        // Move the Story onto the Done column and into a Sprint, so both NO ACTION FKs
        // (Issue.BoardColumnId, Issue.SprintId) point at rows this delete has to remove.
        var scrumResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Sprint Board", type = "Scrum" });
        scrumResponse.EnsureSuccessStatusCode();
        var scrumBoardId = (await scrumResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var sprintResponse = await client.PostAsJsonAsync($"/api/boards/{scrumBoardId}/sprints", new
        {
            name = "Sprint 1",
            goal = "Close the first slice.",
            plannedStartDateUtc = "2026-01-05",
            plannedEndDateUtc = "2026-01-19",
        });
        sprintResponse.EnsureSuccessStatusCode();
        var sprintId = (await sprintResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId = storyId })).EnsureSuccessStatusCode();

        var storyRowVersion = (await (await client.GetAsync($"/api/issues/{storyId}"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetString();
        (await client.PatchAsJsonAsync($"/api/issues/{storyId}/move", new { boardColumnId = seeded.DoneColumnId, rowVersion = storyRowVersion }))
            .EnsureSuccessStatusCode();

        // Capture the stored file's key before the delete removes the row that names it.
        string storageKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            storageKey = await db.Attachments.Where(a => a.IssueId == storyId).Select(a => a.StorageKey).SingleAsync();
        }

        await client.PostAsync($"/api/projects/{projectId}/archive", null);
        var response = await client.DeleteAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await verifyDb.Projects.AnyAsync(p => p.Id == projectId));
        Assert.False(await verifyDb.Issues.AnyAsync(i => i.ProjectId == projectId));
        Assert.False(await verifyDb.Comments.AnyAsync(c => c.IssueId == storyId));
        Assert.False(await verifyDb.Attachments.AnyAsync(a => a.IssueId == storyId));
        Assert.False(await verifyDb.IssueLabels.AnyAsync(il => il.IssueId == storyId || il.IssueId == subtaskId));
        Assert.False(await verifyDb.Labels.AnyAsync(l => l.ProjectId == projectId));
        Assert.False(await verifyDb.Sprints.AnyAsync(s => s.ProjectId == projectId));
        Assert.False(await verifyDb.Boards.AnyAsync(b => b.ProjectId == projectId));
        Assert.False(await verifyDb.ProjectMembers.AnyAsync(m => m.ProjectId == projectId));

        // BR-06 deletes the stored file too, not just the Attachment row.
        var fileStorage = verifyScope.ServiceProvider.GetRequiredService<IFileStorage>();
        Assert.Null(await fileStorage.OpenReadAsync(storageKey, CancellationToken.None));
    }
}
