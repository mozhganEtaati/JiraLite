# 12 — Labels

## 1. Overview

Covers Label CRUD and attaching/detaching Labels on Issues. A Label is scoped to a single Project. `Issue ↔ Label` is the platform's only true many-to-many relationship, modeled via the `IssueLabel` join entity ([00-project-overview.md](00-project-overview.md) §5, §7).

## 2. Business Goal

Let a Project define a reusable set of tags and apply them to Issues for lightweight categorization and filtering.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Project Admin, I can create a Label with a name and color. |
| US-02 | As a Project Admin, I can rename, recolor, or delete a Label. |
| US-03 | As a Developer, I can attach or remove Labels on an Issue. |
| US-04 | As a Project member, I can filter Issues by Label ([09-issues.md](09-issues.md)). |

## 4. Functional Requirements

- FR-01: A Project Admin can create, edit, and delete Labels scoped to their Project.
- FR-02: A Developer or Project Admin can attach or detach an existing Label on an Issue within the same Project.
- FR-03: Any Project member can list a Project's Labels and see the Labels attached to an Issue.

## 5. Non-Functional Requirements

- NFR-01: Label name lookups (for uniqueness checks and filtering) are indexed on (`ProjectId`, `Name`).

## 6. Business Rules

- BR-01: `Label.Name` is unique within a Project (case-insensitive).
- BR-02: Deleting a Label removes all its `IssueLabel` associations (cascade) but never deletes the Issues it was attached to.
- BR-03: A Label can only be attached to Issues within the same Project it belongs to.
- BR-04: Label definition management (create/edit/delete) requires Project Admin or Workspace Admin — a higher bar than attaching/detaching an existing Label to an Issue, which only requires Developer, mirroring the Board/Column-definition vs. Issue-move split in [06-boards.md](06-boards.md)/[09-issues.md](09-issues.md).
- BR-05: Labels cannot be created, edited, or deleted on an archived Project ([05-projects.md](05-projects.md) BR-04).

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Label

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| ProjectId | Guid (FK → Project) | No | |
| Name | string(50) | No | Unique per Project (case-insensitive) |
| Color | string(7) | No | Hex format `#RRGGBB` |
| CreatedAtUtc | datetime2 | No | |

### IssueLabel

| Column | Type | Nullable | Notes |
|---|---|---|---|
| IssueId | Guid (FK → Issue) | No | Composite PK |
| LabelId | Guid (FK → Label) | No | Composite PK |

## 8. Relationships

- `Project (1) → Label (N)`
- `Issue (N) ↔ Label (N)` via `IssueLabel`

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/projects/{projectId}/labels` | Project Member or Workspace Admin | List Labels |
| POST | `/api/projects/{projectId}/labels` | Project Admin or Workspace Admin | Create Label |
| PATCH | `/api/labels/{labelId}` | Project Admin or Workspace Admin | Edit Label |
| DELETE | `/api/labels/{labelId}` | Project Admin or Workspace Admin | Delete Label |
| POST | `/api/issues/{issueId}/labels` | Developer, Project Admin, or Workspace Admin | Attach Label to Issue |
| DELETE | `/api/issues/{issueId}/labels/{labelId}` | Developer, Project Admin, or Workspace Admin | Detach Label from Issue |

## 10. Request Examples

**Create Label**
```http
POST /api/projects/{projectId}/labels
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "regression",
  "color": "#E11D48"
}
```

**Attach Label to Issue**
```http
POST /api/issues/{issueId}/labels
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "labelId": "b2c3d4e5-..."
}
```

## 11. Response Examples

**Create Label — 201 Created**
```json
{
  "id": "b2c3d4e5-...",
  "projectId": "5e6f7a8b-...",
  "name": "regression",
  "color": "#E11D48",
  "createdAtUtc": "2026-07-31T11:30:00Z"
}
```

**GET /api/projects/{projectId}/labels — 200 OK**
```json
{
  "items": [
    { "id": "b2c3d4e5-...", "name": "regression", "color": "#E11D48" },
    { "id": "c3d4e5f6-...", "name": "needs-design", "color": "#7C3AED" }
  ]
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| Name | Required, 1–50 chars, unique per Project (case-insensitive) |
| Color | Required, matches `^#[0-9A-Fa-f]{6}$` |
| labelId (attach) | Required, must belong to the same Project as the Issue |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Duplicate Label name in Project | 409 Conflict | BR-01 |
| Invalid color format | 400 Bad Request | |
| Attach a Label from a different Project | 400 Bad Request | BR-03 |
| Attach a Label already on the Issue | 409 Conflict | |
| Detach a Label not on the Issue | 404 Not Found | |
| Manage Labels on an archived Project | 409 Conflict | BR-05 |
| Viewer attempts create/edit/delete/attach/detach | 403 Forbidden | |
| Label or Issue not found | 404 Not Found | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Labels | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` |
| Create/edit/delete Label | `ProjectMember.Role = ProjectAdmin` **or** `WorkspaceMember.Role = Admin` |
| Attach/detach Label on Issue | `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) **or** `WorkspaceMember.Role = Admin` |

## 15. Acceptance Criteria

- Given a Project Admin, when they create a Label with a unique name and valid hex color, then it becomes available for that Project.
- Given a duplicate Label name within the same Project, when creation is attempted, then it is rejected with 409.
- Given a Label attached to several Issues, when it is deleted, then all `IssueLabel` associations are removed and the Issues themselves are unaffected.
- Given a Developer, when they attach an existing Project Label to an Issue, then it appears in that Issue's label list without requiring Project Admin involvement.
- Given a Label from Project A, when attaching it to an Issue in Project B is attempted, then it is rejected with 400.

## 16. Future Improvements

- Label usage counts and "unused label" cleanup tooling.
- Workspace-level shared Label palette.
- Label-based automation (e.g., auto-notify on label attach).
