# 14 — Dashboard

## 1. Overview

Covers four read-only views: My Tasks, My Projects, Recent Activity, and My Stats. This is a query/projection context — it introduces no new entities, only composed reads over `Issue`, `ProjectMember`, and `ActivityLogEntry` ([00-project-overview.md](00-project-overview.md) §6, Activity & Reporting context). Compare to [02-users.md](02-users.md) `GET /api/users/me/activity`, which is actor-scoped to the caller only — Recent Activity here shows **all** members' actions across the caller's Workspaces.

## 2. Business Goal

Give every user a personalized landing view of what's assigned to them, which Projects they're active on, and what's recently happened around them — without any new write behavior.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a user, I can see all Issues assigned to me across every Project I'm a member of. |
| US-02 | As a user, I can see the list of Projects I'm actively a member of. |
| US-03 | As a user, I can see a feed of recent activity across the Workspaces I belong to. |
| US-04 | As a user, I can see at a glance how my assigned work is distributed and how much of it I have been moving, without reading a list. |

## 4. Functional Requirements

- FR-01: My Tasks returns Issues where `AssigneeUserId` = caller, across Projects where the caller has a `ProjectMember` record, excluding archived Projects and Done-column Issues by default.
- FR-02: My Projects returns Projects where the caller has an explicit `ProjectMember` record, including their role on each.
- FR-03: Recent Activity returns `ActivityLogEntry` rows whose `WorkspaceId` is one the caller belongs to **and** whose `ProjectId` is either `NULL` (a workspace/team-level action) or a Project the caller can view (per BR-06), newest first, paginated.
- FR-04: My Stats returns aggregate counts over the same Issue set as FR-01 — totals (assigned, open, done, overdue, due within seven days), a count per `BoardColumn`, a count per priority — plus a per-day count of the caller's own Issue actions over a trailing window of `days`.

## 5. Non-Functional Requirements

- NFR-01: My Tasks query is indexed on (`AssigneeUserId`, `BoardColumnId`) per [09-issues.md](09-issues.md) NFR-02.
- NFR-02: Recent Activity query is indexed on (`WorkspaceId`, `OccurredAtUtc`) per [02-users.md](02-users.md) NFR-02.
- NFR-03: My Stats introduces no index of its own. Its Issue aggregates reuse NFR-01, and its activity window seeks (`ActorUserId`, `OccurredAtUtc`) — the index [02-users.md](02-users.md) already requires.

## 6. Business Rules

- BR-01: My Tasks excludes Issues in archived Projects unless `includeArchived=true` is passed.
- BR-02: My Tasks excludes Issues whose current `BoardColumn.IsDoneColumn = true` unless `includeDone=true` is passed.
- BR-03: My Projects reflects only explicit `ProjectMember` records for the caller. It does **not** include every Project in Workspaces where the caller is a Workspace Admin — Workspace Admins can browse the full Project list separately via `GET /api/workspaces/{workspaceId}/projects` ([05-projects.md](05-projects.md)).
- BR-04: Recent Activity is scoped to Workspaces the caller currently belongs to. If the caller later leaves a Workspace, its entries stop appearing in their feed even though the underlying `ActivityLogEntry` rows are never deleted.
- BR-05: All four views are strictly read-only — no endpoint in this document creates, updates, or deletes any entity.
- BR-06: **Recent Activity never exposes activity from a Project the caller cannot otherwise view.** In addition to the Workspace scope (BR-04), an `ActivityLogEntry` row is only included if `ProjectId IS NULL`, or the caller has a `ProjectMember` record for that `ProjectId`, or the caller is that Workspace's `Admin` — the same visibility rule already enforced on Issues and Projects directly ([09-issues.md](09-issues.md) §14, [05-projects.md](05-projects.md) §14). Without this filter, a plain Workspace Member with no Project membership could see Issue titles/keys from Projects they are otherwise forbidden to open.
- BR-07: My Stats counts the same Issues as My Tasks — assigned to the caller, in Projects where they hold a `ProjectMember` record, archived Projects excluded (BR-01) — but **counts Done-column Issues rather than excluding them** (contrast BR-02). A completion figure without its denominator is not a figure, so `includeDone` and `includeArchived` are not accepted here.
- BR-08: The My Stats activity window counts only `ActivityLogEntry` rows where the caller is the actor and `EntityType = 'Issue'`, split into `Created`, `StatusChanged`, and `Commented`. Other actions the caller took (creating a Workspace, editing a Project) are out of frame. No Workspace or Project filter applies, because these are the caller's own actions — the same actor-scoping as [02-users.md](02-users.md) FR-05.
- BR-09: The activity window is dense and ends on the current UTC day: a day the caller did nothing comes back with zero counts, never omitted. `days` is clamped to 7–90, defaulting to 14. Day boundaries are UTC, matching `Issue.DueDateUtc` and every other date in the API.
- BR-10: Status counts are grouped by `BoardColumn.Name`, not by `BoardColumnId`. Two Projects that both named a lane "In Progress" are one figure to the person reading it. Done columns sort last, so the reading runs from untouched work to finished work.

## 7. Database Entities

No new entities. This document composes reads over:

- `Issue` (My Tasks) — see [09-issues.md](09-issues.md)
- `Project`, `ProjectMember` (My Projects) — see [05-projects.md](05-projects.md)
- `ActivityLogEntry` (Recent Activity, My Stats) — see [02-users.md](02-users.md)
- `BoardColumn` (My Stats status counts) — see [06-boards.md](06-boards.md)

## 8. Relationships

No new relationships. Existing relationships are queried, not modified.

## 9. API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/dashboard/my-tasks` | Authenticated | Issues assigned to the caller |
| GET | `/api/dashboard/my-projects` | Authenticated | Projects the caller is a member of |
| GET | `/api/dashboard/recent-activity` | Authenticated | Activity feed across the caller's Workspaces |
| GET | `/api/dashboard/my-stats` | Authenticated | Aggregate counts behind the dashboard charts |

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

**My Stats**
```http
GET /api/dashboard/my-stats?days=14
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

**My Stats — 200 OK**
```json
{
  "days": 14,
  "totals": { "assigned": 12, "open": 9, "done": 3, "overdue": 2, "dueSoon": 4 },
  "byStatus": [
    { "name": "To Do", "count": 5, "isDone": false },
    { "name": "In Progress", "count": 4, "isDone": false },
    { "name": "Done", "count": 3, "isDone": true }
  ],
  "byPriority": [
    { "priority": "Critical", "count": 1 },
    { "priority": "High", "count": 4 },
    { "priority": "Medium", "count": 6 },
    { "priority": "Low", "count": 1 }
  ],
  "activity": [
    { "date": "2026-07-24", "created": 0, "moved": 0, "commented": 0 },
    { "date": "2026-07-25", "created": 2, "moved": 1, "commented": 3 }
  ]
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| includeDone / includeArchived | Optional boolean, default `false` |
| limit / cursor | Optional, per [19-api-guidelines.md](19-api-guidelines.md) §5 pagination rules |
| days | Optional integer, clamped to 7–90, default `14` — out-of-range values are clamped, not rejected |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Invalid pagination parameters | 400 Bad Request | |
| No Issues/Projects/activity to show | 200 OK, empty `items` | Not an error |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| My Tasks, My Projects, Recent Activity, My Stats | Authenticated; each result set is inherently scoped to the caller's own assignments/memberships — no role check needed |

## 15. Acceptance Criteria

- Given a user assigned to Issues in two different Projects, when My Tasks is requested, then Issues from both appear, excluding Done-column and archived-Project Issues by default.
- Given a user who is a Workspace Admin but not an explicit `ProjectMember` on Project X, when My Projects is requested, then Project X does not appear.
- Given a user removed from a Workspace, when Recent Activity is requested afterward, then entries from that Workspace no longer appear.
- Given `includeDone=true`, when My Tasks is requested, then Issues in Done columns are included.
- Given a Workspace Member with no `ProjectMember` record on Project X, when Recent Activity is requested, then entries with `ProjectId = X` do not appear, even though entries with `ProjectId = NULL` from the same Workspace do (BR-06).
- Given a caller with an overdue Issue, one due within seven days, one moved to a Done column, and one in an archived Project, when My Stats is requested, then the totals read 3 assigned / 2 open / 1 done / 1 overdue / 1 due soon, and every priority comes back including the empty ones (FR-04, BR-07).
- Given `days=2`, when My Stats is requested, then the window is clamped to 7, every one of those days is present in order, and the last is the current UTC day (BR-09).
- Given a Project member who has been assigned nothing and has touched no Issue, when My Stats is requested, then every total and every day in the window is zero, even though they can view the Project (BR-07, BR-08).

## 16. Future Improvements

- Configurable dashboard layout per user.
- Team-scoped activity filtering ([04-teams.md](04-teams.md) §16).
