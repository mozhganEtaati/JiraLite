using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Ranking;

/// <summary>
/// Task T046, concurrency half. The RowVersion check on Issue is what stops two simultaneous drags
/// from interleaving into a lost update; these tests fire the racing requests for real rather than
/// simulating staleness with a hand-made byte array (which is what
/// <see cref="Backlog.RepositionIssueRankTests"/> already covers).
/// </summary>
public class ConcurrentRankTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private const int Racers = 6;

    private readonly JiraLiteApiFactory _factory;

    public ConcurrentRankTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Concurrent_rank_updates_with_the_same_row_version_leave_exactly_one_winner()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var anchors = new List<Guid>();
        for (var i = 0; i < Racers; i++)
        {
            anchors.Add(await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: $"Anchor {i}"));
        }

        var mover = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Mover");
        var rowVersion = await GetRowVersionAsync(client, mover);

        // Every racer carries the same RowVersion but targets a different destination, so a lost
        // update would be visible as a Rank belonging to a request that was told it lost.
        var responses = await Task.WhenAll(anchors.Select(afterIssueId =>
            SendAsync(client, admin.AccessToken, HttpMethod.Patch, $"/api/issues/{mover}/rank",
                new { afterIssueId, rowVersion })));

        AssertExactlyOneWinner(responses);
        await AssertRankIsUniqueWithinTheListAsync(seeded.ProjectId, mover);
    }

    [Fact]
    public async Task Concurrent_moves_with_the_same_row_version_leave_exactly_one_winner()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var rowVersion = await GetRowVersionAsync(client, issueId);

        // Alternating between the two columns: whichever request wins, the persisted BoardColumnId
        // must be the one that request asked for, and the losers must not have half-applied theirs.
        var targets = Enumerable.Range(0, Racers)
            .Select(i => i % 2 == 0 ? seeded.DefaultColumnId : seeded.DoneColumnId)
            .ToList();

        var responses = await Task.WhenAll(targets.Select(boardColumnId =>
            SendAsync(client, admin.AccessToken, HttpMethod.Patch, $"/api/issues/{issueId}/move",
                new { boardColumnId, rowVersion })));

        var winner = AssertExactlyOneWinner(responses);
        var claimedColumnId = winner.GetProperty("boardColumnId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var stored = await db.Issues.AsNoTracking().SingleAsync(i => i.Id == issueId);

        Assert.Equal(claimedColumnId, stored.BoardColumnId);
        // The winner's response echoes the RowVersion the row now carries; if a loser had also
        // committed, the stored version would have moved past it.
        Assert.Equal(winner.GetProperty("rowVersion").GetString(), Convert.ToBase64String(stored.RowVersion));
    }

    [Fact]
    public async Task A_rank_update_and_a_move_racing_on_the_same_row_version_cannot_both_commit()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var other = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Other");
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Contended");
        var rowVersion = await GetRowVersionAsync(client, issueId);

        var responses = await Task.WhenAll(
            SendAsync(client, admin.AccessToken, HttpMethod.Patch, $"/api/issues/{issueId}/rank",
                new { afterIssueId = other, rowVersion }),
            SendAsync(client, admin.AccessToken, HttpMethod.Patch, $"/api/issues/{issueId}/move",
                new { boardColumnId = seeded.DoneColumnId, rowVersion }));

        AssertExactlyOneWinner(responses);
    }

    /// <summary>
    /// Asserts one 200 and all-losers-409, and returns the winner's body. Any other status is
    /// reported with the offending bodies — a 500 here would mean the concurrency exception escaped
    /// the handler instead of being translated, which is the failure this whole file guards.
    /// </summary>
    private static JsonElement AssertExactlyOneWinner(IReadOnlyList<(HttpStatusCode Status, string Body)> responses)
    {
        var winners = responses.Where(r => r.Status == HttpStatusCode.OK).ToList();
        var losers = responses.Where(r => r.Status == HttpStatusCode.Conflict).ToList();

        Assert.True(
            winners.Count == 1 && losers.Count == responses.Count - 1,
            $"Expected exactly one 200 and {responses.Count - 1} × 409, got: " +
            string.Join(" | ", responses.Select(r => $"{(int)r.Status} {Truncate(r.Body)}")));

        return JsonDocument.Parse(winners[0].Body).RootElement.Clone();
    }

    private async Task AssertRankIsUniqueWithinTheListAsync(Guid projectId, Guid issueId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        var ranks = await db.Issues
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId && i.SprintId == null)
            .Select(i => new { i.Id, i.Rank })
            .ToListAsync();

        Assert.Equal(ranks.Count, ranks.Select(r => r.Rank).Distinct().Count());
        // The contended row must still be ranked in the same list, not left in some partial state.
        Assert.Contains(ranks, r => r.Id == issueId);
    }

    private static async Task<string> GetRowVersionAsync(HttpClient client, Guid issueId)
    {
        var response = await client.GetAsync($"/api/issues/{issueId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("rowVersion").GetString()!;
    }

    /// <summary>
    /// Sends with an explicit Authorization header rather than the client's default one: these
    /// requests are in flight simultaneously and must not depend on shared mutable client state.
    /// </summary>
    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpClient client, string accessToken, HttpMethod method, string uri, object payload)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string Truncate(string body) => body.Length <= 160 ? body : body[..160] + "…";
}
