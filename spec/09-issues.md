# 09 — Issues

## 1. Overview

`Issue` is the central work-item entity: Task, Story, Bug, Epic, and Subtask are all the **same entity** distinguished by a `Type` discriminator, with hierarchy expressed via a self-referencing `ParentIssueId` (see [00-project-overview.md](00-project-overview.md) §8). An Issue's status is its current `BoardColumnId` ([06-boards.md](06-boards.md)); its backlog position is its `Rank` and `SprintId` ([07-backlog.md](07-backlog.md), [08-sprints.md](08-sprints.md)).

## 2. Business Goal

Provide one consistent work-item model that supports Jira-style hierarchy (Epic → Story/Task/Bug → Subtask) without the complexity of separate entity types per Issue kind.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Developer, I can create an Issue with a title, description, type, and priority. |
| US-02 | As a Developer, I can assign an Issue to a teammate and set a due date and estimate. |
| US-03 | As a Developer, I can break a Story down into Subtasks. |
| US-04 | As a Developer, I can group related Stories/Tasks/Bugs under an Epic. |
| US-05 | As a Project member, I can move an Issue to a different column to update its status. |
| US-06 | As a Project Admin, I can delete an Issue that is no longer needed. |

## 4. Functional Requirements

- FR-01: A Developer or Project Admin can create an Issue of type `Epic`, `Story`, `Task`, `Bug`, or `Subtask` within a Project.
- FR-02: A Developer or Project Admin can edit an Issue's title, description, priority, due date, estimate, and assignee.
- FR-03: A Project Admin or Workspace Admin can reassign an Issue's reporter.
- FR-04: A Developer or Project Admin can move an Issue to a different `BoardColumn`, including one on a different Board within the same Project.
- FR-05: A Project Admin or Workspace Admin can delete an Issue.
- FR-06: Any Project member can list and filter Issues by type, status (column), assignee, priority, label, or sprint.
- FR-07: Any Project member can list the Subtasks of a given Issue.
- FR-08: A Developer or Project Admin can mark an Issue as blocked, recording why and when.
- FR-09: A Developer or Project Admin can clear an Issue's blocked state.

## 5. Non-Functional Requirements

- NFR-01: Issue `Description` accepts Markdown text up to 50,000 characters; raw text is stored as-is (rendering happens client-side per [00-project-overview.md](00-project-overview.md) Assumption 11), with basic stripping of embedded script-like content before storage as a security baseline.
- NFR-02: Issue list/filter queries are indexed on (`ProjectId`, `BoardColumnId`), (`ProjectId`, `SprintId`), and (`ProjectId`, `AssigneeUserId`).

## 6. Business Rules

**Hierarchy**
- BR-01: An `Epic` can never have a `ParentIssueId` (it is always a root).
- BR-02: A `Story`, `Task`, or `Bug` may optionally have a `ParentIssueId` pointing to an `Epic` — no other parent type is valid for them.
- BR-03: A `Subtask` must have a `ParentIssueId`, which must point to a `Story`, `Task`, or `Bug` — never to an `Epic` or another `Subtask`.
- BR-04: A `Subtask` can never itself be a parent (maximum hierarchy depth is two levels: Epic → Story/Task/Bug → Subtask).
- BR-05: Deleting a `Story`/`Task`/`Bug` cascades a hard delete of its `Subtask`s (a Subtask has no meaning without its parent).
- BR-06: Deleting an `Epic` does **not** delete its children — it detaches them by setting their `ParentIssueId` to `NULL`.

**Identity & Status**
- BR-07: `Number` is assigned sequentially per Project at creation and is immutable. `Key` is `{Project.Key}-{Number}` (e.g., `JIRA-123`) and is immutable.
- BR-08: Every Issue always has a `BoardColumnId` — set to the Project's default Board's default column if not specified at creation ([06-boards.md](06-boards.md) BR-07).
- BR-09: `Type` is immutable after creation (converting an Issue from one type to another is not supported in V1 — see §16).
- BR-10: Moving an Issue onto a column belonging to a `Kanban` Board automatically clears its `SprintId` — Kanban issues are not sprint-scoped ([08-sprints.md](08-sprints.md)).
- BR-11: A `Subtask`'s `SprintId` is always kept equal to its parent Issue's `SprintId` and cannot be set independently.

**Assignment**
- BR-12: `AssigneeUserId`, if set, must reference an existing `ProjectMember` of the Issue's Project.
- BR-13: `ReporterUserId` defaults to the creating user and is not editable by Developers — only Project Admins or Workspace Admins may reassign it.
- BR-14: `Priority` defaults to `Medium` if not specified at creation.

**Blocked state**
- BR-15: Blocking requires a `BlockedReason`. A blocker without one cannot be cleared by anyone who was not in the room when it was raised, and reports nothing worth reading — so it is rejected rather than stored empty.
- BR-16: Blocking an Issue that is already blocked rewrites `BlockedReason` but leaves `BlockedSinceUtc` untouched. The timestamp answers "how long has this been stuck", which sharpening the wording must not reset.
- BR-17: An Issue whose current `BoardColumn.IsDoneColumn = true` cannot be blocked. Finished work is not blocked, and the blocker would sit in the Sprint report ([24-reports.md](24-reports.md)) describing work that is already over. The same rule applies in reverse: moving a blocked Issue **into** a Done column clears its blocked state, so the invariant holds against the data and not merely against the block endpoint.
- BR-18: Unblocking clears `IsBlocked`, `BlockedReason`, and `BlockedSinceUtc` together. Unblocking an Issue that is not blocked is rejected rather than treated as a no-op, matching how the Sprint lifecycle treats transitions that have already happened ([08-sprints.md](08-sprints.md) §13).

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Issue

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| ProjectId | Guid (FK → Project) | No | |
| Number | int | No | Sequential per Project, immutable (BR-07) |
| Key | string(20) | No | Computed `{Project.Key}-{Number}`, immutable |
| Type | string(20) | No | `Epic` \| `Story` \| `Task` \| `Bug` \| `Subtask`; immutable (BR-09) |
| ParentIssueId | Guid (FK → Issue) | Yes | Self-referencing; rules in BR-01–BR-04 |
| Title | string(255) | No | |
| Description | string(50000) | Yes | Markdown |
| Priority | string(20) | No | `Low` \| `Medium` \| `High` \| `Critical`; default `Medium` |
| BoardColumnId | Guid (FK → BoardColumn) | No | Effective status (BR-08) |
| SprintId | Guid (FK → Sprint) | Yes | Null = Product Backlog |
| Rank | string(255) | No | See [07-backlog.md](07-backlog.md) |
| AssigneeUserId | Guid (FK → User) | Yes | Must be a ProjectMember (BR-12) |
| ReporterUserId | Guid (FK → User) | No | Defaults to creator (BR-13) |
| DueDateUtc | date | Yes | |
| Estimate | decimal(5,2) | Yes | Story points |
| IsBlocked | bit | No | Default `false` (BR-15–BR-18) |
| BlockedReason | string(500) | Yes | Required while `IsBlocked` (BR-15) |
| BlockedSinceUtc | datetime2 | Yes | Set on the first block, kept across re-blocks (BR-16) |
| CreatedByUserId | Guid (FK → User) | No | |
| CreatedAtUtc | datetime2 | No | |
| UpdatedByUserId | Guid (FK → User) | No | |
| UpdatedAtUtc | datetime2 | No | |
| RowVersion | rowversion | No | Concurrency token |

Unique constraint: (`ProjectId`, `Number`).

## 8. Relationships

- `Project (1) → Issue (N)`
- `Issue (0..1) → Issue (N)` — self-referencing parent/children (BR-01–BR-04)
- `BoardColumn (1) → Issue (N)`
- `Sprint (0..1) → Issue (N)`
- `User (0..1) → Issue (N)` as Assignee
- `User (1) → Issue (N)` as Reporter
- `Issue (1) → Comment (N)` — [10-comments.md](10-comments.md)
- `Issue (1) → Attachment (N)` — [11-attachments.md](11-attachments.md)
- `Issue (N) ↔ Label (N)` via `IssueLabel` — [12-labels.md](12-labels.md)

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/projects/{projectId}/issues` | Project Member or Workspace Admin | List/filter Issues |
| POST | `/api/projects/{projectId}/issues` | Developer, Project Admin, or Workspace Admin | Create Issue |
| GET | `/api/issues/{issueId}` | Project Member or Workspace Admin | Get Issue |
| PATCH | `/api/issues/{issueId}` | Developer, Project Admin, or Workspace Admin | Edit fields (reporter change requires Project Admin/Workspace Admin — BR-13) |
| PATCH | `/api/issues/{issueId}/move` | Developer, Project Admin, or Workspace Admin | Change `BoardColumnId` |
| POST | `/api/issues/{issueId}/block` | Developer, Project Admin, or Workspace Admin | Mark blocked with a reason (BR-15) |
| POST | `/api/issues/{issueId}/unblock` | Developer, Project Admin, or Workspace Admin | Clear the blocked state (BR-18) |
| DELETE | `/api/issues/{issueId}` | Project Admin or Workspace Admin | Delete Issue |
| GET | `/api/issues/{issueId}/subtasks` | Project Member or Workspace Admin | List Subtasks |

Rank changes: `PATCH /api/issues/{issueId}/rank` — see [07-backlog.md](07-backlog.md). Sprint assignment: `POST/DELETE /api/sprints/{sprintId}/issues` — see [08-sprints.md](08-sprints.md).

## 10. Request Examples

**Create Issue**
```http
POST /api/projects/{projectId}/issues
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "type": "Story",
  "title": "Support workspace invitations via email",
  "description": "## Acceptance Criteria\n- Admin can invite by email\n- Invitee receives email with link",
  "priority": "High",
  "assigneeUserId": "3c1a1e2e-...",
  "dueDateUtc": "2026-08-10",
  "estimate": 5
}
```

**Create Subtask**
```http
POST /api/projects/{projectId}/issues
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "type": "Subtask",
  "parentIssueId": "e5f6g7h8-...",
  "title": "Write invitation email template"
}
```

**Move Issue**
```http
PATCH /api/issues/{issueId}/move
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "boardColumnId": "d1a2b3c4-...",
  "rowVersion": "AAAAAAAAB9E="
}
```

## 11. Response Examples

**Create Issue — 201 Created**
```json
{
  "id": "e5f6g7h8-...",
  "key": "JIRA-124",
  "number": 124,
  "type": "Story",
  "title": "Support workspace invitations via email",
  "priority": "High",
  "boardColumnId": "c1a1e2e6-...",
  "sprintId": null,
  "assignee": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
  "reporter": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
  "dueDateUtc": "2026-08-10",
  "estimate": 5,
  "createdAtUtc": "2026-07-31T10:00:00Z"
}
```

**GET /api/issues/{issueId} — 200 OK**
```json
{
  "id": "e5f6g7h8-...",
  "key": "JIRA-124",
  "type": "Story",
  "parentIssueId": null,
  "title": "Support workspace invitations via email",
  "description": "## Acceptance Criteria\n- Admin can invite by email\n- Invitee receives email with link",
  "priority": "High",
  "boardColumnId": "c1a1e2e6-...",
  "sprintId": null,
  "rank": "0|100003:",
  "assignee": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
  "reporter": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
  "dueDateUtc": "2026-08-10",
  "estimate": 5,
  "labels": [],
  "subtaskCount": 1,
  "rowVersion": "AAAAAAAAB9E="
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| Type | Required, one of `Epic`, `Story`, `Task`, `Bug`, `Subtask`; immutable |
| ParentIssueId | Required for `Subtask`; forbidden for `Epic`; optional for `Story`/`Task`/`Bug` — see BR-01–BR-04 |
| Title | Required, 1–255 chars |
| Description | Optional, max 50,000 chars |
| Priority | One of `Low`, `Medium`, `High`, `Critical`; defaults to `Medium` |
| AssigneeUserId | Optional, must be a ProjectMember of the Issue's Project |
| DueDateUtc | Optional, valid date |
| Estimate | Optional, decimal ≥ 0, ≤ 999.99 |
| boardColumnId (move) | Required, must belong to a Board in the same Project |
| rowVersion (move) | Required, must match current value |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Invalid parent/child type combination | 400 Bad Request | BR-01–BR-04 |
| Assignee is not a Project member | 400 Bad Request | BR-12 |
| Non-admin attempts to change Reporter | 403 Forbidden | BR-13 |
| Attempt to change `Type` after creation | 400 Bad Request | BR-09 |
| Move to a `BoardColumnId` outside the Issue's Project | 400 Bad Request | |
| `rowVersion` mismatch on move | 409 Conflict | |
| Issue not found | 404 Not Found | |
| Viewer attempts a write action | 403 Forbidden | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Issues, list Subtasks | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` |
| Create, edit, move Issue | `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) **or** `WorkspaceMember.Role = Admin` |
| Change Reporter | `ProjectMember.Role = ProjectAdmin` **or** `WorkspaceMember.Role = Admin` |
| Delete Issue | `ProjectMember.Role = ProjectAdmin` **or** `WorkspaceMember.Role = Admin` |

## 15. Acceptance Criteria

- Given a Project, when a Story is created without specifying a column, then it is placed in the Project's default Board's default column and appended to the bottom of the Product Backlog.
- Given a Story, when a Subtask is created with it as parent, then the Subtask's `SprintId` always mirrors the Story's `SprintId`.
- Given an Epic with child Stories, when the Epic is deleted, then the Stories remain, with `ParentIssueId` cleared.
- Given a Story with Subtasks, when the Story is deleted, then its Subtasks are deleted too.
- Given an Issue moved onto a Kanban Board's column, then its `SprintId` is cleared automatically.
- Given a stale `rowVersion`, when a move is attempted, then it is rejected with 409.

## 16. Future Improvements

- Issue type conversion (e.g., Bug → Task) with hierarchy revalidation.
- Estimate roll-up from Subtasks to parent.
- Custom fields per Project.
- Issue linking beyond parent/child (blocks, relates-to).
- Bulk edit / bulk move.
