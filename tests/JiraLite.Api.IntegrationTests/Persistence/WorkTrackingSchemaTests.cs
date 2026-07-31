using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Persistence;

public class WorkTrackingSchemaTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public WorkTrackingSchemaTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<(User User, Project Project, Board Board, BoardColumn Column)> SeedAsync(JiraLiteDbContext db)
    {
        var now = DateTime.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"schema-{Guid.NewGuid():N}@example.com", PasswordHash = "x", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", OwnerUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        var workspace = new Workspace { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "WS", CreatedByUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        var project = new Project { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Key = "JIRA", Name = "P1", CreatedByUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Main", Type = BoardType.Kanban, CreatedAtUtc = now, UpdatedAtUtc = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "To Do", DisplayOrder = 0, IsDefault = true, IsDoneColumn = false };
        db.AddRange(user, org, workspace, project, board, column);
        await db.SaveChangesAsync();
        return (user, project, board, column);
    }

    private static Issue NewIssue(Project project, BoardColumn column, User user, int number) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = project.Id,
        Number = number,
        Key = $"{project.Key}-{number}",
        Type = IssueType.Story,
        Title = "Title",
        Priority = IssuePriority.Medium,
        BoardColumnId = column.Id,
        Rank = "0|100000:",
        ReporterUserId = user.Id,
        CreatedByUserId = user.Id,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedByUserId = user.Id,
        UpdatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Issue_number_is_unique_per_project()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var (user, project, _, column) = await SeedAsync(db);

        db.Issues.Add(NewIssue(project, column, user, 1));
        await db.SaveChangesAsync();

        db.Issues.Add(NewIssue(project, column, user, 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Issue_insert_fails_for_a_nonexistent_board_column()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var (user, project, _, column) = await SeedAsync(db);

        var issue = NewIssue(project, column, user, 1);
        issue.BoardColumnId = Guid.NewGuid();
        db.Issues.Add(issue);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Label_name_is_unique_per_project_case_insensitively()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var (_, project, _, _) = await SeedAsync(db);

        db.Labels.Add(new Label { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "regression", Color = "#E11D48", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Labels.Add(new Label { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "REGRESSION", Color = "#E11D48", CreatedAtUtc = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_an_issue_cascades_its_comments_and_attachments()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var (user, project, _, column) = await SeedAsync(db);

        var issue = NewIssue(project, column, user, 1);
        db.Issues.Add(issue);
        db.Comments.Add(new Comment { Id = Guid.NewGuid(), IssueId = issue.Id, AuthorUserId = user.Id, Body = "hi", CreatedAtUtc = DateTime.UtcNow });
        db.Attachments.Add(new Attachment { Id = Guid.NewGuid(), IssueId = issue.Id, UploadedByUserId = user.Id, FileName = "a.png", StorageKey = "k", ContentType = "image/png", SizeBytes = 1, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Issues.Remove(issue);
        await db.SaveChangesAsync();

        Assert.False(await db.Comments.AnyAsync(c => c.IssueId == issue.Id));
        Assert.False(await db.Attachments.AnyAsync(a => a.IssueId == issue.Id));
    }
}
