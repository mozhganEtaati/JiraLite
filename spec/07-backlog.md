# 07 — Backlog

## 1. Overview

Covers the Product Backlog, Sprint Backlog, and drag-and-drop ranking. **There is no separate `BacklogItem` entity** — the backlog is a query view over `Issue`, differentiated by `SprintId` (null = Product Backlog, set = that Sprint's Backlog) and ordered by `Rank`. Full `Issue` schema is defined in [09-issues.md](09-issues.md); this document defines the ranking/ordering semantics that field relies on.

## 2. Business Goal

Let a team maintain a single prioritized list of upcoming work (Product Backlog) and pull items into time-boxed Sprints, reordering either list by drag-and-drop without expensive full-list renumbering.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Project member, I can view the Product Backlog ordered by priority/rank. |
| US-02 | As a Project member, I can view a Sprint's Backlog ordered by rank. |
| US-03 | As a Developer or Project Admin, I can drag an Issue to a new position within a list and have it stay there. |

## 4. Functional Requirements

- FR-01: The Product Backlog for a Project is the set of its Issues where `SprintId IS NULL`, excluding `Subtask`-type Issues, ordered by `Rank` ascending.
- FR-02: A Sprint's Backlog is the set of Issues where `SprintId` matches that Sprint, excluding `Subtask`-type Issues, ordered by `Rank` ascending.
- FR-03: A Developer or Project Admin can reposition an Issue within its current list by specifying which Issue it should immediately follow.
- FR-04: Moving an Issue between the Product Backlog and a Sprint (changing `SprintId`) is documented in [08-sprints.md](08-sprints.md); this document covers same-list reordering only.

## 5. Non-Functional Requirements

- NFR-01: Reordering a single Issue must not require rewriting the `Rank` of every other Issue in the list — only the moved Issue's `Rank` is recalculated (see BR-02).
- NFR-02: `Rank` comparisons and list queries are indexed on (`ProjectId`, `SprintId`, `Rank`) for efficient ordered retrieval.

## 6. Business Rules

- BR-01: `Rank` is a lexicographically sortable string column on `Issue` (LexoRank-style). Ranking is scoped independently per list — i.e., relative order is only meaningful among Issues sharing the same `SprintId` value (including `NULL`), never globally across a Project.
- BR-02: Repositioning an Issue computes a new `Rank` value as a midpoint string between its new previous and next neighbors in the target list — only the moved Issue's row is updated.
- BR-03: If repeated insertions between the same two neighbors exhaust available string precision, a background rebalancing job (Hangfire, see [20-coding-guidelines.md](20-coding-guidelines.md)) evenly renumbers the affected list. This never changes relative order, only the underlying values.
- BR-04: `Subtask`-type Issues are never independently ranked or listed in a backlog — they are only visible through their parent Issue (see [09-issues.md](09-issues.md)).
- BR-05: `Epic`, `Story`, `Task`, and `Bug` types all participate uniformly in backlog ranking and Sprint assignment — JiraLite does not restrict Epics from being placed in a Sprint.
- BR-06: When an Issue is created, it is appended to the bottom of its target list (Product Backlog by default, or the specified Sprint's Backlog) with a `Rank` greater than the current last item.
- BR-07: Repositioning uses the Issue's `RowVersion` concurrency token (see [09-issues.md](09-issues.md)) to detect conflicting simultaneous drags.

## 7. Database Entities

No new entities. This document governs the following existing `Issue` columns (full schema: [18-database.md](18-database.md) / [09-issues.md](09-issues.md)):

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Rank | string(255) | No | Lexicographically sortable position within its list (BR-01) |
| SprintId | Guid (FK → Sprint) | Yes | Null = Product Backlog |

## 8. Relationships

- `Project (1) → Issue (N)` — Product Backlog scope
- `Sprint (1) → Issue (N)` — Sprint Backlog scope

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/projects/{projectId}/backlog` | Project Member or Workspace Admin | List Product Backlog, ranked |
| GET | `/api/sprints/{sprintId}/backlog` | Project Member or Workspace Admin | List Sprint Backlog, ranked |
| PATCH | `/api/issues/{issueId}/rank` | Developer, Project Admin, or Workspace Admin | Reposition Issue within its current list |

## 10. Request Examples

**Get Product Backlog**
```http
GET /api/projects/{projectId}/backlog?limit=50
Authorization: Bearer {accessToken}
```

**Reposition Issue**
```http
PATCH /api/issues/{issueId}/rank
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "afterIssueId": "e5f6g7h8-...",
  "rowVersion": "AAAAAAAAB9E="
}
```
`afterIssueId: null` moves the Issue to the top of the list.

## 11. Response Examples

**GET /api/projects/{projectId}/backlog — 200 OK**
```json
{
  "items": [
    {
      "id": "e5f6g7h8-...",
      "key": "JIRA-123",
      "title": "Fix login redirect",
      "type": "Bug",
      "priority": "High",
      "rank": "0|100000:",
      "assignee": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" }
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

**PATCH /api/issues/{issueId}/rank — 200 OK**
```json
{
  "id": "e5f6g7h8-...",
  "rank": "0|100002:",
  "rowVersion": "AAAAAAAAB9I="
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| afterIssueId | Optional; if present, must reference an Issue in the same list scope (same `ProjectId` and `SprintId`) as the Issue being moved |
| rowVersion | Required, must match the Issue's current concurrency token |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| `afterIssueId` references an Issue in a different list (different `SprintId`) | 400 Bad Request | |
| Reposition a `Subtask`-type Issue | 400 Bad Request | BR-04 |
| `rowVersion` mismatch (concurrent edit) | 409 Conflict | BR-07 |
| Issue not found | 404 Not Found | |
| Non-member or Viewer attempts reorder | 403 Forbidden | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Product/Sprint Backlog | `ProjectMember` (any role, including `Viewer`) **or** `WorkspaceMember.Role = Admin` |
| Reposition an Issue | `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) **or** `WorkspaceMember.Role = Admin` — `Viewer` is read-only |

## 15. Acceptance Criteria

- Given a Project with Issues having `SprintId = NULL`, when the Product Backlog is requested, then they are returned ordered by `Rank` ascending, excluding Subtasks.
- Given an Issue moved to a new position via `afterIssueId`, then only that Issue's `Rank` changes, and it now sorts correctly relative to its new neighbors.
- Given two simultaneous reposition requests for the same list with a stale `rowVersion`, then the second request is rejected with 409.
- Given a list where available `Rank` precision between two neighbors is exhausted, then a background rebalance job renumbers the list without changing relative order.

## 16. Future Improvements

- Bulk reorder (move multiple Issues in one request).
- Server-computed rank rebalancing exposed as an on-demand admin action.
- Backlog filtering by label, assignee, or type.
