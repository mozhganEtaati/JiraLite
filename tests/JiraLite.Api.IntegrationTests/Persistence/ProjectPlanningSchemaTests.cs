using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Persistence;

public class ProjectPlanningSchemaTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ProjectPlanningSchemaTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Project_key_is_unique_per_workspace_case_insensitively()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        var user = new User { Id = Guid.NewGuid(), Email = "schema-test@example.com", PasswordHash = "x", IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", OwnerUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var workspace = new Workspace { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "WS", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.Users.Add(user);
        db.Organizations.Add(org);
        db.Workspaces.Add(workspace);
        db.Projects.Add(new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "JIRA", Name = "P1", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Projects.Add(new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "jira", Name = "P2", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sprint_active_uniqueness_index_rejects_a_second_active_sprint_on_the_same_board()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        var user = new User { Id = Guid.NewGuid(), Email = "schema-test-2@example.com", PasswordHash = "x", IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", OwnerUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var workspace = new Workspace { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "WS", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var project = new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "JIRA", Name = "P1", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Main", Type = BoardType.Scrum, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.AddRange(user, org, workspace, project, board);
        db.Sprints.Add(new Sprint { Id = Guid.NewGuid(), BoardId = board.Id, ProjectId = project.Id, Name = "S1", Status = SprintStatus.Active, PlannedStartDateUtc = DateOnly.FromDateTime(DateTime.UtcNow), PlannedEndDateUtc = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Sprints.Add(new Sprint { Id = Guid.NewGuid(), BoardId = board.Id, ProjectId = project.Id, Name = "S2", Status = SprintStatus.Active, PlannedStartDateUtc = DateOnly.FromDateTime(DateTime.UtcNow), PlannedEndDateUtc = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
