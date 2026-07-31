# 05 — Projects

## 1. Overview

Covers Projects: the container for Boards, Sprints, and Issues within a Workspace, and `ProjectMember` — the project-scoped role assignment. A Project's short `Key` is used as the prefix for human-readable Issue identifiers (e.g., `JIRA-123`) defined fully in [09-issues.md](09-issues.md).

## 2. Business Goal

Let a Workspace organize work into distinct Projects, each with its own membership, boards, and backlog, while keeping Workspace Admins able to oversee every Project without needing explicit per-project membership.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Workspace Admin, I can create a Project with a name and a short key. |
| US-02 | As a Project Admin, I can edit the Project's name and description. |
| US-03 | As a Project Admin, I can add or remove Project members and assign their role. |
| US-04 | As a Project Admin, I can archive a Project when work on it is paused or finished. |
| US-05 | As a Workspace Admin, I can permanently delete an archived Project. |

## 4. Functional Requirements

- FR-01: A Workspace Admin can create a Project with a `Name` and a unique `Key`.
- FR-02: The Project creator is automatically added as a `ProjectMember` with role `ProjectAdmin`.
- FR-03: A Project Admin (or Workspace Admin) can edit the Project's `Name` and `Description`. `Key` is immutable after creation.
- FR-04: A Project Admin (or Workspace Admin) can add/remove `ProjectMember`s and change their role.
- FR-05: A Project Admin (or Workspace Admin) can archive and unarchive a Project.
- FR-06: A Workspace Admin can permanently delete a Project, but only if it is already archived.

## 5. Non-Functional Requirements

- NFR-01: Project deletion is a cascading, irreversible operation and must be gated behind the archive-first rule (BR-05) to reduce accidental data loss.
- NFR-02: `Key` uniqueness is enforced at the Workspace level (case-insensitive) to keep Issue identifiers unambiguous within a Workspace.

## 6. Business Rules

- BR-01: Only `WorkspaceMember`s of the owning Workspace can be added as `ProjectMember`s (same pattern as [04-teams.md](04-teams.md) BR-01).
- BR-02: A Workspace Admin has full authority over every Project in their Workspace regardless of whether they hold an explicit `ProjectMember` record. There is no "last Project Admin" invariant on `ProjectMember` — Workspace Admins are always a fallback authority. Full role semantics: [16-rbac.md](16-rbac.md).
- BR-03: `Project.Key` is set at creation, is immutable, uppercase, and unique within the Workspace (case-insensitive).
- BR-04: An archived Project (`IsArchived = true`) is read-only: no new Boards, Sprints, Issues, Comments, or Attachments may be created within it. Existing data remains fully readable. Unarchiving restores write access.
- BR-05: A Project must be archived before it can be deleted. This two-step rail prevents an accidental single-action irreversible deletion.
- BR-06: Deleting a Project cascades a hard delete of everything it owns: Boards, `BoardColumn`s, Sprints, Issues, Comments, Attachments (including their stored files), Labels, and `ProjectMember` records. This is irreversible. As part of the same transaction, any `ActivityLogEntry` rows referencing the deleted Project have their `ProjectId` set to `NULL` (their `WorkspaceId` is retained) rather than being deleted themselves — `ActivityLogEntry` is an append-only audit log ([02-users.md](02-users.md)) and is never purged by a Project deletion, only detached from the Project it can no longer reference. This mirrors how Sprint deletion nulls `Issue.SprintId` ([08-sprints.md](08-sprints.md) BR-06) rather than deleting the Issue.
- BR-07: Deleting a Project requires `WorkspaceMember.Role = Admin` — a higher bar than Archive, given BR-06's irreversibility.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Project

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| WorkspaceId | Guid (FK → Workspace) | No | |
| Key | string(10) | No | Immutable, unique per Workspace, uppercase |
| Name | string(200) | No | |
| Description | string(1000) | Yes | |
| IsArchived | bool | No | Default `false` |
| CreatedByUserId | Guid (FK → User) | No | |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### ProjectMember

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| ProjectId | Guid (FK → Project) | No | |
| UserId | Guid (FK → User) | No | |
| Role | string(20) | No | `ProjectAdmin` \| `Developer` \| `Viewer` |
| CreatedAtUtc | datetime2 | No | |

Unique constraint: (`ProjectId`, `UserId`).

## 8. Relationships

- `Workspace (1) → Project (N)`
- `Project (1) → ProjectMember (N)`
- `User (1) → ProjectMember (N)`
- `Project (1) → Board (N)`, `Project (1) → Sprint (N)`, `Project (1) → Issue (N)`, `Project (1) → Label (N)` — detailed in their respective documents ([06](06-boards.md), [08](08-sprints.md), [09](09-issues.md), [12](12-labels.md))

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/workspaces/{workspaceId}/projects` | Workspace Member | List Projects |
| POST | `/api/workspaces/{workspaceId}/projects` | Workspace Admin | Create Project |
| GET | `/api/projects/{projectId}` | Project Member or Workspace Admin | Get Project |
| PATCH | `/api/projects/{projectId}` | Project Admin or Workspace Admin | Edit Name/Description |
| POST | `/api/projects/{projectId}/archive` | Project Admin or Workspace Admin | Archive |
| POST | `/api/projects/{projectId}/unarchive` | Project Admin or Workspace Admin | Unarchive |
| DELETE | `/api/projects/{projectId}` | Workspace Admin | Permanently delete (must be archived) |
| GET | `/api/projects/{projectId}/members` | Project Member or Workspace Admin | List members |
| POST | `/api/projects/{projectId}/members` | Project Admin or Workspace Admin | Add member |
| PATCH | `/api/projects/{projectId}/members/{userId}` | Project Admin or Workspace Admin | Change role |
| DELETE | `/api/projects/{projectId}/members/{userId}` | Project Admin or Workspace Admin | Remove member |

## 10. Request Examples

**Create Project**
```http
POST /api/workspaces/{workspaceId}/projects
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "key": "JIRA",
  "name": "JiraLite Platform",
  "description": "Core platform engineering work"
}
```

**Add member**
```http
POST /api/projects/{projectId}/members
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "userId": "3c1a1e2e-6b1a-4e9a-9c3e-1a2b3c4d5e6f",
  "role": "Developer"
}
```

**Delete Project**
```http
DELETE /api/projects/{projectId}
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**Create Project — 201 Created**
```json
{
  "id": "5e6f7a8b-...",
  "workspaceId": "7d8e9f0a-...",
  "key": "JIRA",
  "name": "JiraLite Platform",
  "description": "Core platform engineering work",
  "isArchived": false,
  "createdAtUtc": "2026-07-31T10:00:00Z"
}
```

**Delete Project — 204 No Content**
(empty body)

## 12. Validation Rules

| Field | Rule |
|---|---|
| Key | Required, 2–10 chars, uppercase letters and digits only, must start with a letter, unique per Workspace |
| Name | Required, 1–200 chars |
| Description | Optional, max 1000 chars |
| Role | Required, one of `ProjectAdmin`, `Developer`, `Viewer` |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Duplicate Key within Workspace | 409 Conflict | |
| Attempt to change Key after creation | 400 Bad Request | Field is immutable |
| Delete a Project that is not archived | 409 Conflict | BR-05 |
| Add a member who is not a Workspace member | 400 Bad Request | BR-01 |
| Write operation on an archived Project | 409 Conflict | BR-04 |
| Project not found | 404 Not Found | |
| Non-admin attempts management action | 403 Forbidden | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| Create Project | `WorkspaceMember.Role = Admin` |
| View Project/members | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` (BR-02) |
| Edit, archive/unarchive, manage members | `ProjectMember.Role = ProjectAdmin` **or** `WorkspaceMember.Role = Admin` |
| Delete Project | `WorkspaceMember.Role = Admin` only (BR-07) |

## 15. Acceptance Criteria

- Given a Workspace Admin, when they create a Project with a unique key, then the Project is created and the creator becomes `ProjectAdmin`.
- Given a duplicate key within the same Workspace, when creating a Project, then the request is rejected with 409.
- Given an archived Project, when any write operation (create Issue, add Comment, etc.) is attempted, then it is rejected with 409.
- Given a non-archived Project, when deletion is attempted, then it is rejected until the Project is archived first.
- Given an archived Project, when a Workspace Admin deletes it, then all Boards, Sprints, Issues, Comments, Attachments, Labels, and ProjectMembers are permanently removed.
- Given a Project with existing `ActivityLogEntry` history, when it is deleted, then those entries remain in the Workspace's activity log with `ProjectId` set to `NULL` rather than being deleted or blocking the transaction.

## 16. Future Improvements

- Project templates (pre-configured boards/columns/labels).
- Project-level custom settings (default assignee, issue type restrictions).
- Transferring a Project between Workspaces.
- Data export prior to deletion (CSV/JSON archive download).
- Project key change tooling with Issue-identifier migration.
