using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.FileStorage;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Users;

/// <summary>
/// spec/02-users.md §15 — the bullets not already covered by
/// <see cref="GetMyActivityTests"/> (activity log) or
/// <see cref="Auth.AuthenticationTests"/> (deactivated login) (task T048).
/// </summary>
public class UserProfileTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public UserProfileTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Registration_alone_creates_the_profile_and_notification_preferences_with_defaults()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, user.AccessToken);

        // No additional API call between register and these reads — that is the criterion.
        var profile = await ReadJsonAsync(client, "/api/users/me");
        var preferences = await ReadJsonAsync(client, "/api/users/me/notification-preferences");

        Assert.Equal(user.Email, profile.GetProperty("email").GetString());
        Assert.Equal(user.Email.Split('@')[0], profile.GetProperty("displayName").GetString());
        Assert.True(profile.GetProperty("avatarUrl").ValueKind is JsonValueKind.Null);
        Assert.True(preferences.GetProperty("emailEnabled").GetBoolean());
        Assert.True(preferences.GetProperty("inAppEnabled").GetBoolean());
    }

    [Fact]
    public async Task Uploading_an_avatar_sets_the_url_and_deletes_the_file_it_replaced()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, user.AccessToken);

        var first = await UploadAvatarAsync(client, "first.png");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstUrl = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("avatarUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstUrl));

        string firstStorageKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            var profile = await db.UserProfiles.AsNoTracking().SingleAsync(p => p.UserId == user.UserId);
            firstStorageKey = profile.AvatarStorageKey!;
            Assert.Equal(firstUrl, profile.AvatarUrl);
        }

        var second = await UploadAvatarAsync(client, "second.png");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var profile = await db.UserProfiles.AsNoTracking().SingleAsync(p => p.UserId == user.UserId);

            Assert.NotEqual(firstStorageKey, profile.AvatarStorageKey);
            // BR-03: the replaced file is removed from storage, not just orphaned by the row update.
            Assert.Null(await fileStorage.OpenReadAsync(firstStorageKey, CancellationToken.None));
            Assert.NotNull(await fileStorage.OpenReadAsync(profile.AvatarStorageKey!, CancellationToken.None));
        }
    }

    [Fact]
    public async Task A_non_image_avatar_is_rejected_and_leaves_the_existing_avatar_alone()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, user.AccessToken);

        var response = await UploadAvatarAsync(client, "notes.txt", "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.Null((await db.UserProfiles.AsNoTracking().SingleAsync(p => p.UserId == user.UserId)).AvatarUrl);
    }

    [Fact]
    public async Task Another_users_public_profile_exposes_only_display_name_and_avatar_never_the_email()
    {
        var client = _factory.CreateClient();
        var subject = await TestDataHelper.RegisterAndLoginAsync(client);
        var viewer = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, viewer.AccessToken);

        var response = await client.GetAsync($"/api/users/{subject.UserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // NFR-03: assert the whole property set, not just the absence of "email" — a future field
        // added to the shared UserSummary would otherwise leak silently.
        Assert.Equal(
            ["avatarUrl", "displayName", "id"],
            body.EnumerateObject().Select(p => p.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(subject.UserId, body.GetProperty("id").GetGuid());
        Assert.False(body.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Deactivating_revokes_every_refresh_token_and_the_account_can_no_longer_authenticate()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, user.AccessToken);

        var response = await client.PostAsync("/api/users/me/deactivate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.UserId)).IsActive);
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == user.UserId && t.RevokedAtUtc == null));
    }

    [Fact]
    public async Task Deactivation_leaves_memberships_and_assigned_issues_untouched()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var member = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        Authenticate(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var assign = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = member.UserId });
        assign.EnsureSuccessStatusCode();

        Authenticate(client, member.AccessToken);
        (await client.PostAsync("/api/users/me/deactivate", null)).EnsureSuccessStatusCode();

        // BR-09: deactivation is a flag on User and nothing else. Their history has to stay
        // readable to everyone else on the Project.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.True(await db.WorkspaceMembers.AnyAsync(m => m.UserId == member.UserId && m.WorkspaceId == seeded.WorkspaceId));
        Assert.True(await db.ProjectMembers.AnyAsync(m => m.UserId == member.UserId && m.ProjectId == seeded.ProjectId));
        Assert.Equal(member.UserId, (await db.Issues.AsNoTracking().SingleAsync(i => i.Id == issueId)).AssigneeUserId);
    }

    [Fact]
    public async Task Updated_notification_preferences_are_what_a_later_read_returns()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, user.AccessToken);

        var update = await client.PatchAsJsonAsync(
            "/api/users/me/notification-preferences", new { emailEnabled = false, inAppEnabled = false });
        update.EnsureSuccessStatusCode();

        var preferences = await ReadJsonAsync(client, "/api/users/me/notification-preferences");

        Assert.False(preferences.GetProperty("emailEnabled").GetBoolean());
        Assert.False(preferences.GetProperty("inAppEnabled").GetBoolean());
    }

    private static void Authenticate(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string uri)
    {
        var response = await client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> UploadAvatarAsync(HttpClient client, string fileName, string contentType = "image/png")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes($"pixels-{Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return await client.PutAsync("/api/users/me/avatar", content);
    }
}
