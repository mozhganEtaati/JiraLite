# 08 — Sprints

## 1. Overview

Covers Sprint creation, lifecycle (Planned → Active → Completed), and the assignment of Issues into a Sprint's Backlog. A Sprint belongs to exactly one `Scrum`-type Board ([06-boards.md](06-boards.md) BR-08). Kanban boards have no Sprints. Ranking of Issues within a Sprint's Backlog is covered by [07-backlog.md](07-backlog.md).

## 2. Business Goal

Let a Scrum team plan a time-boxed iteration, pull Issues into it, run it, and close it out — carrying unfinished work forward predictably.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Developer or Project Admin, I can create a Sprint with a name, goal, and planned dates. |
| US-02 | As a Developer or Project Admin, I can start a Sprint, making it the Board's active iteration. |
| US-03 | As a Developer or Project Admin, I can add or remove Issues from a Sprint's Backlog. |
| US-04 | As a Developer or Project Admin, I can complete a Sprint, with unfinished Issues automatically carried to the Product Backlog or a follow-up Sprint. |

## 4. Functional Requirements

- FR-01: A Developer or Project Admin can create a Sprint on a `Scrum`-type Board with a name, optional goal, and planned start/end dates.
- FR-02: A Developer or Project Admin can start a Planned Sprint, transitioning it to Active.
- FR-03: A Developer or Project Admin can add an Issue to a Sprint's Backlog (setting its `SprintId`) or remove it (returning it to the Product Backlog).
- FR-04: A Developer or Project Admin can complete an Active Sprint, transitioning it to Completed.
- FR-05: On completion, Issues not in a "Done" column (per [06-boards.md](06-boards.md) `IsDoneColumn`) are moved either to the Product Backlog or to another Planned Sprint on the same Board, per the caller's choice.
- FR-06: A Project Admin or Workspace Admin can delete a Sprint, but only while it is still Planned.

## 5. Non-Functional Requirements

- NFR-01: Sprint state transitions (start/complete) are atomic — either the transition and any Issue carry-forward both succeed, or neither does.

## 6. Business Rules

- BR-01: A Board may have at most one `Active` Sprint at any time. Starting a Sprint while another Sprint on the same Board is Active is rejected.
- BR-02: Sprint lifecycle is strictly linear: `Planned → Active → Completed`. A Sprint cannot be completed without first being started, and cannot be reopened once Completed.
- BR-03: `PlannedStartDateUtc`/`PlannedEndDateUtc` are editable only while `Status = Planned`. Once Active, the actual `StartedAtUtc` timestamp is the source of truth for when the Sprint began.
- BR-04: `PlannedEndDateUtc` must be strictly after `PlannedStartDateUtc`.
- BR-05: On completion, every Issue in the Sprint whose current column has `IsDoneColumn = false` is carried forward: either to the Product Backlog (`SprintId = NULL`) or to a specified other `Planned` Sprint on the same Board, per the `moveIncompleteIssuesToSprintId` parameter. Issues already in a Done column keep their `SprintId` set to the completed Sprint permanently, forming that Sprint's historical record (used by [15-calendar.md](15-calendar.md) sprint timeline).
- BR-06: A Sprint can only be deleted while `Status = Planned`. Deleting it moves any Issues currently assigned to it back to the Product Backlog (`SprintId = NULL`).
- BR-07: Adding an Issue to a Sprint that is already assigned to a different Sprint reassigns it directly (no explicit "remove first" step required).
- BR-08: Only `Scrum`-type Boards may have Sprints created on them.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Sprint

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| BoardId | Guid (FK → Board) | No | Must reference a `Scrum`-type Board |
| ProjectId | Guid (FK → Project) | No | Denormalized from Board for query convenience |
| Name | string(100) | No | |
| Goal | string(500) | Yes | |
| Status | string(20) | No | `Planned` \| `Active` \| `Completed` |
| PlannedStartDateUtc | date | No | |
| PlannedEndDateUtc | date | No | Must be after `PlannedStartDateUtc` |
| StartedAtUtc | datetime2 | Yes | Set when transitioned to Active |
| CompletedAtUtc | datetime2 | Yes | Set when transitioned to Completed |
| CreatedByUserId | Guid (FK → User) | No | |
| CreatedAtUtc | datetime2 | No | |

Related `Issue` columns (defined in [09-issues.md](09-issues.md)): `SprintId` (nullable FK → Sprint).

## 8. Relationships

- `Board (1) → Sprint (N)` — Scrum boards only
- `Sprint (1) → Issue (N)` — via `Issue.SprintId`
- `Sprint (0..1) → Sprint (N)` — conceptual "carried to" relationship on completion (not a stored FK; expressed via the resulting `Issue.SprintId` values)

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/boards/{boardId}/sprints` | Project Member or Workspace Admin | List Sprints on a Board |
| POST | `/api/boards/{boardId}/sprints` | Developer, Project Admin, or Workspace Admin | Create Sprint |
| GET | `/api/sprints/{sprintId}` | Project Member or Workspace Admin | Get Sprint |
| PATCH | `/api/sprints/{sprintId}` | Developer, Project Admin, or Workspace Admin | Edit name/goal/dates |
| POST | `/api/sprints/{sprintId}/start` | Developer, Project Admin, or Workspace Admin | Start Sprint |
| POST | `/api/sprints/{sprintId}/complete` | Developer, Project Admin, or Workspace Admin | Complete Sprint |
| DELETE | `/api/sprints/{sprintId}` | Project Admin or Workspace Admin | Delete Sprint (Planned only) |
| POST | `/api/sprints/{sprintId}/issues` | Developer, Project Admin, or Workspace Admin | Add Issue to Sprint |
| DELETE | `/api/sprints/{sprintId}/issues/{issueId}` | Developer, Project Admin, or Workspace Admin | Remove Issue from Sprint (→ Product Backlog) |

Sprint Backlog listing: `GET /api/sprints/{sprintId}/backlog` — see [07-backlog.md](07-backlog.md).

## 10. Request Examples

**Create Sprint**
```http
POST /api/boards/{boardId}/sprints
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Sprint 12",
  "goal": "Ship JWT refresh flow and workspace invitations",
  "plannedStartDateUtc": "2026-08-03",
  "plannedEndDateUtc": "2026-08-14"
}
```

**Complete Sprint**
```http
POST /api/sprints/{sprintId}/complete
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "moveIncompleteIssuesToSprintId": null
}
```

**Add Issue to Sprint**
```http
POST /api/sprints/{sprintId}/issues
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "issueId": "e5f6g7h8-..."
}
```

## 11. Response Examples

**Create Sprint — 201 Created**
```json
{
  "id": "1f2e3d4c-...",
  "boardId": "8a9b0c1d-...",
  "name": "Sprint 12",
  "goal": "Ship JWT refresh flow and workspace invitations",
  "status": "Planned",
  "plannedStartDateUtc": "2026-08-03",
  "plannedEndDateUtc": "2026-08-14"
}
```

**Complete Sprint — 200 OK**
```json
{
  "id": "1f2e3d4c-...",
  "status": "Completed",
  "completedAtUtc": "2026-08-14T17:00:00Z",
  "carriedForwardIssueCount": 2
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| Name | Required, 1–100 chars |
| Goal | Optional, max 500 chars |
| PlannedStartDateUtc / PlannedEndDateUtc | Required, valid dates, end after start |
| moveIncompleteIssuesToSprintId | Optional; if present, must be a `Planned` Sprint on the same Board |
| issueId | Required, must exist in the same Project |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Start a Sprint while another Sprint on the Board is Active | 409 Conflict | BR-01 |
| Start a Sprint that is not Planned | 409 Conflict | BR-02 |
| Complete a Sprint that is not Active | 409 Conflict | BR-02 |
| Delete a Sprint that is not Planned | 409 Conflict | BR-06 |
| Create a Sprint on a Kanban Board | 400 Bad Request | BR-08 |
| `moveIncompleteIssuesToSprintId` references a non-Planned Sprint or a Sprint on a different Board | 400 Bad Request | |
| `PlannedEndDateUtc` not after `PlannedStartDateUtc` | 400 Bad Request | BR-04 |
| Add an Issue that doesn't belong to the same Project | 400 Bad Request | |
| Sprint or Issue not found | 404 Not Found | |
| Viewer attempts a write action | 403 Forbidden | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Sprints | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` |
| Create, edit, start, complete Sprint; add/remove Issues | `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) **or** `WorkspaceMember.Role = Admin` |
| Delete Sprint | `ProjectMember.Role = ProjectAdmin` **or** `WorkspaceMember.Role = Admin` |

## 15. Acceptance Criteria

- Given a Scrum Board with no Active Sprint, when a Planned Sprint is started, then its `Status` becomes `Active` and `StartedAtUtc` is set.
- Given a Board with an Active Sprint, when starting a second Sprint on the same Board is attempted, then it is rejected.
- Given an Active Sprint with some Issues in Done columns and some not, when completed without `moveIncompleteIssuesToSprintId`, then Done Issues retain `SprintId`, and non-Done Issues have `SprintId` set to `NULL`.
- Given the same scenario but with `moveIncompleteIssuesToSprintId` set to a valid Planned Sprint, then non-Done Issues have `SprintId` set to that Sprint instead.
- Given a Planned Sprint with Issues assigned, when it is deleted, then those Issues return to the Product Backlog.

## 16. Future Improvements

- Sprint velocity/burndown reporting.
- Sprint templates (recurring goal structure, default duration).
- Automatic Sprint start/end on scheduled dates (currently manual actions only).
