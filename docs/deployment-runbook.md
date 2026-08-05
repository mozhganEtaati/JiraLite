# JiraLite — Deployment Runbook

Task **T049** ([spec/22-tasks.md](../spec/22-tasks.md) §9). Operational procedure for the stack
defined by [`docker-compose.prod.yml`](../docker-compose.prod.yml) and
[`src/Api/Dockerfile`](../src/Api/Dockerfile).

This document is the authority on *how* a release is performed. The rule it exists to enforce is
[spec/20-coding-guidelines.md](../spec/20-coding-guidelines.md) §9: **outside Development, the
application never applies its own migrations.** Everything below follows from that.

---

## 1. What gets deployed

One image, two roles:

| Role | Command | Restart policy |
|---|---|---|
| Migration step | `dotnet JiraLite.Api.dll --migrate` | `no` — runs once, must exit 0 |
| API + background jobs | `dotnet JiraLite.Api.dll` | `unless-stopped` |

`--migrate` applies pending migrations and exits without starting the HTTP server or the Hangfire
worker. Shipping one artifact for both roles means the schema is always moved by exactly the EF
model the new code was compiled against — there is no separate migration bundle to drift.

The API refuses to start if `Database__AutoMigrate` is `true` in any environment other than
`Development`. That is a hard failure, not a warning: an operator who set it believes migrations
are being applied, and booting anyway would run the app against an unknown schema.

## 2. Prerequisites

- Docker Engine 24+ with Compose v2 on the target host.
- A SQL Server instance. The Compose file runs one in-stack for a self-contained deployment; a
  managed instance works equally well — point `JIRALITE_CONNECTION_STRING` at it and delete the
  `sqlserver` service and both `depends_on: sqlserver` blocks.
- A reverse proxy terminating TLS in front of the API (see §8 — the app does not terminate TLS).
- An SMTP relay reachable from the host.

### Required environment

Compose fails immediately if any of these is unset — none has a default.

| Variable | Maps to | Notes |
|---|---|---|
| `JIRALITE_IMAGE` | image tag for both services | Use an immutable tag (a commit SHA), never `latest` — rollback depends on it |
| `JIRALITE_CONNECTION_STRING` | `ConnectionStrings__Default` | Used by EF **and** Hangfire's job storage |
| `JIRALITE_JWT_SIGNING_KEY` | `Jwt__SigningKey` | ≥ 32 bytes. Rotating it invalidates every issued access token |
| `JIRALITE_SA_PASSWORD` | `MSSQL_SA_PASSWORD` | Only when running the in-stack SQL Server |
| `JIRALITE_PUBLIC_BASE_URL` | `FileStorage__PublicBaseUrl` | Public origin; avatar URLs are built from it |
| `JIRALITE_SMTP_HOST` | `Email__SmtpHost` | |
| `JIRALITE_SMTP_FROM` | `Email__FromAddress` | |
| `JIRALITE_RESET_URL_TEMPLATE` | `PasswordReset__ResetUrlTemplate` | Optional. Web origin + `{token}`, e.g. `https://app.example.com/reset-password?token={token}`. Unset, reset emails carry the bare token instead of a link |

Optional, with defaults: `JIRALITE_PORT` (8080), `JIRALITE_JWT_ISSUER`/`JIRALITE_JWT_AUDIENCE`
(`JiraLite`), `JIRALITE_SMTP_PORT` (587), `JIRALITE_SMTP_ENABLE_SSL` (true),
`JIRALITE_SMTP_USERNAME`/`JIRALITE_SMTP_PASSWORD` (empty = unauthenticated relay).

Rate limiting (`RateLimiting__*`, defaults 300 req/min per user and 10 req/min per IP on
`/api/auth/*`) and invitation expiry (`Invitations__ExpiryDays`, 7) are left at their
`appsettings.json` values unless a deployment overrides them.

`Mcp__Enabled` (default `false`) controls the MCP surface — the `/mcp` endpoint and the personal
access token endpoints under `/api/users/me/tokens`. With it unset, none of those routes exists and
the deployment behaves exactly as it did before Phase 8. Turning it on lets users mint long-lived
credentials that an AI client can use to read and write issues as them; see
[mcp-client-setup.md](mcp-client-setup.md), and the operational note in §8.

Keep the variables in a root-owned `.env` beside the compose file, mode `600`. It is **not** in
version control and must not be.

## 3. Build and publish

```bash
TAG=$(git rev-parse --short HEAD)
docker build -t registry.example.com/jiralite-api:$TAG ./src/Api
docker push registry.example.com/jiralite-api:$TAG
```

The build context is `src/Api`, not the repository root; `src/Api/.dockerignore` keeps `bin/`,
`obj/`, local `storage/`, and `appsettings.Development.json` (which carries the dev signing key)
out of the image.

Verify what you built before it goes anywhere:

```bash
docker run --rm --entrypoint id registry.example.com/jiralite-api:$TAG   # expect uid=1654
```

## 4. First-time deploy

```bash
cd /opt/jiralite                       # holds docker-compose.prod.yml and .env
export JIRALITE_IMAGE=registry.example.com/jiralite-api:$TAG

docker compose -f docker-compose.prod.yml config >/dev/null   # fails loudly on a missing secret
docker compose -f docker-compose.prod.yml up -d sqlserver
docker compose -f docker-compose.prod.yml up --exit-code-from migrator migrator
docker compose -f docker-compose.prod.yml up -d api
```

The migrator creates the schema from scratch on an empty database — there is no separate "create
database" step beyond SQL Server having the database, which the connection string's `Database=`
name causes EF to create on first migration.

Then run the smoke checks in §7.

## 5. Routine deploy

```bash
cd /opt/jiralite
export JIRALITE_IMAGE=registry.example.com/jiralite-api:$NEW_TAG

# 1. Pull first, so the migration step is not waiting on a registry round trip.
docker compose -f docker-compose.prod.yml pull

# 2. Migrate. This is the step that is allowed to fail; nothing has changed yet if it does.
docker compose -f docker-compose.prod.yml up --exit-code-from migrator migrator

# 3. Release. The API only starts because the migrator exited 0.
docker compose -f docker-compose.prod.yml up -d api
```

If step 2 exits non-zero, **stop**. The old container is still serving the old schema, which is
still intact. Read `docker compose -f docker-compose.prod.yml logs migrator`, fix forward, and
start again from step 1. Do not run step 3 to "see if it works".

Because the API is gated on `service_completed_successfully`, running `up -d` for the whole stack
in one command is also safe — it just gives you less control over where a failure stops.

### Writing migrations that can be deployed this way

There is a window between the migration completing and the new containers taking over during
which the **old code is running against the new schema**. Migrations must therefore be backward
compatible for that window: add columns as nullable or with a default, and split a rename or a
`NOT NULL` tightening across two releases (add → backfill and deploy code → drop in the next
release). This is a review obligation on the pull request, in addition to the constraint review
[spec/20-coding-guidelines.md](../spec/20-coding-guidelines.md) §9 already requires.

## 6. Rollback

**Application rollback** is a redeploy of the previous tag:

```bash
export JIRALITE_IMAGE=registry.example.com/jiralite-api:$PREVIOUS_TAG
docker compose -f docker-compose.prod.yml up -d api
```

Skip the migrator. Rolling the app back does **not** roll the schema back, and for a backward
compatible migration (§5) it does not need to — the old code runs fine against the newer schema.
This is the normal case and should be the only one you ever perform under time pressure.

**Schema rollback** is a separate, deliberate act and is not automated. EF's `Migrate` only moves
forward; reverting requires targeting the previous migration explicitly, from a machine with the
EF tools and the repository at the *newer* commit (the down-migrations only exist there):

```bash
dotnet ef database update <PreviousMigrationName> \
    --project src/Api --connection "$JIRALITE_CONNECTION_STRING"
```

Before doing this: stop the API (`docker compose -f docker-compose.prod.yml stop api`), take a
backup (below), and confirm the down-migration is not destructive — a reverted `AddColumn` drops
the column and everything in it.

**Backup before any schema change:**

```bash
docker compose -f docker-compose.prod.yml exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$JIRALITE_SA_PASSWORD" -C \
    -Q "BACKUP DATABASE JiraLite TO DISK='/var/opt/mssql/backup/JiraLite-$(date +%F-%H%M).bak' WITH INIT, COMPRESSION"
```

The `sqlserver-data` volume holds `/var/opt/mssql`, so the backup survives a container replacement.
Copy it off the host as well — a lost volume loses both the database and its backups.

Uploaded files live in the `file-storage` volume and are not covered by a database backup. Back it
up on the same schedule; a database restored to a point where an Attachment row exists but its file
does not will 404 on download.

## 7. Post-release smoke checks

```bash
curl -fsS https://jiralite.example.com/health                    # 200 "Healthy"
docker compose -f docker-compose.prod.yml ps                     # api healthy, migrator exited 0
docker compose -f docker-compose.prod.yml logs --tail=100 api    # no startup exceptions
```

Then, against the deployed origin:

1. `POST /api/auth/login` with a known account returns 200 and an access token.
2. `GET /api/users/me` with that token returns the profile — proves JWT validation is using the
   deployed signing key.
3. `GET /api/dashboard/my-tasks` returns 200 — proves the database is reachable and migrated.
4. Repeat `POST /api/auth/login` with bad credentials ~12 times: the later attempts return **429**
   with a `Retry-After` header. This is the only external confirmation that rate limiting is on.
5. `GET /hangfire` renders and shows the recurring jobs registered and no failed jobs.

If step 3 returns 500 with a schema error, the migration step did not run against the database the
API is pointed at — check that both services received the same `JIRALITE_CONNECTION_STRING`.

## 8. Known operational gaps

These are real and unresolved as of Phase 7. They are listed here because an operator will hit
them, not as a to-do list.

- **The Hangfire dashboard is unauthenticated.** `Program.cs` still wires
  `AllowAllDashboardAuthorizationFilter`, carried over from Phase 0 when no user system existed.
  Anyone who can reach `/hangfire` can trigger and delete jobs. Until it is replaced with a real
  filter, **block `/hangfire` at the reverse proxy** and reach it through an SSH tunnel.
- **TLS terminates upstream.** The API listens on plain HTTP:8080 and has no certificate.
  `UseHttpsRedirection` is registered but cannot determine an HTTPS port in the container, so it
  passes requests through rather than redirecting. The proxy must terminate TLS and must not
  forward plain HTTP from the internet.
- **File storage is a local volume.** `LocalDiskFileStorage` writes to `/app/storage` inside the
  container. This does not survive moving the stack to another host and cannot be shared by two
  API replicas — the stack is single-instance until that is replaced with object storage.
- **No zero-downtime path.** `up -d api` replaces the container; there is a short outage. Combined
  with the single-volume file storage above, running two replicas behind a load balancer is not
  currently supported.
- **Personal access tokens have no operator-side kill switch.** A user revokes their own token via
  `DELETE /api/users/me/tokens/{tokenId}`; there is no admin endpoint to revoke someone else's. If
  a token is known to be compromised and its owner is unavailable, the options are deactivating
  that user (which stops all of their tokens at once), setting `Mcp__Enabled=false` and
  redeploying (which takes the whole surface down), or a manual `UPDATE` against
  `PersonalAccessToken`. Worth deciding which of those is acceptable **before** enabling MCP.
- **The SA account is used for the application connection.** The in-stack SQL Server is reachable
  only from the Compose network, but a dedicated least-privilege login is the correct fix and does
  not exist yet.

## 9. Related documents

- [spec/20-coding-guidelines.md](../spec/20-coding-guidelines.md) §9 — the migration rule this
  runbook implements
- [spec/21-roadmap.md](../spec/21-roadmap.md) §9 — Phase 7 scope
- [docs/plans/2026-08-01-phase-7-hardening.md](plans/2026-08-01-phase-7-hardening.md) — the plan
  these deployment artifacts came from
- [spec/23-mcp-server.md](../spec/23-mcp-server.md) — the MCP surface `Mcp__Enabled` controls
- [docs/mcp-client-setup.md](mcp-client-setup.md) — what a user does once it is enabled
