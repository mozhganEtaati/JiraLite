# 03 — Workspaces

## 1. Overview

Covers the multi-tenancy layer: Organizations, Workspaces, Workspace membership, and Invitations. A registered User ([01-authentication.md](01-authentication.md)) is not attached to any Organization/Workspace until they create one or accept an invitation to one.

## 2. Business Goal

Let a user create an isolated space (Workspace) for their team's Projects, and let Workspace Admins control who else can access it.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a new user, I can create an Organization and a Workspace to start using the platform with my team. |
| US-02 | As a Workspace Admin, I can invite teammates by email. |
| US-03 | As an invited person, I can accept or decline an invitation. |
| US-04 | As a Workspace Admin, I can remove a member or change their workspace role. |
| US-05 | As a Workspace member, I can see who else is in the workspace. |
| US-06 | As a user who has created one or more Organizations, I can list them. |
| US-07 | As a Workspace member, I can remove myself from a Workspace without needing an Admin to do it for me. |

## 4. Functional Requirements

- FR-01: An authenticated user can create an Organization; they become its Owner.
- FR-02: An Organization Owner can create one or more Workspaces within their Organization.
- FR-03: Creating a Workspace automatically adds the creator as a `WorkspaceMember` with role `Admin`.
- FR-04: A Workspace Admin can invite a person by email; the invitation carries the role the person will receive upon acceptance.
- FR-05: An invited person (once logged in with a matching email) can accept or decline the invitation.
- FR-06: A Workspace Admin can remove a member or change a member's role.
- FR-07: A Workspace Admin can revoke a pending invitation.
- FR-08: A Workspace Admin can archive the Workspace.
- FR-09: An authenticated user can list every Organization they own.
- FR-10: A Workspace member can leave a Workspace via a dedicated action, subject to the same last-Admin guard (BR-03) as an Admin-initiated removal.

## 5. Non-Functional Requirements

- NFR-01: Invitation tokens are cryptographically random and unguessable (≥128 bits of entropy).
- NFR-02: Membership and role checks are evaluated on every request to a Workspace/Project-scoped endpoint (no caching of role state in the JWT — see [01-authentication.md](01-authentication.md) BR-06).

## 6. Business Rules

- BR-01: In V1, only the Organization's `OwnerUserId` may create Workspaces within that Organization — there is no separate Organization-level membership list. Broader Organization membership is a Future Improvement (§16).
- BR-02: `WorkspaceMember.Role` is one of `Admin` or `Member`. Full authorization semantics are defined in [16-rbac.md](16-rbac.md); this document only establishes the field.
- BR-03: A Workspace must always have at least one `Admin`. Removing or demoting the last remaining Admin is rejected.
- BR-04: An Invitation's `Email` must match the accepting user's account email (case-insensitive) — a user cannot accept an invitation addressed to a different email.
- BR-05: Inviting an email that is already an active `WorkspaceMember` of that Workspace is rejected.
- BR-06: Creating a new invitation for an email that already has a `Pending` invitation to the same Workspace revokes the old invitation and creates a new one (no duplicate pending invitations).
- BR-07: Invitations expire after 7 days from creation (configurable via application settings).
- BR-08: Removing a `WorkspaceMember` cascades: all of that user's `ProjectMember` records within that Workspace's Projects are removed as well — a user cannot retain project-level access after losing workspace membership.
- BR-09: Workspace deletion is not supported in V1 — only archiving (`IsArchived`), since a Workspace may contain Projects, Issues, and history that must not be irrecoverably lost. Archived workspaces are read-only: no new Projects, Invitations, or Issues may be created within them.
- BR-10: A Workspace member may leave a Workspace via a dedicated `POST /api/workspaces/{workspaceId}/leave` action, kept separate from the Admin-only member-removal endpoint per [19-api-guidelines.md](19-api-guidelines.md) §4's convention of dedicated verb-routes for non-pure-CRUD state transitions (mirrors `POST /sprints/{sprintId}/start`, `POST /projects/{projectId}/archive`). BR-03's last-Admin guard still applies: the sole remaining Admin cannot leave either, and must first promote another member to Admin.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Organization

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| Name | string(200) | No | |
| OwnerUserId | Guid (FK → User) | No | |
| CreatedAtUtc | datetime2 | No | |

### Workspace

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| OrganizationId | Guid (FK → Organization) | No | |
| Name | string(200) | No | |
| Description | string(1000) | Yes | |
| IsArchived | bool | No | Default `false` |
| CreatedByUserId | Guid (FK → User) | No | |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### WorkspaceMember

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| WorkspaceId | Guid (FK → Workspace) | No | |
| UserId | Guid (FK → User) | No | |
| Role | string(20) | No | `Admin` \| `Member` |
| CreatedAtUtc | datetime2 | No | Join date |

Unique constraint: (`WorkspaceId`, `UserId`).

### Invitation

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| WorkspaceId | Guid (FK → Workspace) | No | |
| Email | string(256) | No | Invitee's email |
| Role | string(20) | No | Role granted on acceptance: `Admin` \| `Member` |
| Token | string(64) | No | Unique, unguessable |
| Status | string(20) | No | `Pending` \| `Accepted` \| `Declined` \| `Expired` \| `Revoked` |
| InvitedByUserId | Guid (FK → User) | No | |
| ExpiresAtUtc | datetime2 | No | |
| CreatedAtUtc | datetime2 | No | |
| AcceptedAtUtc | datetime2 | Yes | |
| AcceptedByUserId | Guid (FK → User) | Yes | |

## 8. Relationships

- `Organization (1) → Workspace (N)`
- `User (1) → Organization (N)` as Owner
- `Workspace (1) → WorkspaceMember (N)`
- `User (1) → WorkspaceMember (N)`
- `Workspace (1) → Invitation (N)`
- `User (1) → Invitation (N)` as InvitedBy / AcceptedBy

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/organizations` | Authenticated | List Organizations the caller owns |
| POST | `/api/organizations` | Authenticated | Create an Organization |
| PATCH | `/api/organizations/{orgId}` | Owner | Rename Organization |
| GET | `/api/organizations/{orgId}` | Owner | Get Organization |
| POST | `/api/organizations/{orgId}/workspaces` | Owner | Create a Workspace |
| GET | `/api/workspaces` | Authenticated | List Workspaces the caller is a member of |
| GET | `/api/workspaces/{workspaceId}` | Workspace Member | Get Workspace |
| PATCH | `/api/workspaces/{workspaceId}` | Workspace Admin | Edit Workspace name/description |
| POST | `/api/workspaces/{workspaceId}/archive` | Workspace Admin | Archive Workspace |
| GET | `/api/workspaces/{workspaceId}/members` | Workspace Member | List members |
| PATCH | `/api/workspaces/{workspaceId}/members/{userId}` | Workspace Admin | Change member role |
| DELETE | `/api/workspaces/{workspaceId}/members/{userId}` | Workspace Admin | Remove another member |
| POST | `/api/workspaces/{workspaceId}/leave` | Any Workspace member | Leave Workspace (BR-10) |
| GET | `/api/workspaces/{workspaceId}/invitations` | Workspace Admin | List pending invitations |
| POST | `/api/workspaces/{workspaceId}/invitations` | Workspace Admin | Create invitation |
| DELETE | `/api/workspaces/{workspaceId}/invitations/{invitationId}` | Workspace Admin | Revoke invitation |
| POST | `/api/invitations/{token}/accept` | Authenticated | Accept invitation |
| POST | `/api/invitations/{token}/decline` | Authenticated | Decline invitation |

## 10. Request Examples

**Create Organization**
```http
POST /api/organizations
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Acme Inc."
}
```

**List my Organizations**
```http
GET /api/organizations
Authorization: Bearer {accessToken}
```

**Leave a Workspace**
```http
POST /api/workspaces/{workspaceId}/leave
Authorization: Bearer {accessToken}
```

**Create Workspace**
```http
POST /api/organizations/{orgId}/workspaces
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Platform Team",
  "description": "Core platform and infrastructure"
}
```

**Invite member**
```http
POST /api/workspaces/{workspaceId}/invitations
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "email": "new.teammate@example.com",
  "role": "Member"
}
```

**Accept invitation**
```http
POST /api/invitations/{token}/accept
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**Create Organization — 201 Created**
```json
{
  "id": "1a2b3c4d-...",
  "name": "Acme Inc.",
  "ownerUserId": "3c1a1e2e-...",
  "createdAtUtc": "2026-07-31T10:00:00Z"
}
```

**List my Organizations — 200 OK**
```json
{
  "items": [
    { "id": "1a2b3c4d-...", "name": "Acme Inc.", "createdAtUtc": "2026-07-31T10:00:00Z" }
  ]
}
```

**Create Workspace — 201 Created**
```json
{
  "id": "7d8e9f0a-...",
  "organizationId": "1a2b3c4d-...",
  "name": "Platform Team",
  "description": "Core platform and infrastructure",
  "isArchived": false,
  "createdAtUtc": "2026-07-31T10:00:00Z"
}
```

**List members — 200 OK**
```json
{
  "items": [
    {
      "userId": "3c1a1e2e-...",
      "displayName": "Jane Doe",
      "avatarUrl": "https://cdn.jiralite.local/avatars/3c1a1e2e.png",
      "role": "Admin",
      "joinedAtUtc": "2026-07-31T10:00:00Z"
    }
  ]
}
```

**Leave a Workspace — 204 No Content**
(empty body)

## 12. Validation Rules

| Field | Rule |
|---|---|
| Organization.Name / Workspace.Name | Required, 1–200 chars |
| Workspace.Description | Optional, max 1000 chars |
| Invitation.Email | Required, valid email format |
| Invitation.Role / WorkspaceMember.Role | Required, one of `Admin`, `Member` |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Non-owner attempts to create a Workspace | 403 Forbidden | |
| Non-admin attempts member/invitation management | 403 Forbidden | |
| Invite an email already an active member | 409 Conflict | |
| Accept invitation with mismatched email | 403 Forbidden | |
| Accept/decline an expired or already-resolved invitation | 410 Gone | |
| Invalid/unknown invitation token | 404 Not Found | |
| Remove or demote the last Admin, or the last Admin attempting to leave | 409 Conflict | BR-03, BR-10 |
| Action on an archived Workspace that requires write access | 409 Conflict | BR-09 |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| Create Organization, list my Organizations | Any authenticated user |
| Manage Organization, create Workspace | Organization Owner only |
| View Workspace, list members | Any `WorkspaceMember` (Admin or Member) |
| Edit/archive Workspace, manage invitations | `WorkspaceMember.Role = Admin` |
| Change another member's role, remove another member | `WorkspaceMember.Role = Admin` |
| Remove self from Workspace (leave) | Any `WorkspaceMember`, subject to BR-03/BR-10 (cannot be the sole remaining Admin) |
| Accept/decline invitation | Authenticated user whose email matches the invitation |

## 15. Acceptance Criteria

- Given an authenticated user, when they create a Workspace under their Organization, then they are added as `WorkspaceMember` with role `Admin`.
- Given a Workspace Admin invites an email not yet a member, then a `Pending` invitation is created and an email is dispatched (see [13-notifications.md](13-notifications.md)).
- Given a matching-email authenticated user accepts a `Pending`, unexpired invitation, then a `WorkspaceMember` record is created with the invitation's `Role`, and the invitation's `Status` becomes `Accepted`.
- Given a Workspace with exactly one Admin, when removal or demotion of that Admin is attempted, then the request is rejected.
- Given a member is removed from a Workspace, then all their `ProjectMember` records in that Workspace's Projects are removed.
- Given a non-Admin Workspace member, when they call `POST /api/workspaces/{workspaceId}/leave`, then it succeeds without requiring an Admin to act on their behalf.
- Given a Workspace with exactly one Admin, when that Admin attempts to leave, then the request is rejected with 409 (same rule as being removed by someone else).
- Given a user who owns two Organizations, when they call `GET /api/organizations`, then both are returned.

## 16. Future Improvements

- Organization-level membership with multiple owners/admins.
- Workspace hard-deletion with explicit data export.
- Ownership transfer for an Organization.
- Bulk invitations (CSV/multiple emails at once).
- Invitation resend/reminder flow.
- Workspace-level configurable settings (branding, default board type, etc.).
