# Phase 7 — Hardening & Release Readiness

Implements [spec/21-roadmap.md](../../spec/21-roadmap.md) §9 and tasks **T045–T049** of
[spec/22-tasks.md](../../spec/22-tasks.md) §9. No new features; this phase makes what
Phases 0–6 built safe to deploy.

Branch: `phase-7-hardening`.

---

## Task 1 — T045: Rate limiting

**Spec:** [19-api-guidelines.md](../../spec/19-api-guidelines.md) §13,
[01-authentication.md](../../spec/01-authentication.md) NFR-04.

Uses the in-framework `Microsoft.AspNetCore.RateLimiting` middleware — no new package.

- `Common/Infrastructure/RateLimiting/RateLimitingOptions.cs` — bound from the
  `RateLimiting` config section: `Enabled`, `AuthPermitLimit`, `AuthWindowSeconds`,
  `GlobalPermitLimit`, `GlobalWindowSeconds`. Thresholds live in configuration, exactly as
  §13 says ("an infrastructure configuration concern").
- `Common/Infrastructure/RateLimiting/RateLimiting.cs` — `AddJiraLiteRateLimiting(...)`:
  - **Global limiter** — fixed window partitioned by the authenticated user id
    (`ClaimsPrincipalExtensions`), falling back to the remote IP for anonymous callers.
    Only `/api/*` is limited; `/health` and `/hangfire` get `GetNoLimiter`.
  - **`"auth"` policy** — a tighter fixed window partitioned by remote IP, applied to
    `/api/auth/*` (Register, Login, Refresh, Logout) via `.RequireRateLimiting("auth")`.
  - **`OnRejected`** — 429 `application/problem+json` shaped by `ProblemResults`, plus a
    `Retry-After` header taken from the limiter's `RetryAfterMetadata`.
- Options are resolved **lazily from `HttpContext.RequestServices`** inside the partition
  factories, never from `builder.Configuration` — the same stale-config trap already
  documented in `Program.cs` for the connection string and the JWT signing key.
- `app.UseRateLimiter()` sits after `UseAuthentication()` so the user-id partition key is
  available, and before `UseAuthorization()`.
- Test factory sets `RateLimiting:Enabled=false` so the existing ~40 test classes (which
  register/login constantly) are unaffected; a dedicated factory turns it on with a permit
  limit of 3.

**Verification:** new `RateLimiting/RateLimitingTests.cs` — the 4th rapid login returns
429, the response is a Problem Details body, `Retry-After` is present, `/health` is never
limited, and a limited endpoint recovers after the window.

## Task 2 — T046: Index & concurrency verification

**Spec:** [18-database.md](../../spec/18-database.md) index/concurrency NFRs.

Spec §Notification declares one composite index `(RecipientUserId, IsRead, CreatedAtUtc)`;
the code currently has two narrower indexes. Align the configuration with the spec and add
migration `AlignNotificationIndexWithSpec`.

- `Persistence/IndexCoverageTests.cs` — asserts every composite index named in
  [18-database.md](../../spec/18-database.md) exists with the specced **column order**,
  read from `sys.indexes`/`sys.index_columns`: `Issue` ×5, `ActivityLogEntry` ×2,
  `Notification`, `RefreshToken`, plus the unique `User.Email` and `Invitation.Token`.
- `Persistence/QueryPlanTests.cs` — seeds ~5,000 Issues across two Projects by raw bulk
  insert, then captures the actual plan for the backlog query via `SET SHOWPLAN_XML ON`
  and asserts it seeks `IX_Issue_ProjectId_SprintId_Rank` rather than scanning.
- `Ranking/ConcurrentRankTests.cs` — fires N parallel `PATCH .../rank` and
  `PATCH .../move` requests carrying the same `RowVersion`; asserts exactly one 200 and
  the rest 409, and that the surviving row is internally consistent.

## Task 3 — T047: Production image + controlled migrations

**Spec:** [20-coding-guidelines.md](../../spec/20-coding-guidelines.md) §9.

Today migrations are applied by nothing at all outside the test factory. §9 requires
automatic application **only** in Development/Compose, and an explicit step elsewhere.

- `Program.cs`: `--migrate` CLI switch — applies migrations and exits, so a deployment job
  runs the *same image* as a one-shot migrator instead of shipping the EF tools.
- `Program.cs`: auto-migrate on startup gated on
  `Environment.IsDevelopment() && Database:AutoMigrate` (default `true` in Development,
  never elsewhere), with the same bounded retry the test factory uses. The test factory
  sets `Database:AutoMigrate=false` — it does its own migration with retry already.
- `src/Api/Dockerfile`: build off `Release` publish, run as the non-root `app` user, set
  `ASPNETCORE_ENVIRONMENT=Production` as the image default, add a container `HEALTHCHECK`
  against `/health`.
- `src/Api/.dockerignore`: keep `bin/`, `obj/`, `storage/` out of the build context.
- `docker-compose.prod.yml`: `sqlserver` → `migrator` (`command: ["--migrate"]`,
  `restart: "no"`) → `api` (`depends_on: migrator: service_completed_successfully`), with
  every secret sourced from the environment and no defaults.

**Verification:** `DeploymentConfigurationTests.cs` — asserts the app does not migrate on
startup when `AutoMigrate=false`/non-Development, and that `Database:AutoMigrate` is
honoured. The image build and Compose boot are verified by hand (recorded in the runbook).

## Task 4 — T048: Full regression pass

- `docs/acceptance-criteria-coverage.md` — a matrix mapping every feature document
  [01](../../spec/01-authentication.md)–[17](../../spec/17-admin.md) and its §15
  acceptance-criteria list to the integration test classes that cover it, with any
  deliberate gaps called out explicitly rather than left implied.
- Run the whole suite (`dotnet test`) and record the true result — pass count, failures,
  duration — in the coverage document and in the final report.

## Task 5 — T049: Deployment runbook

`docs/deployment-runbook.md` covering: prerequisites and required environment variables;
first-time deploy; routine deploy (build → push → migrate → release); how migrations are
applied and why they are not automatic; rollback of both app and schema; smoke checks
after release; and the known operational gaps (the Hangfire dashboard is still wired to
`AllowAllDashboardAuthorizationFilter`, HTTPS/TLS terminates upstream).

---

## Execution order

T045 → T046 → T047 → T049 → T048 last (the regression pass must run against the final
state of the code, not an intermediate one).

---

## Outcome

Executed in that order. Deviations from the plan as written, all in the direction of
doing more rather than less:

- **T046** added `Ranking/ConcurrentRankTests.cs` with three races rather than one, and
  `IndexCoverageTests.cs` grew to cover the unique `User.Email`/`Invitation.Token` indexes
  as well as the composites. The query-plan assertions passed on the first run — the
  optimizer does seek `IX_Issue_ProjectId_SprintId_Rank` and
  `IX_Issue_ProjectId_AssigneeUserId` at 10,000 rows.
- **T047** was verified by actually building the image and running
  `docker-compose.prod.yml` end to end, not only by the unit-level
  `DeploymentConfigurationTests`. That run found a real defect: the migrator container
  died on the `Jwt:SigningKey` fail-fast. Fixed by skipping that check on the `--migrate`
  path, since the migration step issues no tokens.
- **T048** turned out to be substantially larger than "write a matrix and run the suite".
  Four documents had no real coverage — 01 Authentication (nothing beyond incidental use
  in `TestDataHelper`), 02 Users, 03 Workspaces (acceptance/last-admin/leave), and 04
  Teams (nothing at all) — plus the delivery half of 13 Notifications and the
  cross-cutting bullets of 16 RBAC. Those tests were written as part of this task; see
  [docs/acceptance-criteria-coverage.md](../acceptance-criteria-coverage.md).
