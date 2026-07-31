# 16 — RBAC (Roles & Permissions)

## 1. Overview

This is the canonical source for JiraLite's authorization model. "Roles" and "Permissions" are treated as a single capability, not two — there is no separate, dynamic, user-editable Permission entity (see [00-project-overview.md](00-project-overview.md) §2 Scope Validation, Recommendation 3). Every other document's "Authorization Rules" section is a local summary; this document is authoritative if any conflict is ever found.

JiraLite has exactly four named roles, split across two independent scopes:

| Scope | Roles |
|---|---|
| Workspace | `Admin` |
| Project | `ProjectAdmin`, `Developer`, `Viewer` |

`WorkspaceMember.Role` also has a `Member` value ([03-workspaces.md](03-workspaces.md)) — **this is not a fifth named platform role.** It is the required non-admin baseline value for the `Role` column (which cannot be null), meaning "belongs to the Workspace without elevated rights." All real authorization decisions at the Workspace level are simply "is this caller `Admin`, or not."

Two further flags exist outside this role system entirely and grant no RBAC authority: `TeamMember.IsLead` ([04-teams.md](04-teams.md) BR-03, scoped only to managing that one Team's roster) and `Organization.OwnerUserId` ([03-workspaces.md](03-workspaces.md), scoped only to creating Workspaces under that Organization).

## 2. Business Goal

Provide predictable, code-defined access control with no configuration surface — any developer can read this document and know exactly what every role can and cannot do, and no admin UI can accidentally grant more.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a developer implementing an endpoint, I can look up the single required role tier for that action. |
| US-02 | As a frontend, I can query my effective role for a Workspace or Project to decide what UI to show. |
| US-03 | As a Workspace Admin, my authority automatically extends to every Project in my Workspace without needing an explicit per-Project membership. |

## 4. Functional Requirements

- FR-01: Every write endpoint in the system enforces exactly one of the role tiers defined in §14, via a named ASP.NET Core authorization policy — never an inline role string comparison in a feature handler ([00-project-overview.md](00-project-overview.md) §6 Maintainability Risks).
- FR-02: A caller can retrieve their effective role for a given Workspace and for a given Project.

## 5. Non-Functional Requirements

- NFR-01: Role/permission checks are evaluated fresh on every request against `WorkspaceMember`/`ProjectMember` tables — never cached in the JWT ([01-authentication.md](01-authentication.md) BR-06).

## 6. Business Rules

- BR-01: **Permission sets are fixed in code**, defined once as authorization policies in `Common/Auth` ([20-coding-guidelines.md](20-coding-guidelines.md)). There is no database table of permissions and no endpoint to create, edit, or assign custom permissions.
- BR-02: **Role resolution algorithm** for any Project-scoped action:
  1. Look up `WorkspaceMember` for (caller, the Project's `WorkspaceId`). If `Role = Admin`, the caller has full authority over that Project — evaluation stops here.
  2. Otherwise, look up `ProjectMember` for (caller, the Project). If found, its `Role` (`ProjectAdmin` / `Developer` / `Viewer`) determines what the caller may do.
  3. If neither record exists, the caller has no access to that Project at all (403 on every action, including read).
- BR-03: **Workspace Admin authority is a strict superset** of `ProjectAdmin` for every Project in their Workspace, with exactly one exception: permanently deleting a Project requires `WorkspaceMember.Role = Admin` specifically — `ProjectAdmin` cannot do it ([05-projects.md](05-projects.md) BR-07). This is the only capability where Workspace Admin and Project Admin are not equivalent.
- BR-04: **Admin capabilities ([17-admin.md](17-admin.md)) are scoped per-Workspace.** There is no platform-wide super-admin role in V1 — a Workspace Admin administers Users (as members), Projects, and settings only within Workspaces where they hold the `Admin` role.
- BR-05: `TeamMember.IsLead` and `Organization.OwnerUserId` are **not** part of this role system; they never satisfy a `ProjectMember`/`WorkspaceMember` role check.
- BR-06: **Authorship alone is not sufficient to delete a Comment or Attachment.** The "delete own content" capability requires the caller currently hold `Developer` or `ProjectAdmin` on the Project *at the time of deletion*, evaluated the same way as any other write action (§14, BR-02). A Comment/Attachment authored while the user held `Developer` remains theirs, but if they are later demoted to `Viewer`, they lose the ability to delete it — closing the gap where authorship alone might otherwise be read as sufficient. See [10-comments.md](10-comments.md) BR-07, [11-attachments.md](11-attachments.md) BR-07.

## 7. Database Entities

No new entities. This document governs the semantics of existing role fields:

- `WorkspaceMember.Role` — `Admin` \| `Member` ([03-workspaces.md](03-workspaces.md))
- `ProjectMember.Role` — `ProjectAdmin` \| `Developer` \| `Viewer` ([05-projects.md](05-projects.md))

## 8. Relationships

No new relationships beyond those already defined in [03-workspaces.md](03-workspaces.md) and [05-projects.md](05-projects.md).

## 9. API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/workspaces/{workspaceId}/my-role` | Authenticated | Get caller's effective Workspace role |
| GET | `/api/projects/{projectId}/my-role` | Authenticated | Get caller's effective Project role (per BR-02 resolution) |

## 10. Request Examples

```http
GET /api/projects/{projectId}/my-role
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**Caller is an explicit Project Developer**
```json
{
  "projectId": "5e6f7a8b-...",
  "effectiveRole": "Developer",
  "viaWorkspaceAdmin": false
}
```

**Caller has no Project membership but is Workspace Admin**
```json
{
  "projectId": "5e6f7a8b-...",
  "effectiveRole": "Admin",
  "viaWorkspaceAdmin": true
}
```

**Caller has no access**
```json
{
  "projectId": "5e6f7a8b-...",
  "effectiveRole": null,
  "viaWorkspaceAdmin": false
}
```

## 12. Validation Rules

Read-only endpoints; no request body.

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Workspace/Project does not exist | 404 Not Found | |
| Caller not authenticated | 401 Unauthorized | |

`effectiveRole: null` (not an HTTP error) represents "no access," so the frontend can render an appropriate empty/denied state without a failed request.

## 14. Authorization Rules — Canonical Permission Matrix

**Workspace-scoped actions**

| Capability | Member | Admin |
|---|---|---|
| View Workspace, list members/Teams | ✅ | ✅ |
| Edit/archive Workspace | ❌ | ✅ |
| Manage members, invitations | ❌ | ✅ |
| Create/rename/delete Team | ❌ | ✅ |
| Create Project | ❌ | ✅ |

**Team-scoped actions** (orthogonal to the role table — see BR-05). Columns are about standing in the **Workspace**, not the individual Team — per [04-teams.md](04-teams.md) §14, any Workspace member can view any Team in that Workspace, whether or not they personally belong to it; only Team roster changes are gated by actual Team membership/lead status.

| Capability | Not a Workspace member | Workspace Member (any Team standing) | Team Lead of this Team | Workspace Admin |
|---|---|---|---|---|
| View this Team and its members | ❌ | ✅ | ✅ | ✅ |
| Add/remove this Team's members, set Lead flag | ❌ | ❌ (unless also Lead) | ✅ | ✅ |

**Project-scoped actions** (Workspace `Admin` always has full `ProjectAdmin`-equivalent authority per BR-02/BR-03; only the last row differs)

| Capability | Viewer | Developer | ProjectAdmin | Workspace Admin |
|---|---|---|---|---|
| View Project, Boards, Backlog, Issues, Comments, Attachments, Labels, Sprints, Calendar | ✅ | ✅ | ✅ | ✅ |
| Create/edit/move Issues; reposition backlog rank | ❌ | ✅ | ✅ | ✅ |
| Add Comments; upload Attachments; attach/detach Labels | ❌ | ✅ | ✅ | ✅ |
| Create, edit, start, complete Sprints; add/remove Sprint Issues | ❌ | ✅ | ✅ | ✅ |
| Delete Sprint | ❌ | ❌ | ✅ | ✅ |
| Delete own Comment/Attachment (author must currently hold Developer or ProjectAdmin — see [10-comments.md](10-comments.md) BR-07, [11-attachments.md](11-attachments.md) BR-07) | ❌ (Viewers cannot author content, and cannot delete even prior content authored before a demotion) | ✅ (own only) | ✅ | ✅ |
| Delete any Comment/Attachment (moderation) | ❌ | ❌ | ✅ | ✅ |
| Change Issue Reporter | ❌ | ❌ | ✅ | ✅ |
| Delete Issue | ❌ | ❌ | ✅ | ✅ |
| Manage Boards/Columns | ❌ | ❌ | ✅ | ✅ |
| Manage Label definitions (create/edit/delete) | ❌ | ❌ | ✅ | ✅ |
| Edit Project, archive/unarchive, manage Project members | ❌ | ❌ | ✅ | ✅ |
| Delete Project (permanent) | ❌ | ❌ | ❌ | ✅ |

## 15. Acceptance Criteria

- Given a `Viewer` on a Project, when they attempt any write action (create Issue, comment, upload, etc.), then every one is rejected with 403.
- Given a `ProjectAdmin`, when they attempt to permanently delete the Project, then it is rejected with 403 — only Workspace `Admin` can.
- Given a Workspace `Admin` with no explicit `ProjectMember` row on Project X, when they perform any Project-level action on X, then it succeeds as if they were `ProjectAdmin`.
- Given a user with neither a `WorkspaceMember` nor a `ProjectMember` record for a given Project, when they call `GET /api/projects/{projectId}/my-role`, then `effectiveRole` is `null`.
- Given a `TeamMember` with `IsLead = true`, when they attempt a Project-level write action without holding a qualifying `ProjectMember` role, then it is rejected with 403 (BR-05).
- Given a `ProjectAdmin`, when they attempt to delete a `Planned` Sprint, then it succeeds; given a `Developer`, the same request is rejected with 403 (Delete Sprint row, §14).
- Given a user who authored a Comment while `Developer` and was later demoted to `Viewer`, when they attempt to delete that Comment, then it is rejected with 403 (BR-06).
- Given a Workspace Member who is not on a particular Team, when they request that Team's details, then it succeeds (view is Workspace-scoped, not Team-scoped — Team-scoped table above).

## 16. Future Improvements

- Custom/configurable roles (explicitly out of scope for V1 per [00-project-overview.md](00-project-overview.md) Non-Goals).
- Fine-grained per-field permissions.
- Audit log specifically for permission/role changes (currently covered generically by [02-users.md](02-users.md) `ActivityLogEntry`).
