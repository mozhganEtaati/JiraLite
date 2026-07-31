# 06 — Boards

## 1. Overview

Covers Boards (Scrum or Kanban) and their Columns. A `BoardColumn` **is** the effective status of any Issue placed on it — JiraLite does not maintain a separate global status enum (see [00-project-overview.md](00-project-overview.md) §8 and [09-issues.md](09-issues.md)). Moving an Issue between columns, including across Boards, is an Issue action documented in [09-issues.md](09-issues.md); this document covers Board/Column structure only.

## 2. Business Goal

Let each Project visualize its work the way the team works — Scrum sprints or continuous Kanban flow — with columns the team defines themselves, while guaranteeing every Issue always has an unambiguous status.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Project Admin, I can create additional Boards for my Project (e.g., a support Kanban board alongside the main Scrum board). |
| US-02 | As a Project Admin, I can add, rename, reorder, and remove columns on a Board. |
| US-03 | As a Project member, I can view a Board and see Issues grouped by column. |
| US-04 | As a Project Admin, I can mark a column as the "Done" column so Sprint completion logic knows which Issues are finished. |

## 4. Functional Requirements

- FR-01: Creating a Project automatically creates one default Kanban Board named "Main Board" with three default columns: "To Do", "In Progress", "Done" (`IsDoneColumn = true`) — this guarantees every new Issue always has somewhere to land.
- FR-02: A Project Admin can create additional Boards (Scrum or Kanban).
- FR-03: A Project Admin can add, rename, reorder, and delete columns on any Board in their Project.
- FR-04: A Project Admin can mark a column as the default landing column for new Issues, and mark one or more columns as "Done" columns.
- FR-05: Any Project member can retrieve a Board's current Issues, grouped by column.

## 5. Non-Functional Requirements

- NFR-01: Column reordering uses a concurrency token (`RowVersion`) to prevent two simultaneous drag-and-drop operations from silently overwriting each other's order.

## 6. Business Rules

- BR-01: A Board must always have at least one Column. Deleting the last remaining column on a Board is rejected.
- BR-02: Each Board must have exactly one column marked `IsDefault = true` (the landing column for new Issues assigned to that Board) and at least one column marked `IsDoneColumn = true` (used by Sprint completion — see [08-sprints.md](08-sprints.md)).
- BR-03: A Column cannot be deleted while it has Issues currently placed on it — Issues must be moved to another column first (see [09-issues.md](09-issues.md) Move Issue).
- BR-04: A Project must always retain at least one Board. Deleting the last remaining Board is rejected.
- BR-05: A Board cannot be deleted while it has Issues currently placed on any of its columns.
- BR-06: An Issue's current `BoardColumnId` determines both its status label and which single Board it currently belongs to — an Issue is on exactly one Board at a time. Moving it to a column on a different Board transfers it to that Board.
- BR-07: When an Issue is created without an explicit column, it is placed in the Project's default Board's default column (see [09-issues.md](09-issues.md)).
- BR-08: A `Scrum`-type Board is associated with zero or more Sprints, one active at a time (see [08-sprints.md](08-sprints.md)). A `Kanban`-type Board has no Sprint association — its columns represent continuous flow.
- BR-09: A Board cannot be deleted while any Sprint — `Planned`, `Active`, or `Completed` — references it (`Sprint.BoardId`). This includes historical, already-Completed Sprints: `Sprint.BoardId` is a database foreign key with `ON DELETE NO ACTION` ([18-database.md](18-database.md) §6), so without this application-level guard the deletion would fail as an unhandled constraint violation rather than a clean `409 Conflict`. A Board with historical Sprints can never be deleted in V1 via `DELETE /api/boards/{boardId}` (no Sprint-transfer or archival tooling exists — see §16). This guard applies only to the standalone Board-delete endpoint — it does not block a Project-level delete ([05-projects.md](05-projects.md) BR-06), which removes the Board and its Sprints together in the same orchestrated transaction.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Board

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| ProjectId | Guid (FK → Project) | No | |
| Name | string(100) | No | |
| Type | string(20) | No | `Scrum` \| `Kanban` |
| DisplayOrder | int | No | Ordering for board tabs in UI |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### BoardColumn

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| BoardId | Guid (FK → Board) | No | |
| Name | string(100) | No | |
| DisplayOrder | int | No | Left-to-right position |
| IsDefault | bool | No | Landing column for new Issues; exactly one `true` per Board |
| IsDoneColumn | bool | No | Marks a completion state; at least one `true` per Board |
| RowVersion | rowversion | No | Concurrency token for reordering |

## 8. Relationships

- `Project (1) → Board (N)`
- `Board (1) → BoardColumn (N)`
- `BoardColumn (1) → Issue (N)` — an Issue's current column (see [09-issues.md](09-issues.md))
- `Board (1) → Sprint (N)` — Scrum boards only (see [08-sprints.md](08-sprints.md))

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/projects/{projectId}/boards` | Project Member or Workspace Admin | List Boards |
| POST | `/api/projects/{projectId}/boards` | Project Admin or Workspace Admin | Create Board |
| GET | `/api/boards/{boardId}` | Project Member or Workspace Admin | Get Board + columns |
| PATCH | `/api/boards/{boardId}` | Project Admin or Workspace Admin | Rename Board |
| DELETE | `/api/boards/{boardId}` | Project Admin or Workspace Admin | Delete Board |
| GET | `/api/boards/{boardId}/issues` | Project Member or Workspace Admin | Get Issues grouped by column |
| POST | `/api/boards/{boardId}/columns` | Project Admin or Workspace Admin | Add column |
| PATCH | `/api/boards/{boardId}/columns/{columnId}` | Project Admin or Workspace Admin | Rename column / toggle flags |
| DELETE | `/api/boards/{boardId}/columns/{columnId}` | Project Admin or Workspace Admin | Delete column |
| PATCH | `/api/boards/{boardId}/columns/reorder` | Project Admin or Workspace Admin | Bulk reorder columns |

## 10. Request Examples

**Create Board**
```http
POST /api/projects/{projectId}/boards
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Support Kanban",
  "type": "Kanban"
}
```

**Add column**
```http
POST /api/boards/{boardId}/columns
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Code Review",
  "isDefault": false,
  "isDoneColumn": false
}
```

**Reorder columns**
```http
PATCH /api/boards/{boardId}/columns/reorder
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "orderedColumnIds": [
    "c1a1e2e6-...",
    "b1a4e9a9-...",
    "d1a2b3c4-..."
  ]
}
```

## 11. Response Examples

**GET /api/boards/{boardId} — 200 OK**
```json
{
  "id": "8a9b0c1d-...",
  "projectId": "5e6f7a8b-...",
  "name": "Main Board",
  "type": "Kanban",
  "columns": [
    { "id": "c1a1e2e6-...", "name": "To Do", "displayOrder": 0, "isDefault": true, "isDoneColumn": false },
    { "id": "b1a4e9a9-...", "name": "In Progress", "displayOrder": 1, "isDefault": false, "isDoneColumn": false },
    { "id": "d1a2b3c4-...", "name": "Done", "displayOrder": 2, "isDefault": false, "isDoneColumn": true }
  ]
}
```

**GET /api/boards/{boardId}/issues — 200 OK**
```json
{
  "columns": [
    {
      "columnId": "c1a1e2e6-...",
      "issues": [
        { "id": "e5f6g7h8-...", "key": "JIRA-123", "title": "Fix login redirect", "type": "Bug", "priority": "High", "assignee": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" } }
      ]
    }
  ]
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| Board.Name | Required, 1–100 chars |
| Board.Type | Required, one of `Scrum`, `Kanban`; immutable after creation |
| Column.Name | Required, 1–100 chars |
| Column.IsDefault / IsDoneColumn | Boolean |
| orderedColumnIds | Must contain exactly the set of column ids currently on the Board, no duplicates |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Delete the last remaining Column on a Board | 409 Conflict | BR-01 |
| Delete a Column that currently has Issues | 409 Conflict | BR-03 |
| Delete the last remaining Board in a Project | 409 Conflict | BR-04 |
| Delete a Board that currently has Issues | 409 Conflict | BR-05 |
| Delete a Board that has any Sprint (including Completed) referencing it | 409 Conflict | BR-09 |
| Unset `IsDefault` on the only default column without setting another | 400 Bad Request | BR-02 |
| Unset `IsDoneColumn` on the only Done column without setting another | 400 Bad Request | BR-02 |
| Reorder payload doesn't match current column set | 400 Bad Request | |
| Board/Column not found | 404 Not Found | |
| Non-admin attempts management action | 403 Forbidden | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Boards, columns, and issues-by-column | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` |
| Create/edit/delete Boards and Columns, reorder columns | `ProjectMember.Role = ProjectAdmin` **or** `WorkspaceMember.Role = Admin` |

## 15. Acceptance Criteria

- Given a new Project, when it is created, then a default Kanban "Main Board" exists with "To Do" (default), "In Progress", and "Done" (done) columns.
- Given a Board with Issues on one of its columns, when deletion of that column is attempted, then it is rejected until the Issues are moved elsewhere.
- Given a Project with exactly one Board, when deletion of that Board is attempted, then it is rejected.
- Given a Scrum Board with a Completed Sprint and zero Issues currently on its columns, when deletion of that Board is attempted, then it is still rejected (BR-09).
- Given a valid reordering payload covering all of a Board's columns, when submitted, then each column's `DisplayOrder` is updated atomically.
- Given a Board, when its Issues-by-column view is requested, then Issues are grouped under the column matching their current `BoardColumnId`.

## 16. Future Improvements

- Per-column WIP (work-in-progress) limits.
- Column-level automation (e.g., auto-assign on entering a column).
- Board-level filters/swimlanes (by assignee, label, epic).
- Cross-project boards.
- Sprint-transfer or archival tooling that would allow retiring a Board with historical Sprints (BR-09) without losing Sprint history.
