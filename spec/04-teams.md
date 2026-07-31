# 04 — Teams

## 1. Overview

Covers Teams: sub-groupings of `WorkspaceMember`s within a Workspace (e.g., "Backend Team"), used for organization and visibility. Teams are **not** an access-control construct — see BR-03.

## 2. Business Goal

Let a Workspace organize its members into smaller groups for clarity and filtering, without introducing a second permission system.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Workspace Admin, I can create a Team and name it. |
| US-02 | As a Workspace Admin or Team Lead, I can add or remove Workspace members from a Team. |
| US-03 | As a Workspace Admin or Team Lead, I can designate one or more members as Team Lead. |
| US-04 | As a Workspace member, I can see which Teams exist and who is on each. |

## 4. Functional Requirements

- FR-01: A Workspace Admin can create, rename, and delete a Team.
- FR-02: A Workspace Admin or existing Team Lead can add/remove members of that Team.
- FR-03: A Workspace Admin or existing Team Lead can toggle the `IsLead` flag for a Team member.
- FR-04: Any Workspace member can list Teams and view their membership.

## 5. Non-Functional Requirements

- NFR-01: Team membership changes take effect immediately for any UI/filtering purpose; there is no caching layer to invalidate.

## 6. Business Rules

- BR-01: Only existing `WorkspaceMember`s of the same Workspace can be added to a Team. A user must join the Workspace before joining any of its Teams.
- BR-02: A Team may have zero, one, or multiple Leads (`IsLead = true`); there is no minimum requirement, unlike Workspace's last-Admin rule ([03-workspaces.md](03-workspaces.md) BR-03).
- BR-03: Team membership and the `IsLead` flag grant **no** project or workspace access by themselves. They are an organizational/visibility construct only. All access control is governed exclusively by `WorkspaceMember.Role` and `ProjectMember.Role` — see [16-rbac.md](16-rbac.md). A Team Lead's authority is limited to managing that specific Team's roster (FR-02, FR-03).
- BR-04: A user may belong to multiple Teams within the same Workspace simultaneously.
- BR-05: Deleting a Team cascades deletion of its `TeamMember` records only. It never deletes Users, `WorkspaceMember` records, Projects, or Issues.
- BR-06: Removing a user from a Workspace ([03-workspaces.md](03-workspaces.md) BR-08) also removes them from any Teams within that Workspace.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Team

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| WorkspaceId | Guid (FK → Workspace) | No | |
| Name | string(100) | No | |
| Description | string(500) | Yes | |
| CreatedByUserId | Guid (FK → User) | No | |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### TeamMember

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| TeamId | Guid (FK → Team) | No | |
| UserId | Guid (FK → User) | No | |
| IsLead | bool | No | Default `false` |
| CreatedAtUtc | datetime2 | No | Joined date |

Unique constraint: (`TeamId`, `UserId`).

## 8. Relationships

- `Workspace (1) → Team (N)`
- `Team (1) → TeamMember (N)`
- `User (1) → TeamMember (N)`

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/workspaces/{workspaceId}/teams` | Workspace Member | List Teams |
| POST | `/api/workspaces/{workspaceId}/teams` | Workspace Admin | Create Team |
| GET | `/api/teams/{teamId}` | Workspace Member | Get Team + members |
| PATCH | `/api/teams/{teamId}` | Workspace Admin | Rename/edit Team |
| DELETE | `/api/teams/{teamId}` | Workspace Admin | Delete Team |
| POST | `/api/teams/{teamId}/members` | Workspace Admin or Team Lead | Add member |
| DELETE | `/api/teams/{teamId}/members/{userId}` | Workspace Admin or Team Lead | Remove member |
| PATCH | `/api/teams/{teamId}/members/{userId}` | Workspace Admin or Team Lead | Set/unset `IsLead` |

## 10. Request Examples

**Create Team**
```http
POST /api/workspaces/{workspaceId}/teams
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Backend Team",
  "description": "Owns API and infrastructure services"
}
```

**Add member**
```http
POST /api/teams/{teamId}/members
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "userId": "3c1a1e2e-6b1a-4e9a-9c3e-1a2b3c4d5e6f"
}
```

**Set Team Lead**
```http
PATCH /api/teams/{teamId}/members/{userId}
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "isLead": true
}
```

## 11. Response Examples

**GET /api/teams/{teamId} — 200 OK**
```json
{
  "id": "9b8c7d6e-...",
  "workspaceId": "7d8e9f0a-...",
  "name": "Backend Team",
  "description": "Owns API and infrastructure services",
  "members": [
    {
      "userId": "3c1a1e2e-...",
      "displayName": "Jane Doe",
      "avatarUrl": "https://cdn.jiralite.local/avatars/3c1a1e2e.png",
      "isLead": true,
      "joinedAtUtc": "2026-07-31T10:00:00Z"
    }
  ]
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| Name | Required, 1–100 chars |
| Description | Optional, max 500 chars |
| userId (add member) | Required, must reference an existing `WorkspaceMember` of the same Workspace |
| isLead | Boolean, required |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Add a user who is not a Workspace member | 400 Bad Request | BR-01 |
| Add a user already on the Team | 409 Conflict | |
| Team not found | 404 Not Found | |
| Non-admin, non-lead attempts management action | 403 Forbidden | |
| Rename with empty name | 400 Bad Request | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Teams/members | Any `WorkspaceMember` |
| Create/rename/delete Team | `WorkspaceMember.Role = Admin` |
| Add/remove Team members, toggle `IsLead` | `WorkspaceMember.Role = Admin` **or** the caller has `IsLead = true` on that Team |

## 15. Acceptance Criteria

- Given a Workspace Admin, when they create a Team, then it is created with zero members.
- Given a Team Lead, when they add another Workspace member to their Team, then a `TeamMember` record is created without requiring Admin involvement.
- Given a user who is not a member of the Workspace, when adding them to a Team is attempted, then the request is rejected.
- Given a Team is deleted, then all its `TeamMember` records are removed, and the underlying `User`/`WorkspaceMember` records are unaffected.
- Given a `TeamMember` with `IsLead = true`, they cannot access any Project or Issue they would not otherwise have access to via their own `ProjectMember` role (BR-03).

## 16. Future Improvements

- Team-based default assignment or notification routing (e.g., "assign to Backend Team").
- Team-level dashboards/filters (e.g., "issues owned by my team").
- Nested/sub-teams.
- Team avatar/color for visual grouping in the UI.
