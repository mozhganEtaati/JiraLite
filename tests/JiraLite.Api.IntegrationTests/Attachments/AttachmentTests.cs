using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Attachments;

public class AttachmentTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public AttachmentTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static MultipartFormDataContent BuildUpload(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task Developer_uploads_a_file_and_it_appears_in_the_list()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.PostAsync(
            $"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3, 4], "stack-trace.png", "image/png"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("stack-trace.png", body.GetProperty("fileName").GetString());
        Assert.Equal(4, body.GetProperty("sizeBytes").GetInt64());

        var listResponse = await client.GetAsync($"/api/issues/{issueId}/attachments");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(listBody.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Disallowed_extension_is_rejected_with_415()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.PostAsync(
            $"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3], "virus.exe", "application/octet-stream"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Uploaded_file_can_be_downloaded_with_the_original_content_and_filename()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        byte[] fileBytes = [10, 20, 30, 40, 50];
        var uploadResponse = await client.PostAsync($"/api/issues/{issueId}/attachments", BuildUpload(fileBytes, "notes.png", "image/png"));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = uploaded.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/attachments/{attachmentId}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        var downloadedBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileBytes, downloadedBytes);
    }

    [Fact]
    public async Task Preview_of_a_non_previewable_content_type_is_rejected_with_415()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var uploadResponse = await client.PostAsync(
            $"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3], "archive.zip", "application/zip"));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = uploaded.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/attachments/{attachmentId}/preview");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Preview_of_an_image_is_served_inline()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var uploadResponse = await client.PostAsync($"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3], "photo.png", "image/png"));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = uploaded.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/attachments/{attachmentId}/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task Uploader_deletes_their_own_attachment_and_the_file_is_removed()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var uploadResponse = await client.PostAsync($"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3], "photo.png", "image/png"));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = uploaded.GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var downloadResponse = await client.GetAsync($"/api/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task Non_uploader_developer_cannot_delete_someone_elses_attachment()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var uploadResponse = await client.PostAsync($"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3], "photo.png", "image/png"));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = uploaded.GetProperty("id").GetGuid();

        var otherDeveloper = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");
        client.DefaultRequestHeaders.Authorization = new("Bearer", otherDeveloper.AccessToken);

        var response = await client.DeleteAsync($"/api/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_download_but_not_upload()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var uploadResponse = await client.PostAsync($"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3], "photo.png", "image/png"));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = uploaded.GetProperty("id").GetGuid();

        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Viewer");
        client.DefaultRequestHeaders.Authorization = new("Bearer", viewer.AccessToken);

        var downloadResponse = await client.GetAsync($"/api/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);

        var uploadAttemptResponse = await client.PostAsync($"/api/issues/{issueId}/attachments", BuildUpload([1, 2, 3], "another.png", "image/png"));
        Assert.Equal(HttpStatusCode.Forbidden, uploadAttemptResponse.StatusCode);
    }
}
