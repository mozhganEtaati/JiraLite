# 21 — Roadmap

## 1. Overview

Eight sequential phases, each building only on entities and endpoints delivered by earlier phases — the same dependency logic used in [00-project-overview.md](00-project-overview.md)'s bounded contexts. No phase implements a feature whose prerequisites aren't already in place (e.g., Issues aren't built before Boards exist, since every Issue requires a `BoardColumnId`). Task-level breakdown of each phase is in [22-tasks.md](22-tasks.md).

## 2. Phase 0 — Foundations & Infrastructure

**Goals:** Stand up the solution skeleton and cross-cutting infrastructure so every later phase builds on solid ground.

**Deliverables:**
- .NET 10 solution scaffold matching [20-coding-guidelines.md](20-coding-guidelines.md) §2 folder structure
- `JiraLiteDbContext` (empty model), SQL Server connectivity, Docker Compose (API + SQL Server)
- Serilog, Swagger, Problem Details middleware ([19-api-guidelines.md](19-api-guidelines.md) §9), validation pipeline behavior
- JWT authentication middleware skeleton (no user-facing endpoints yet)
- Hangfire dashboard wired up (no jobs yet)

**Dependencies:** None — first phase.

**Definition of Ready:** [18-database.md](18-database.md), [19-api-guidelines.md](19-api-guidelines.md), [20-coding-guidelines.md](20-coding-guidelines.md) approved.

**Definition of Done:** `docker compose up` boots the API and SQL Server; Swagger UI loads; a health-check endpoint returns 200; Serilog emits structured logs; an empty EF Core migration applies cleanly.

## 3. Phase 1 — Identity & Access

**Goals:** A user can register, authenticate, and manage their own profile.

**Deliverables:** `User`, `RefreshToken`, `UserProfile`, `NotificationPreference` entities/migrations; all endpoints in [01-authentication.md](01-authentication.md) and [02-users.md](02-users.md) except Activity History, including self-service account deactivation. Activity History (`ActivityLogEntry` and its read endpoint) is deferred to Phase 3: `ActivityLogEntry.WorkspaceId`/`ProjectId` are foreign keys to `Workspace`/`Project`, which don't exist until Phases 2 and 3 — building it here would produce a migration with a constraint referencing a nonexistent table.

**Dependencies:** Phase 0.

**Definition of Ready:** [01](01-authentication.md), [02](02-users.md) approved.

**Definition of Done:** Register → Login → Refresh → Logout works end-to-end via Swagger; profile/avatar/notification-preference/deactivation endpoints functional; all acceptance criteria in [01](01-authentication.md) §15 pass, and all in [02](02-users.md) §15 pass **except** the Activity History write/read criterion, which is only fully verifiable once the entity exists (Phase 3) and writers are retrofitted into mutating handlers (Phase 5, `T041`).

## 4. Phase 2 — Workspace & Membership

**Goals:** Multi-tenant structure exists: Organizations, Workspaces, membership, invitations, Teams.

**Deliverables:** `Organization`, `Workspace`, `WorkspaceMember`, `Invitation`, `Team`, `TeamMember` entities/migrations; all endpoints in [03-workspaces.md](03-workspaces.md) and [04-teams.md](04-teams.md); Workspace-level authorization policies ([16-rbac.md](16-rbac.md) §14 Workspace-scoped table).

**Dependencies:** Phase 1 (Users must exist to be invited/assigned).

**Definition of Ready:** [03](03-workspaces.md), [04](04-teams.md), [16](16-rbac.md) approved.

**Definition of Done:** A user creates an Organization and Workspace, invites a teammate, the teammate accepts, and both see each other as members; last-Admin-removal guard applies equally to Admin-initiated removal and self-initiated leaving ([03-workspaces.md](03-workspaces.md) BR-03, BR-10), `GET /api/organizations` lists the caller's Organizations, and Team Lead delegation ([04-teams.md](04-teams.md)) is verified by test.

## 5. Phase 3 — Project Planning

**Goals:** Projects, Boards/Columns, and Sprints exist, with correct default-Board/default-Column bootstrapping.

**Deliverables:** `Project`, `ProjectMember`, `Board`, `BoardColumn`, `Sprint`, `ActivityLogEntry` entities/migrations (the latter deferred from Phase 1 — see that phase's note) plus the [02-users.md](02-users.md) Activity History read endpoint; all endpoints in [05-projects.md](05-projects.md), [06-boards.md](06-boards.md), [08-sprints.md](08-sprints.md); full Project-level authorization policies and the role-resolution algorithm ([16-rbac.md](16-rbac.md) BR-02).

**Dependencies:** Phase 2 (Projects belong to Workspaces).

**Definition of Ready:** [05](05-projects.md), [06](06-boards.md), [08](08-sprints.md), [16](16-rbac.md) approved.

**Definition of Done:** Creating a Project auto-creates its default Board and columns ([06-boards.md](06-boards.md) FR-01); Sprint start/complete lifecycle enforces single-active-sprint-per-board; a Board cannot be deleted while any Sprint references it ([06-boards.md](06-boards.md) BR-09); Project archive-before-delete rail enforced; `GET /api/users/me/activity` returns correctly-paginated results (still empty until Phase 5 wires up writers); all acceptance criteria in these three documents pass.

## 6. Phase 4 — Work Tracking

**Goals:** The core value proposition: Issues (with hierarchy), backlog ranking, Comments, Attachments, Labels.

**Note on carry-over from Phase 3:** Three pieces of `spec/08-sprints.md` and `spec/06-boards.md` behavior could not be built in Phase 3 because they require `Issue`, which doesn't exist until this phase — mirroring the same forward-reference logic already documented for `ActivityLogEntry` in Phase 1's note. All three must be added here, alongside `Issue`:
- **Sprint completion carry-forward** ([08-sprints.md](08-sprints.md) BR-05): Phase 3's `CompleteSprint` only performs the `Active → Completed` status transition; the "move incomplete Issues to the Product Backlog or another Sprint" logic must be retrofitted once `Issue` exists.
- **`POST/DELETE /sprints/{sprintId}/issues`** and **`GET /boards/{boardId}/issues`** ([08-sprints.md](08-sprints.md) §9, [06-boards.md](06-boards.md) §9): not implemented in Phase 3 at all — both require querying/mutating `Issue`.
- **Board/Column delete Issue-presence guards** ([06-boards.md](06-boards.md) BR-03, BR-05): Phase 3's `DeleteBoard`/`DeleteColumn` only enforce the structural guards (last-Board, last-Column) and the Sprint-reference guard (BR-09); the Issue-presence checks must be added to both handlers once `Issue` exists.

**Deliverables:** `Issue`, `Comment`, `Attachment`, `Label`, `IssueLabel` entities/migrations; all endpoints in [07-backlog.md](07-backlog.md), [09-issues.md](09-issues.md), [10-comments.md](10-comments.md), [11-attachments.md](11-attachments.md), [12-labels.md](12-labels.md); `LocalDiskFileStorage` implementation of `IFileStorage`.

**Dependencies:** Phase 3 (every Issue requires an existing `BoardColumnId`; Sprint assignment requires Sprints to exist).

**Definition of Ready:** [07](07-backlog.md), [09](09-issues.md), [10](10-comments.md), [11](11-attachments.md), [12](12-labels.md) approved.

**Definition of Done:** Full Issue CRUD including Epic/Story/Task/Bug/Subtask hierarchy rules ([09-issues.md](09-issues.md) BR-01–BR-06); Move and Rank endpoints enforce optimistic concurrency; Comments/Attachments/Labels functional with correct role gating; all acceptance criteria in these five documents pass.

## 7. Phase 5 — Notifications & Activity

**Goals:** Users are notified of relevant events, and activity is recorded and browsable.

**Deliverables:** `Notification` entity/migration (`ActivityLogEntry` itself was already delivered in Phase 3); Hangfire email delivery job; notification triggers wired into Phase 2 (invitation emails) and Phase 4 (Issue assignment/status change, Comment) handlers; all endpoints in [13-notifications.md](13-notifications.md); `ActivityLogEntry` writes retrofitted into every mutating handler from Phases 1–4.

**Dependencies:** Phases 1–4 (triggers hook into their handlers).

**Definition of Ready:** [13-notifications.md](13-notifications.md) approved.

**Definition of Done:** Creating a Comment or reassigning an Issue produces correctly-scoped `Notification` rows and enqueues email jobs per recipient `NotificationPreference` ([13-notifications.md](13-notifications.md) BR-02); activity entries appear for key actions across every prior phase; all acceptance criteria in [13](13-notifications.md) §15 pass.

## 8. Phase 6 — Reporting Views & Admin

**Goals:** The read-projection layer over everything built so far: Dashboard, Calendar, Admin console.

**Deliverables:** All endpoints in [14-dashboard.md](14-dashboard.md), [15-calendar.md](15-calendar.md), [17-admin.md](17-admin.md). No new entities or migrations.

**Dependencies:** Phases 1–5 (these views read across every entity built previously).

**Definition of Ready:** [14](14-dashboard.md), [15](15-calendar.md), [17](17-admin.md) approved.

**Definition of Done:** My Tasks/My Projects/Recent Activity, Due Dates/Sprint Timeline, and the Admin console all return correctly-scoped data against a seeded multi-Project, multi-Workspace dataset; all acceptance criteria in these three documents pass.

## 9. Phase 7 — Hardening & Release Readiness

**Goals:** Production readiness: security, performance, and a repeatable deployment path.

**Deliverables:** Rate limiting configured ([19-api-guidelines.md](19-api-guidelines.md) §13); index/concurrency behavior verified against [18-database.md](18-database.md) NFRs under load; production Docker image; a controlled, non-automatic migration deployment step ([20-coding-guidelines.md](20-coding-guidelines.md) §9); a smoke-test pass covering every acceptance-criteria list across [01](01-authentication.md)–[17](17-admin.md).

**Dependencies:** Phases 0–6 complete.

**Definition of Ready:** All feature documents implemented and individually verified in their own phase.

**Definition of Done:** A full regression pass across all acceptance criteria in [01](01-authentication.md)–[17](17-admin.md) succeeds; the production Docker image builds and runs the full stack; a deployment runbook exists.

## 10. Related Documents

- [22-tasks.md](22-tasks.md) — task-level breakdown of each phase above
