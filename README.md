# JiraLite

**A lightweight project management platform for small teams** — projects, boards, sprints, and issues, with the familiar Jira/Linear concepts and none of the enterprise configurability.

A .NET 10 vertical-slice API, a Next.js 16 web client, and an MCP server that lets AI assistants read and write your backlog.

```
Organization → Workspace → Project → Board → Column
                    ↓          ↓
                  Team      Sprint → Issue → Comment / Attachment / Label
```

## Quick start

Docker brings up SQL Server and the API, and applies migrations on startup:

```bash
docker compose up -d --build
cd web && npm install && npm run dev
```

| | |
|---|---|
| Web | http://localhost:3000 |
| API · Swagger | http://localhost:8080 · `/swagger` |
| Hangfire dashboard | http://localhost:8080/hangfire |

The web app proxies `/api` through a Next rewrite, so the browser never crosses an origin and the API needs no CORS setup.

> [!WARNING]
> The Hangfire dashboard is currently unauthenticated — lock it down before any non-local deployment.

## What's in it

**Auth** — register, login, rotating refresh tokens with reuse detection, password reset by emailed one-time link.
**Workspaces** — organizations, workspaces, members, email invitations, teams.
**Planning** — projects, Scrum/Kanban boards with custom columns, sprints, product and sprint backlogs with drag-and-drop LexoRank ordering.
**Work** — five issue types with parent/child hierarchy, comments, attachments, labels, and a blocked state with its reason and age.
**Reporting** — a sprint report reading progress against the calendar, blockers, team load, and a health verdict that shows its reasons.
**Everything else** — in-app and email notifications, dashboard, calendar, and admin views.

Deliberately **not** included: full-text search, time tracking, custom fields, workflow builders, issue links beyond parent/child. Those are documented non-goals, not gaps.

## MCP server

JiraLite speaks the [Model Context Protocol](https://modelcontextprotocol.io) at `/mcp` over Streamable HTTP, so an assistant can work your backlog directly — *"what's on my plate?"*, *"move JL-42 to In Review and comment that it's ready"* — without anyone writing glue code against the REST API.

**11 tools.** Seven read, four write:

| | |
|---|---|
| **Read** | `list_my_issues` · `list_projects` · `list_issues` · `get_issue` · `list_board` · `list_sprints` · `list_comments` |
| **Write** | `create_issue` · `update_issue` · `move_issue` · `add_comment` |

Write tools produce **exactly** the same domain effects as their HTTP counterparts — activity log entries, notification triggers, validation, the lot. Tool handlers add no logic of their own; a tool needing behaviour its slice lacks means the slice is incomplete, not that the tool should compensate.

### The safety model

This is the part worth reading before you point an agent at your issue tracker.

- **No new authority.** A token's holder can do precisely what its owning user could do through `/api`, at the role they hold *at invocation time*. Roles are evaluated fresh on every call and never cached in the token.
- **No destructive operations, by design.** There is no delete tool of any kind — no member or role management, no project or workspace administration, no attachment access. Those stay HTTP-only, where a human is unambiguously in the loop. That's a deliberate blast-radius limit, not a missing feature.
- **Credentials don't cross over.** MCP clients authenticate with a Personal Access Token, never a JWT — and `/api/*` rejects PATs just as `/mcp` rejects JWTs. A leaked long-lived token cannot be replayed against the full API surface.
- **Tokens are constrained.** Hashed at rest, mandatory expiry of at most 365 days, 10 active per user, revocation immediate and irreversible. Deactivating a user kills all of theirs instantly, with no separate revocation step.
- **Every invocation is logged** with tool name, caller, and which token was used — enough to reconstruct who did what through which client.
- **Off by default.** With `Mcp:Enabled` false there is no `/mcp` and no way to mint a token for it; both 404 rather than existing and refusing.

### Connecting a client

The Compose stack already enables it. Mint a token, then point your client at `http://localhost:8080/mcp`:

```bash
curl -X POST http://localhost:8080/api/users/me/tokens \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"claude-desktop","expiresInDays":90}'
```

The plaintext value comes back **once, at creation**, and is never retrievable afterwards. Per-client configuration: [`docs/mcp-client-setup.md`](docs/mcp-client-setup.md). Full contract: [`spec/23-mcp-server.md`](spec/23-mcp-server.md).

## Design bets

- **Four roles, not a permission matrix.** Admin, Project Admin, Developer, Viewer — defined in code.
- **Columns *are* status.** No separate global status enum to keep in sync with the board.
- **One `Issue` entity.** Epic → Story/Task/Bug → Subtask is a self-reference plus a `Type` discriminator, not five tables.
- **A modular monolith.** No microservices, no event bus, no event sourcing, no repository layer over EF Core.

## Architecture

Vertical Slice Architecture — organized by feature, not by technical layer. There is no top-level `Controllers/`, `Services/`, or `Repositories/`. Each use case is one file owning its request, validation, handler, and response:

```
src/Api/Features/Auth/Login.cs      →  Request · Response · Validator · Handler · MapEndpoint
```

A slice never references another slice's types; shared needs go through `Common/Domain` and the shared `DbContext`. Validators check *shape*, handlers check *state*. Errors are RFC 7807 Problem Details. Authorization goes through named policies, never inline role checks. Tokens are stored as SHA-256 hashes, so a database read alone yields nothing usable.

```
src/Api/Features/     one folder per context, one file per use case
src/Api/Common/       domain, auth, behaviors, infrastructure
web/app/              (auth) and (app) route groups
tests/                integration tests against real SQL Server
spec/                 24 numbered documents — the source of truth
```

## Testing

Integration tests run against a real SQL Server in a Testcontainers container — no in-memory provider, no mocked `DbContext`. **Docker must be running.**

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~PasswordReset"
```

## Migrations

EF Core code-first; the entity classes are the source of truth for the schema.

```bash
dotnet tool restore
dotnet ef migrations add YourMigrationName \
  --project src/Api --startup-project src/Api \
  --output-dir Common/Infrastructure/Persistence/Migrations
```

Production applies them as a separate step (`dotnet JiraLite.Api.dll --migrate`), so a failed migration stops the rollout before the new image serves a request. Outside Development the app refuses to start with auto-migration enabled.

## Documentation

[`spec/`](spec/) is the single source of truth — domain model, API, database, and engineering conventions, numbered in dependency order. Start with [00 — Project Overview](spec/00-project-overview.md) and [21 — Roadmap](spec/21-roadmap.md).

Before contributing, read [20 — Coding Guidelines](spec/20-coding-guidelines.md). Add integration tests for the acceptance criteria you touch, record them in [`docs/acceptance-criteria-coverage.md`](docs/acceptance-criteria-coverage.md), and update the owning spec document when behaviour changes — the spec is authoritative, not written after the fact.

Operational guides live in [`docs/`](docs/), including the [deployment runbook](docs/deployment-runbook.md) and its full environment variable table.
