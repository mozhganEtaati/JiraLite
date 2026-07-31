using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

public class DeleteIssueTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public DeleteIssueTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_a_story_cascades_its_subtasks()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        var subtaskId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId);

        var response = await client.DeleteAsync($"/api/issues/{storyId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await db.Issues.AnyAsync(i => i.Id == storyId));
        Assert.False(await db.Issues.AnyAsync(i => i.Id == subtaskId));
    }

    [Fact]
    public async Task Deleting_an_epic_detaches_its_children_instead_of_deleting_them()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var epicId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Epic");
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story", parentIssueId: epicId);

        var response = await client.DeleteAsync($"/api/issues/{epicId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await db.Issues.AnyAsync(i => i.Id == epicId));
        var story = await db.Issues.SingleAsync(i => i.Id == storyId);
        Assert.Null(story.ParentIssueId);
    }

    [Fact]
    public async Task Deleting_an_issue_removes_its_comments_and_attachment_files_from_storage()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        string storageKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            storageKey = await fileStorage.SaveAsync("attachments", "note.txt", new MemoryStream(Encoding.UTF8.GetBytes("hi")), CancellationToken.None);

            db.Comments.Add(new Comment { Id = Guid.NewGuid(), IssueId = issueId, AuthorUserId = admin.UserId, Body = "hello", CreatedAtUtc = DateTime.UtcNow });
            db.Attachments.Add(new Attachment { Id = Guid.NewGuid(), IssueId = issueId, UploadedByUserId = admin.UserId, FileName = "note.txt", StorageKey = storageKey, ContentType = "text/plain", SizeBytes = 2, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/issues/{issueId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var verifyFileStorage = verifyScope.ServiceProvider.GetRequiredService<IFileStorage>();
        Assert.False(await verifyDb.Comments.AnyAsync(c => c.IssueId == issueId));
        Assert.False(await verifyDb.Attachments.AnyAsync(a => a.IssueId == issueId));
        Assert.Null(await verifyFileStorage.OpenReadAsync(storageKey, CancellationToken.None));
    }

    [Fact]
    public async Task Developer_is_forbidden_from_deleting_an_issue()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", developer.AccessToken);
        var response = await client.DeleteAsync($"/api/issues/{issueId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
