# 17 — Admin

## 1. Overview

Covers the Workspace Admin console: consolidated, governance-oriented views over Users (members), Roles, Projects, and the Workspace itself. Per [16-rbac.md](16-rbac.md) BR-04, this is a **per-Workspace** overlay, not a platform-wide super-admin surface — every endpoint here requires `WorkspaceMember.Role = Admin` for the specific Workspace being administered. This document does not duplicate the mutating endpoints already defined in [03-workspaces.md](03-workspaces.md), [04-teams.md](04-teams.md), and [05-projects.md](05-projects.md) (invite/remove member, create/archive/delete Project, etc.) — it adds only the read/aggregate views an admin console needs, which those documents don't otherwise provide.

## 2. Business Goal

Give a Workspace Admin a single place to see everything under their authority — members and their roles across every Project, all Projects including archived ones, and the fixed role catalog — without building a second, platform-spanning admin system.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Workspace Admin, I can see a summary of my Workspace: member, team, project, and pending-invitation counts. |
| US-02 | As a Workspace Admin, I can see every member's Workspace role and their role on each Project, in one list. |
| US-03 | As a Workspace Admin, I can see every Project in my Workspace, including archived ones, with basic size stats. |
| US-04 | As a Workspace Admin, I can see the fixed catalog of roles available to assign. |

## 4. Functional Requirements

- FR-01: A Workspace Admin can retrieve a summary overview of their Workspace.
- FR-02: A Workspace Admin can retrieve a list of all Workspace members, each with their Workspace role and their role on every Project they belong to.
- FR-03: A Workspace Admin can retrieve a list of all Projects in the Workspace, including archived ones, with member and Issue counts.
- FR-04: A Workspace Admin can retrieve the static catalog of Workspace- and Project-scoped roles defined in [16-rbac.md](16-rbac.md).

## 5. Non-Functional Requirements

- NFR-01: Admin list endpoints (`users`, `projects`) are paginated per [19-api-guidelines.md](19-api-guidelines.md), since a Workspace may accumulate many members/Projects over time.

## 6. Business Rules

- BR-01: Every endpoint in this document requires `WorkspaceMember.Role = Admin` on the target Workspace — there is no role that grants Admin visibility across Workspaces the caller does not belong to.
- BR-02: Admin list views include data that member-facing views hide by default: archived Projects (vs. [05-projects.md](05-projects.md) default list) and deactivated Users' historical membership — governance visibility takes priority over the "hide inactive by default" convention used elsewhere.
- BR-03: The Roles catalog (FR-04) is static, identical for every Workspace, and sourced directly from the code-defined policies in [16-rbac.md](16-rbac.md) — it is not stored per-Workspace and cannot be edited.
- BR-04: All mutations (removing a member, changing a role, archiving/deleting a Project, managing invitations) happen through the existing endpoints in [03-workspaces.md](03-workspaces.md) and [05-projects.md](05-projects.md); this document is read-only.

## 7. Database Entities

No new entities. This document composes reads over `WorkspaceMember`, `ProjectMember`, `Project`, `Team`, and `Invitation` (see [03-workspaces.md](03-workspaces.md), [04-teams.md](04-teams.md), [05-projects.md](05-projects.md)).

## 8. Relationships

No new relationships.

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/workspaces/{workspaceId}/admin/overview` | Workspace Admin | Summary counts |
| GET | `/api/workspaces/{workspaceId}/admin/users` | Workspace Admin | All members with Workspace + per-Project roles |
| GET | `/api/workspaces/{workspaceId}/admin/projects` | Workspace Admin | All Projects, including archived, with stats |
| GET | `/api/workspaces/{workspaceId}/admin/roles` | Workspace Admin | Static role catalog |

## 10. Request Examples

```http
GET /api/workspaces/{workspaceId}/admin/overview
Authorization: Bearer {accessToken}
```

```http
GET /api/workspaces/{workspaceId}/admin/users?limit=50
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**Overview — 200 OK**
```json
{
  "workspaceId": "7d8e9f0a-...",
  "memberCount": 12,
  "teamCount": 3,
  "projectCount": 5,
  "activeProjectCount": 4,
  "archivedProjectCount": 1,
  "pendingInvitationCount": 2
}
```

**Users — 200 OK**
```json
{
  "items": [
    {
      "userId": "3c1a1e2e-...",
      "displayName": "Jane Doe",
      "email": "jane.doe@example.com",
      "isActive": true,
      "workspaceRole": "Admin",
      "joinedAtUtc": "2026-07-31T10:00:00Z",
      "projectRoles": [
        { "projectId": "5e6f7a8b-...", "projectKey": "JIRA", "role": "ProjectAdmin" }
      ]
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

**Projects — 200 OK**
```json
{
  "items": [
    {
      "projectId": "5e6f7a8b-...",
      "key": "JIRA",
      "name": "JiraLite Platform",
      "isArchived": false,
      "memberCount": 6,
      "issueCount": 148,
      "createdAtUtc": "2026-07-31T10:00:00Z"
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

**Roles — 200 OK**
```json
{
  "items": [
    { "scope": "Workspace", "role": "Admin", "description": "Full authority over the Workspace and every Project within it." },
    { "scope": "Workspace", "role": "Member", "description": "Baseline Workspace membership; no elevated rights." },
    { "scope": "Project", "role": "ProjectAdmin", "description": "Full authority over a single Project, its Boards, and members." },
    { "scope": "Project", "role": "Developer", "description": "Can create and edit Issues, Comments, Attachments, and Sprints." },
    { "scope": "Project", "role": "Viewer", "description": "Read-only access to a Project." }
  ]
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| limit / cursor | Optional, per [19-api-guidelines.md](19-api-guidelines.md) §5 |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Caller is not a Workspace Admin | 403 Forbidden | BR-01 |
| Workspace not found | 404 Not Found | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| All endpoints in this document | `WorkspaceMember.Role = Admin` on the target Workspace |

## 15. Acceptance Criteria

- Given a Workspace with 12 members and 5 Projects (1 archived), when the overview is requested, then the counts match exactly, including the archived Project.
- Given a member who is `ProjectAdmin` on one Project and has no membership on another, when the admin Users list is requested, then their `projectRoles` array contains only the one Project.
- Given a non-Admin Workspace member, when any endpoint in this document is called, then it is rejected with 403.
- Given the Roles catalog is requested from two different Workspaces, then the response is identical (BR-03).

## 16. Future Improvements

- Storage usage reporting (Attachment bytes per Workspace).
- Admin-initiated bulk actions (bulk archive stale Projects).
- Exportable audit trail for administrative actions.
