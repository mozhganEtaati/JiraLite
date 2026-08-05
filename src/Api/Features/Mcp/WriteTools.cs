using System.ComponentModel;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Mcp;
using JiraLite.Api.Common.Notifications;
using JiraLite.Api.Features.Comments;
using JiraLite.Api.Features.Issues;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace JiraLite.Api.Features.Mcp;

/// <summary>
/// The write half of the tool surface in spec/23-mcp-server.md §14.
///
/// Each tool calls the same handler the HTTP endpoint calls, which is why activity-log writes and
/// notification dispatch happen here for free (FR-07) — they are not reimplemented, they are the
/// handler's own behaviour.
///
/// Deliberately absent (BR-06): every delete tool, board and column management, label definitions,
/// sprint lifecycle, project/workspace/member administration, and attachments. Those stay
/// HTTP-only, where a human is unambiguously in the loop.
/// McpReadToolTests.No_destructive_or_administrative_tool_is_advertised asserts this list stays
/// absent rather than trusting a reviewer to notice.
/// </summary>
[McpServerToolType]
public class WriteTools(
    McpToolGateway gateway,
    JiraLiteDbContext db,
    NotificationDispatcher notificationDispatcher,
    IAuthorizationService authorizationService,
    IHttpContextAccessor httpContextAccessor)
{
    private const string CannotContribute = "You need the Developer or Project Admin role on this project to do that.";

    [McpServerTool(Name = "create_issue")]
    [Description("Creates an issue in a project. It lands in the board's first column and at the bottom of the backlog. Requires the Developer or Project Admin role.")]
    public Task<object?> CreateIssueAsync(
        [Description("The project id (GUID).")] Guid projectId,
        [Description("Issue type: Epic, Story, Task, Bug, or Subtask. A Subtask requires parentIssueId.")] string type,
        [Description("Short summary, at most 255 characters.")] string title,
        [Description("Optional longer description.")] string? description = null,
        [Description("Priority: Low, Medium, High, or Critical. Defaults to Medium.")] string? priority = null,
        [Description("Parent issue id (GUID) — required for a Subtask, and used to place a Story/Task/Bug under an Epic.")] Guid? parentIssueId = null,
        [Description("Assignee user id (GUID). Must already be a member of the project.")] Guid? assigneeUserId = null,
        [Description("Estimate in story points, 0-999.99.")] decimal? estimate = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateIssue.Request(type, title, description, priority, parentIssueId, assigneeUserId, null, estimate);

        return gateway.InvokeAsync("create_issue", "ProjectContribute", CannotContribute,
            [("projectId", projectId)], request,
            () => CreateIssue.Handler.Handle(projectId, request, gateway.User, db, cancellationToken));
    }

    [McpServerTool(Name = "update_issue")]
    [Description("Updates an issue's fields. Only the fields you supply change; omitted fields are left alone and cannot be cleared through this tool. Requires the Developer or Project Admin role.")]
    public Task<object?> UpdateIssueAsync(
        [Description("The issue id (GUID).")] Guid issueId,
        [Description("New title, at most 255 characters.")] string? title = null,
        [Description("New description.")] string? description = null,
        [Description("New priority: Low, Medium, High, or Critical.")] string? priority = null,
        [Description("New assignee user id (GUID). Must be a member of the project.")] Guid? assigneeUserId = null,
        [Description("New estimate in story points, 0-999.99.")] decimal? estimate = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EditIssue.Request(title, description, priority, assigneeUserId, null, estimate, null);

        return gateway.InvokeAsync("update_issue", "IssueContribute", CannotContribute,
            [("issueId", issueId)], request,
            () => EditIssue.Handler.Handle(
                issueId, request, httpContextAccessor.HttpContext!, authorizationService, db, notificationDispatcher, cancellationToken));
    }

    [McpServerTool(Name = "move_issue")]
    [Description("Moves an issue to a different board column, which is how its status changes. Notifies the assignee and reporter. Call get_issue first: rowVersion is required, and a mismatch means someone else changed the issue in the meantime — re-read it and try again rather than retrying blindly. Requires the Developer or Project Admin role.")]
    public Task<object?> MoveIssueAsync(
        [Description("The issue id (GUID).")] Guid issueId,
        [Description("The target board column id (GUID) — see list_board.")] Guid boardColumnId,
        [Description("The issue's current rowVersion, exactly as returned by get_issue.")] string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var request = new MoveIssue.Request(boardColumnId, rowVersion);

        return gateway.InvokeAsync("move_issue", "IssueContribute", CannotContribute,
            [("issueId", issueId)], request,
            () => MoveIssue.Handler.Handle(issueId, request, gateway.User, db, notificationDispatcher, cancellationToken));
    }

    [McpServerTool(Name = "add_comment")]
    [Description("Adds a comment to an issue. Notifies the assignee, the reporter, and everyone who has already commented. Requires the Developer or Project Admin role.")]
    public Task<object?> AddCommentAsync(
        [Description("The issue id (GUID).")] Guid issueId,
        [Description("The comment body, at most 10000 characters.")] string body,
        CancellationToken cancellationToken = default)
    {
        var request = new AddComment.Request(body);

        return gateway.InvokeAsync("add_comment", "IssueContribute", CannotContribute,
            [("issueId", issueId)], request,
            () => AddComment.Handler.Handle(issueId, request, gateway.User, db, notificationDispatcher, cancellationToken));
    }
}
