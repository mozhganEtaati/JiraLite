using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Labels;

public class LabelTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public LabelTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Project_admin_creates_a_label_and_it_appears_in_the_list()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "regression", color = "#E11D48" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var listResponse = await client.GetAsync($"/api/projects/{seeded.ProjectId}/labels");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(listBody.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Duplicate_label_name_is_rejected_case_insensitively()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        (await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "regression", color = "#E11D48" })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "REGRESSION", color = "#7C3AED" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_color_format_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "regression", color = "red" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Developer_can_attach_an_existing_label_without_project_admin()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var labelResponse = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "regression", color = "#E11D48" });
        var label = await labelResponse.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetGuid();
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");
        client.DefaultRequestHeaders.Authorization = new("Bearer", developer.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var getIssueResponse = await client.GetAsync($"/api/issues/{issueId}");
        var issueBody = await getIssueResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(issueBody.GetProperty("labels").EnumerateArray());
    }

    [Fact]
    public async Task Attaching_a_label_from_a_different_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seededA = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var seededB = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var labelInB = await client.PostAsJsonAsync($"/api/projects/{seededB.ProjectId}/labels", new { name = "b-only", color = "#E11D48" });
        var label = await labelInB.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetGuid();
        var issueInA = await TestDataHelper.CreateIssueAsync(client, seededA.ProjectId);

        var response = await client.PostAsJsonAsync($"/api/issues/{issueInA}/labels", new { labelId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_label_removes_its_associations_without_deleting_the_issue()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var labelResponse = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "regression", color = "#E11D48" });
        var label = await labelResponse.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetGuid();
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        (await client.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId })).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/labels/{labelId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getIssueResponse = await client.GetAsync($"/api/issues/{issueId}");
        Assert.Equal(HttpStatusCode.OK, getIssueResponse.StatusCode);
        var issueBody = await getIssueResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(issueBody.GetProperty("labels").EnumerateArray());
    }

    [Fact]
    public async Task Developer_cannot_create_a_label_definition()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", developer.AccessToken);
        var response = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "nope", color = "#E11D48" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Detaching_a_label_not_on_the_issue_returns_404()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var labelResponse = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "regression", color = "#E11D48" });
        var label = await labelResponse.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetGuid();
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.DeleteAsync($"/api/issues/{issueId}/labels/{labelId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
