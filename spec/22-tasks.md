# 22 — Tasks

## 1. Overview

Task-level breakdown of the nine phases in [21-roadmap.md](21-roadmap.md). Each task has a unique ID, description, difficulty (S/M/L), dependencies on other task IDs, and acceptance criteria — pointing back to the relevant feature document's own §15 Acceptance Criteria rather than restating it, per the "reference, don't duplicate" rule used throughout this specification.

## 2. Phase 0 — Foundations & Infrastructure

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T001 | Scaffold .NET 10 solution and project structure per [20-coding-guidelines.md](20-coding-guidelines.md) §2 | S | — | Solution builds; folder layout matches the guideline |
| T002 | Configure EF Core + SQL Server, empty `JiraLiteDbContext`, initial migration | S | T001 | `dotnet ef database update` succeeds locally |
| T003 | Docker Compose for API + SQL Server | M | T002 | `docker compose up` boots both containers; API reachable |
| T004 | Wire Serilog structured logging + Swagger | S | T001 | Structured JSON logs emitted; Swagger UI loads at `/swagger` |
| T005 | Implement Problem Details middleware + validation pipeline behavior | M | T001 | An invalid request returns the shape in [19-api-guidelines.md](19-api-guidelines.md) §9 |
| T006 | Wire Hangfire (dashboard + job infrastructure, no jobs yet) | M | T002 | Dashboard accessible; a test job enqueues and executes |
| T007 | Add health-check endpoint | S | T001 | `GET /health` returns 200 |

## 3. Phase 1 — Identity & Access

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T008 | `User`, `RefreshToken`, `UserProfile`, `NotificationPreference` entities + migration | M | T002 | Matches [18-database.md](18-database.md) §3 |
| T009 | Register + Login endpoints | M | T008 | [01-authentication.md](01-authentication.md) §15 register/login criteria |
| T010 | JWT issuance + Refresh (rotation, reuse detection) | L | T009 | [01](01-authentication.md) §15 refresh/reuse-detection criteria |
| T011 | Logout endpoint | S | T010 | [01](01-authentication.md) §15 logout criteria |
| T012 | `GET/PATCH /users/me` + avatar upload (via `IFileStorage`) | M | T008 | [02-users.md](02-users.md) §15 profile/avatar criteria |
| T013 | Notification preference endpoints | S | T008 | [02](02-users.md) §15 preferences criteria |
| T013A | Self-service account deactivation (`POST /api/users/me/deactivate`) | S | T010 | [02](02-users.md) §15 deactivation criteria |

Note: `ActivityLogEntry` (and the Activity History read endpoint it powers, [02-users.md](02-users.md)) is **not** built in this phase. `ActivityLogEntry.WorkspaceId`/`ProjectId` are foreign keys to `Workspace`/`Project`, which don't exist until `T015` (Phase 2) and `T022` (Phase 3) — a migration creating those constraints here would fail. See `T014` in Phase 3.

## 4. Phase 2 — Workspace & Membership

Note on ordering: T021 (authorization policies) is deliberately sequenced right after the entities it checks against (T015), and *before* T017/T018 — those tasks' own acceptance criteria require rejecting non-admins with 403, which is not achievable until the policy layer exists. This resolves a dependency-ordering issue found in architecture review (policies must precede the endpoints that enforce them, not follow). T016 depends only on T015, not T021: Create Organization and Create Workspace are authorized via `Organization.OwnerUserId`, a distinct, simpler mechanism from the `WorkspaceMember.Role` policies T021 builds — they don't require the policy layer.

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T015 | `Organization`, `Workspace`, `WorkspaceMember`, `Invitation` entities + migration | M | T008 | Matches [18-database.md](18-database.md) §5 |
| T021 | Workspace-scoped authorization policies + `GET /workspaces/{id}/my-role` | M | T015 | [16-rbac.md](16-rbac.md) §15 Workspace-scoped criteria |
| T016 | Create Organization, list my Organizations, Create Workspace endpoints | M | T015 | [03-workspaces.md](03-workspaces.md) §15 create criteria |
| T017 | Workspace membership management (invite/accept/decline/remove/role change) + leave-Workspace endpoint + last-Admin guard | L | T016, T021 | [03](03-workspaces.md) §15 membership/invitation/leave criteria |
| T018 | Workspace edit/archive | S | T016, T021 | Archived Workspace blocks writes (BR-09) |
| T019 | `Team`, `TeamMember` entities + migration | S | T015 | Matches [18-database.md](18-database.md) §5 |
| T020 | Team CRUD + membership/lead management | M | T019, T017 | [04-teams.md](04-teams.md) §15 criteria |

## 5. Phase 3 — Project Planning

Note on ordering: as in Phase 2, T028 (Project-scoped authorization policies) is sequenced right after the entities it checks against (T022), and before the CRUD tasks whose acceptance criteria depend on it (T023, T024, T026, T027). T014 (`ActivityLogEntry`, deferred from Phase 1 — see that phase's note) is placed here because it needs `Project` to exist for its FK; it only depends on T022, not on the Project-scoped policy work. T027 (`Sprint` entity) is sequenced before T026 (Board CRUD) because T026 implements the Sprint-reference delete guard ([06-boards.md](06-boards.md) BR-09), which requires the `Sprint` table to exist and be queryable.

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T022 | `Project`, `ProjectMember` entities + migration | M | T015 | Matches [18-database.md](18-database.md) §6 |
| T014 | `ActivityLogEntry` entity + migration, and its read endpoint (deferred from Phase 1) | S | T022 | Endpoint paginates correctly against an empty/seeded table; matches [18-database.md](18-database.md) §4 |
| T028 | Full Project-scoped authorization policies + role-resolution algorithm + `GET /projects/{id}/my-role` | M | T022, T021 | [16-rbac.md](16-rbac.md) §15 Project-scoped criteria |
| T023 | Project CRUD (create/edit/archive/delete, archive-before-delete rail) | M | T022, T028 | [05-projects.md](05-projects.md) §15 criteria |
| T024 | Project member management | S | T023, T028 | [05](05-projects.md) §15 member criteria |
| T025 | `Board`, `BoardColumn` entities + migration; default Board/columns auto-created on Project create | L | T022 | [06-boards.md](06-boards.md) §15 default-board criteria |
| T027 | `Sprint` entity + migration; lifecycle (create/start/complete) + single-active-Sprint guard | L | T025, T028 | [08-sprints.md](08-sprints.md) §15 criteria |
| T026 | Board/Column CRUD + reorder with concurrency token, incl. Sprint-reference delete guard | M | T025, T027, T028 | [06](06-boards.md) §15 reorder/delete-guard criteria |

## 6. Phase 4 — Work Tracking

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T029 | `Issue` entity + migration, incl. sequential `Number`/`Key` generation | L | T025, T027 | Matches [18-database.md](18-database.md) §7; unique (`ProjectId`, `Number`) |
| T030 | Create/Edit Issue + hierarchy validation (Epic/Story/Task/Bug/Subtask rules) | L | T029 | [09-issues.md](09-issues.md) §15 hierarchy criteria |
| T031 | Move Issue (column change, cross-Board transfer, Kanban `SprintId`-clear rule) with concurrency | M | T030 | [09](09-issues.md) §15 move criteria |
| T032 | Delete Issue (cascade Subtasks / detach Epic children) | M | T030 | [09](09-issues.md) §15 delete criteria |
| T033 | Product/Sprint Backlog endpoints + Rank reposition + rebalance job | L | T029, T006 | [07-backlog.md](07-backlog.md) §15 criteria |
| T034 | `Comment` entity + migration + CRUD | S | T029 | [10-comments.md](10-comments.md) §15 criteria |
| T035 | `Attachment` entity + migration + `LocalDiskFileStorage` + upload/download/preview | L | T029 | [11-attachments.md](11-attachments.md) §15 criteria |
| T036 | `Label`, `IssueLabel` entities + migration + CRUD + attach/detach | M | T029 | [12-labels.md](12-labels.md) §15 criteria |

## 7. Phase 5 — Notifications & Activity

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T037 | `Notification` entity + migration + list/mark-read endpoints | M | T008 | [13-notifications.md](13-notifications.md) §15 list/read criteria |
| T038 | Email delivery Hangfire job + `SmtpEmailSender` | M | T006, T037 | Enqueued job sends a real/test email in the dev environment |
| T039 | Wire notification triggers into Issue assignment/move and Comment handlers | M | T030, T031, T034, T037 | [13](13-notifications.md) §15 assignment/comment criteria |
| T040 | Wire invitation email trigger into Workspace invitation handler | S | T017, T038 | [13](13-notifications.md) §15 invitation-email criteria (BR-06: no `Notification` row) |
| T041 | Retrofit `ActivityLogEntry` writes into mutating handlers across Phases 1–4 | L | T014, T008, T017, T023, T030 | Activity entries appear for a representative action from each phase |

## 8. Phase 6 — Reporting Views & Admin

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T042 | Dashboard endpoints (My Tasks, My Projects, Recent Activity) | M | T030, T024, T041 | [14-dashboard.md](14-dashboard.md) §15 criteria |
| T043 | Calendar endpoints (Due Dates, Sprint Timeline) | S | T030, T027 | [15-calendar.md](15-calendar.md) §15 criteria |
| T044 | Admin console endpoints (overview, users, projects, roles catalog) | M | T024, T028 | [17-admin.md](17-admin.md) §15 criteria |

## 9. Phase 7 — Hardening & Release Readiness

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T045 | Configure per-user/per-endpoint rate limiting | M | T009 | Exceeding the limit returns 429 with correct headers |
| T046 | Load-test index/concurrency behavior against [18-database.md](18-database.md) NFRs | L | T031, T033 | Query plans use the expected indexes; concurrent move/rank conflicts return 409 as designed |
| T047 | Production Docker image + controlled migration deployment step | M | T003 | Image runs the full stack; migrations are not auto-applied outside Development ([20-coding-guidelines.md](20-coding-guidelines.md) §9) |
| T048 | Full regression pass against every feature document's acceptance criteria ([01](01-authentication.md)–[17](17-admin.md)) | L | All prior tasks | Every §15 acceptance-criteria bullet across 01–17 verified |
| T049 | Write the deployment runbook | S | T047 | Runbook covers deploy, rollback, and migration-application steps |

## 10. Phase 8 — MCP Server

Note on ordering: T052 (the Personal Access Token authentication scheme) is sequenced before both tool tasks because their acceptance criteria are authorization criteria — a Viewer being refused, a demoted user being refused — none of which can be demonstrated until the credential they authorize against exists. T054 follows T053 rather than running beside it so the read surface proves the host wiring before any tool can mutate state.

| ID | Description | Difficulty | Dependencies | Acceptance Criteria |
|---|---|---|---|---|
| T050 | `PersonalAccessToken` entity + migration | S | T008 | Matches [23-mcp-server.md](23-mcp-server.md) §7; token value never persisted in plaintext (NFR-01) |
| T051 | Token management endpoints (create/list/revoke) | M | T050 | [23-mcp-server.md](23-mcp-server.md) §15 token criteria |
| T052 | Personal Access Token authentication scheme, separate from JWT | M | T051, T045 | [23](23-mcp-server.md) §15 credential-separation and revocation criteria (BR-02, BR-04, BR-07) |
| T053 | MCP server host + read tools | L | T052, T030, T026, T027, T034, T042 | [23](23-mcp-server.md) §15 tool-list and read criteria; no excluded tool advertised (BR-06) |
| T054 | MCP write tools | M | T053, T031, T039 | [23](23-mcp-server.md) §15 write, refusal, and fresh-role-resolution criteria (BR-01) |
| T055 | Client setup documentation + real-client verification | S | T054, T049 | A real MCP client connects, reads, and moves an Issue; runbook covers the flag and revocation |

## 11. Related Documents

- [21-roadmap.md](21-roadmap.md) — the phase-level plan this task list implements
- [README.md](README.md) — full specification index
