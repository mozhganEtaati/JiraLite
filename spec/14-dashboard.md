# 14 — Dashboard

## 1. Overview

Covers three read-only views: My Tasks, My Projects, and Recent Activity. This is a query/projection context — it introduces no new entities, only composed reads over `Issue`, `ProjectMember`, and `ActivityLogEntry` ([00-project-overview.md](00-project-overview.md) §6, Activity & Reporting context). Compare to [02-users.md](02-users.md) `GET /api/users/me/activity`, which is actor-scoped to the caller only — Recent Activity here shows **all** members' actions across the caller's Workspaces.

## 2. Business Goal

Give every user a personalized landing view of what's assigned to them, which Projects they're active on, and what's recently happened around them — without any new write behavior.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a user, I can see all Issues assigned to me across every Project I'm a member of. |
| US-02 | As a user, I can see the list of Projects I'm actively a member of. |
| US-03 | As a user, I can see a feed of recent activity across the Workspaces I belong to. |

## 4. Functional Requirements

- FR-01: My Tasks returns Issues where `AssigneeUserId` = caller, across Projects where the caller has a `ProjectMember` record, excluding archived Projects and Done-column Issues by default.
- FR-02: My Projects returns Projects where the caller has an explicit `ProjectMember` record, including their role on each.
- FR-03: Recent Activity returns `ActivityLogEntry` rows whose `WorkspaceId` is one the caller belongs to **and** whose `ProjectId` is either `NULL` (a workspace/team-level action) or a Project the caller can view (per BR-06), newest first, paginated.

## 5. Non-Functional Requirements

- NFR-01: My Tasks query is indexed on (`AssigneeUserId`, `BoardColumnId`) per [09-issues.md](09-issues.md) NFR-02.
- NFR-02: Recent Activity query is indexed on (`WorkspaceId`, `OccurredAtUtc`) per [02-users.md](02-users.md) NFR-02.

## 6. Business Rules

- BR-01: My Tasks excludes Issues in archived Projects unless `includeArchived=true` is passed.
- BR-02: My Tasks excludes Issues whose current `BoardColumn.IsDoneColumn = true` unless `includeDone=true` is passed.
- BR-03: My Projects reflects only explicit `ProjectMember` records for the caller. It does **not** include every Project in Workspaces where the caller is a Workspace Admin — Workspace Admins can browse the full Project list separately via `GET /api/workspaces/{workspaceId}/projects` ([05-projects.md](05-projects.md)).
- BR-04: Recent Activity is scoped to Workspaces the caller currently belongs to. If the caller later leaves a Workspace, its entries stop appearing in their feed even though the underlying `ActivityLogEntry` rows are never deleted.
- BR-05: All three views are strictly read-only — no endpoint in this document creates, updates, or deletes any entity.
- BR-06: **Recent Activity never exposes activity from a Project the caller cannot otherwise view.** In addition to the Workspace scope (BR-04), an `ActivityLogEntry` row is only included if `ProjectId IS NULL`, or the caller has a `ProjectMember` record for that `ProjectId`, or the caller is that Workspace's `Admin` — the same visibility rule already enforced on Issues and Projects directly ([09-issues.md](09-issues.md) §14, [05-projects.md](05-projects.md) §14). Without this filter, a plain Workspace Member with no Project membership could see Issue titles/keys from Projects they are otherwise forbidden to open.

## 7. Database Entities

No new entities. This document composes reads over:

- `Issue` (My Tasks) — see [09-issues.md](09-issues.md)
- `Project`, `ProjectMember` (My Projects) — see [05-projects.md](05-projects.md)
- `ActivityLogEntry` (Recent Activity) — see [02-users.md](02-users.md)

## 8. Relationships

No new relationships. Existing relationships are queried, not modified.

## 9. API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/dashboard/my-tasks` | Authenticated | Issues assigned to the caller |
| GET | `/api/dashboard/my-projects` | Authenticated | Projects the caller is a member of |
| GET | `/api/dashboard/recent-activity` | Authenticated | Activity feed across the caller's Workspaces |

## 10. Request Examples

**My Tasks**
```http
GET /api/dashboard/my-tasks?includeDone=false&includeArchived=false&limit=25
Authorization: Bearer {accessToken}
```

**Recent Activity**
```http
GET /api/dashboard/recent-activity?limit=25
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**My Tasks — 200 OK**
```json
{
  "items": [
    {
      "id": "e5f6g7h8-...",
      "key": "JIRA-124",
      "title": "Support workspace invitations via email",
      "type": "Story",
      "priority": "High",
      "dueDateUtc": "2026-08-10",
      "projectKey": "JIRA",
      "columnName": "In Progress"
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

**My Projects — 200 OK**
```json
{
  "items": [
    { "id": "5e6f7a8b-...", "key": "JIRA", "name": "JiraLite Platform", "role": "ProjectAdmin" }
  ]
}
```

**Recent Activity — 200 OK**
```json
{
  "items": [
    {
      "id": "a1b2c3d4-...",
      "actor": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
      "summary": "moved Issue JIRA-123 to Done",
      "entityType": "Issue",
      "entityId": "e5f6g7h8-...",
      "occurredAtUtc": "2026-07-31T09:45:00Z"
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| includeDone / includeArchived | Optional boolean, default `false` |
| limit / cursor | Optional, per [19-api-guidelines.md](19-api-guidelines.md) §5 pagination rules |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Invalid pagination parameters | 400 Bad Request | |
| No Issues/Projects/activity to show | 200 OK, empty `items` | Not an error |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| My Tasks, My Projects, Recent Activity | Authenticated; each result set is inherently scoped to the caller's own assignments/memberships — no role check needed |

## 15. Acceptance Criteria

- Given a user assigned to Issues in two different Projects, when My Tasks is requested, then Issues from both appear, excluding Done-column and archived-Project Issues by default.
- Given a user who is a Workspace Admin but not an explicit `ProjectMember` on Project X, when My Projects is requested, then Project X does not appear.
- Given a user removed from a Workspace, when Recent Activity is requested afterward, then entries from that Workspace no longer appear.
- Given `includeDone=true`, when My Tasks is requested, then Issues in Done columns are included.
- Given a Workspace Member with no `ProjectMember` record on Project X, when Recent Activity is requested, then entries with `ProjectId = X` do not appear, even though entries with `ProjectId = NULL` from the same Workspace do (BR-06).

## 16. Future Improvements

- Dashboard widgets for overdue Issues, upcoming due dates.
- Configurable dashboard layout per user.
- Team-scoped activity filtering ([04-teams.md](04-teams.md) §16).
