using System.ComponentModel;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Mcp;
using JiraLite.Api.Features.Boards;
using JiraLite.Api.Features.Comments;
using JiraLite.Api.Features.Dashboard;
using JiraLite.Api.Features.Issues;
using JiraLite.Api.Features.Projects;
using JiraLite.Api.Features.Sprints;
using ModelContextProtocol.Server;

namespace JiraLite.Api.Features.Mcp;

/// <summary>
/// The read half of the tool surface in spec/23-mcp-server.md §14.
///
/// Every method is a two-line adapter: name the policy the equivalent HTTP endpoint requires, then
/// call that endpoint's handler. No query lives here (NFR-04) — a tool that could not be expressed
/// this way would mean the underlying slice is incomplete, and the slice is where the fix belongs.
///
/// Descriptions are written for a model reader: what the tool returns, and when to prefer it over
/// its neighbours.
/// </summary>
[McpServerToolType]
public class ReadTools(McpToolGateway gateway, JiraLiteDbContext db)
{
    private const string NotAMember = "You are not a member of this project.";

    [McpServerTool(Name = "list_my_issues")]
    [Description("Lists issues assigned to the calling user across every project they belong to, newest first. Use this for questions like \"what am I working on\" or \"what's on my plate\"; use list_issues when the question is about a specific project rather than about the caller.")]
    public Task<object?> ListMyIssuesAsync(
        [Description("Include issues sitting in a Done column. Defaults to false.")] bool? includeDone = null,
        [Description("Include issues belonging to archived projects. Defaults to false.")] bool? includeArchived = null,
        [Description("Maximum issues to return, 1-100. Defaults to 25.")] int? limit = null,
        CancellationToken cancellationToken = default) =>
        // No policy: the slice already restricts results to issues assigned to the caller in
        // projects they belong to, so being authenticated is the whole requirement.
        gateway.InvokeAsync("list_my_issues",
            () => GetMyTasks.Handler.Handle(includeDone, includeArchived, limit, null, gateway.User, db, cancellationToken));

    [McpServerTool(Name = "list_projects")]
    [Description("Lists the projects in a workspace that the caller can see. Start here when you know the workspace but not the project id.")]
    public Task<object?> ListProjectsAsync(
        [Description("The workspace id (GUID).")] Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        gateway.InvokeAsync("list_projects", "WorkspaceMember", "You are not a member of this workspace.",
            [("workspaceId", workspaceId)],
            () => ListProjects.Handler.Handle(workspaceId, db, cancellationToken));

    [McpServerTool(Name = "list_issues")]
    [Description("Lists issues in one project, with optional filters. Returns a summary of each issue; call get_issue when you need the description, labels, or the rowVersion required to move an issue.")]
    public Task<object?> ListIssuesAsync(
        [Description("The project id (GUID).")] Guid projectId,
        [Description("Filter by issue type: Epic, Story, Task, Bug, or Subtask.")] string? type = null,
        [Description("Filter by board column id (GUID) — a column is the issue's status.")] Guid? boardColumnId = null,
        [Description("Filter by assignee user id (GUID).")] Guid? assigneeUserId = null,
        [Description("Filter by priority: Lowest, Low, Medium, High, or Highest.")] string? priority = null,
        [Description("Filter by sprint id (GUID).")] Guid? sprintId = null,
        [Description("Maximum issues to return, 1-100. Defaults to 25.")] int? limit = null,
        CancellationToken cancellationToken = default) =>
        gateway.InvokeAsync("list_issues", "ProjectView", NotAMember, [("projectId", projectId)],
            () => ListIssues.Handler.Handle(projectId, type, boardColumnId, assigneeUserId, priority, null, sprintId, limit, null, db, cancellationToken));

    [McpServerTool(Name = "get_issue")]
    [Description("Returns one issue in full, including its description, labels, subtask count, and its rowVersion — which move_issue requires.")]
    public Task<object?> GetIssueAsync(
        [Description("The issue id (GUID).")] Guid issueId,
        CancellationToken cancellationToken = default) =>
        gateway.InvokeAsync("get_issue", "IssueView", NotAMember, [("issueId", issueId)],
            () => GetIssue.Handler.Handle(issueId, db, cancellationToken));

    [McpServerTool(Name = "list_board")]
    [Description("Returns a board and its ordered columns. A column is an issue's status, so call this first to find the target column id for move_issue.")]
    public Task<object?> ListBoardAsync(
        [Description("The board id (GUID).")] Guid boardId,
        CancellationToken cancellationToken = default) =>
        gateway.InvokeAsync("list_board", "BoardView", NotAMember, [("boardId", boardId)],
            () => GetBoard.Handler.Handle(boardId, db, cancellationToken));

    [McpServerTool(Name = "list_sprints")]
    [Description("Lists the sprints on a board, including which one is currently active.")]
    public Task<object?> ListSprintsAsync(
        [Description("The board id (GUID).")] Guid boardId,
        CancellationToken cancellationToken = default) =>
        gateway.InvokeAsync("list_sprints", "BoardView", NotAMember, [("boardId", boardId)],
            () => ListSprints.Handler.Handle(boardId, db, cancellationToken));

    [McpServerTool(Name = "list_comments")]
    [Description("Lists the comments on an issue, oldest first.")]
    public Task<object?> ListCommentsAsync(
        [Description("The issue id (GUID).")] Guid issueId,
        [Description("Maximum comments to return, 1-100. Defaults to 25.")] int? limit = null,
        CancellationToken cancellationToken = default) =>
        gateway.InvokeAsync("list_comments", "IssueView", NotAMember, [("issueId", issueId)],
            () => ListComments.Handler.Handle(issueId, limit, null, db, cancellationToken));
}
