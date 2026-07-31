# 15 — Calendar

## 1. Overview

Covers two read-only, Project-scoped views: Due Dates and Sprint Timeline. Unlike [14-dashboard.md](14-dashboard.md), which is personalized to the caller ("my" tasks/projects), Calendar views show **all** relevant data for a Project that the caller can view, regardless of assignment. This is a query/projection context — no new entities are introduced.

## 2. Business Goal

Let a Project member see upcoming due dates and Sprint scheduling at a glance, across the whole Project rather than just their own assignments.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Project member, I can view Issues with due dates within a date range. |
| US-02 | As a Project member, I can view the Project's Sprints on a timeline, showing planned and actual dates. |

## 4. Functional Requirements

- FR-01: Due Dates returns Issues in the Project with a non-null `DueDateUtc` falling within the requested date range (default: current calendar month).
- FR-02: Sprint Timeline returns all Sprints across the Project's Scrum Boards, with `Status`, `PlannedStartDateUtc`, `PlannedEndDateUtc`, `StartedAtUtc`, and `CompletedAtUtc`.

## 5. Non-Functional Requirements

- NFR-01: Due Dates query is indexed on (`ProjectId`, `DueDateUtc`).

## 6. Business Rules

- BR-01: Due Dates includes Issues of any type (including Subtasks) that have a `DueDateUtc` set — unlike backlog ranking ([07-backlog.md](07-backlog.md) BR-04), Subtasks are not excluded here since they can carry their own due dates.
- BR-02: If no `from`/`to` range is supplied, Due Dates defaults to the current calendar month (UTC).
- BR-03: Sprint Timeline aggregates Sprints across every Scrum Board in the Project, not just one Board.
- BR-04: Both views remain fully viewable for archived Projects, consistent with archiving being read-only rather than hidden ([05-projects.md](05-projects.md) BR-04).
- BR-05: Both views are strictly read-only.

## 7. Database Entities

No new entities. This document composes reads over:

- `Issue.DueDateUtc` — see [09-issues.md](09-issues.md)
- `Sprint` — see [08-sprints.md](08-sprints.md)

## 8. Relationships

No new relationships. Existing relationships are queried, not modified.

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/projects/{projectId}/calendar/due-dates` | Project Member or Workspace Admin | Issues with due dates in range |
| GET | `/api/projects/{projectId}/calendar/sprint-timeline` | Project Member or Workspace Admin | Sprints across the Project's Scrum Boards |

## 10. Request Examples

**Due Dates**
```http
GET /api/projects/{projectId}/calendar/due-dates?from=2026-08-01&to=2026-08-31
Authorization: Bearer {accessToken}
```

**Sprint Timeline**
```http
GET /api/projects/{projectId}/calendar/sprint-timeline?from=2026-07-01&to=2026-09-30
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**Due Dates — 200 OK**
```json
{
  "items": [
    {
      "id": "e5f6g7h8-...",
      "key": "JIRA-124",
      "title": "Support workspace invitations via email",
      "type": "Story",
      "dueDateUtc": "2026-08-10",
      "assignee": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" }
    }
  ]
}
```

**Sprint Timeline — 200 OK**
```json
{
  "items": [
    {
      "id": "1f2e3d4c-...",
      "boardId": "8a9b0c1d-...",
      "name": "Sprint 12",
      "status": "Active",
      "plannedStartDateUtc": "2026-08-03",
      "plannedEndDateUtc": "2026-08-14",
      "startedAtUtc": "2026-08-03T09:00:00Z",
      "completedAtUtc": null
    }
  ]
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| from / to | Optional dates; if both present, `to` must not be before `from` |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| `to` earlier than `from` | 400 Bad Request | |
| Project not found | 404 Not Found | |
| Non-member, non-Workspace-Admin access | 403 Forbidden | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Due Dates, Sprint Timeline | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` |

## 15. Acceptance Criteria

- Given Issues with due dates inside and outside August 2026, when Due Dates is requested for that month, then only the in-range Issues are returned.
- Given no date range supplied, when Due Dates is requested, then the current calendar month is used.
- Given a Project with Sprints across two different Scrum Boards, when Sprint Timeline is requested, then Sprints from both Boards appear together, ordered chronologically.
- Given an archived Project, when either Calendar view is requested, then data is still returned.

## 16. Future Improvements

- iCalendar (.ics) export/subscription feed.
- Workspace-wide calendar aggregating multiple Projects.
- Calendar view of Team-level due dates.
