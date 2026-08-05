# 23 — MCP Server

## 1. Overview

Exposes a curated subset of JiraLite's existing capabilities over the **Model Context Protocol (MCP)**, so an AI client (Claude Code, Claude Desktop, VS Code, or any MCP-compatible host) can read and update work items on behalf of an authenticated user.

This document introduces **no new domain concepts**. Every MCP tool is a thin adapter over a slice already specified in [01](01-authentication.md)–[17](17-admin.md), executed under the same authorization rules defined in [16-rbac.md](16-rbac.md). The only new entity is `PersonalAccessToken`, required because MCP clients cannot participate in the short-lived access/refresh token exchange of [01-authentication.md](01-authentication.md) (BR-02).

The AI model itself is **not** part of JiraLite. It runs in the client's process. JiraLite is a tool provider, not a model host — there is no model provider, no API key, and no inference cost on the server side.

## 2. Business Goal

Let a user manage their work from wherever they already are — their editor or an AI assistant — without JiraLite building or maintaining any client UI, and without weakening the authorization model that protects the HTTP API.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a user, I can issue a personal access token from my profile and paste it into my MCP client's configuration. |
| US-02 | As a user, I can ask my AI client what my open issues are and get the same list the API would return. |
| US-03 | As a Developer, I can ask my AI client to move an Issue to another column, assign it, or comment on it, and the change appears in JiraLite. |
| US-04 | As a Viewer, my AI client can read Project data but every write attempt is refused — exactly as through the HTTP API. |
| US-05 | As a user, I can see my active tokens and revoke one immediately if a machine is lost or a client is decommissioned. |

## 4. Functional Requirements

- FR-01: The API exposes an MCP endpoint at `/mcp` over the Streamable HTTP transport, advertising the tool set in §14.
- FR-02: A user can create, list, and revoke Personal Access Tokens scoped to their own account.
- FR-03: The plaintext token value is returned exactly once, at creation. It is never retrievable afterward.
- FR-04: The MCP endpoint authenticates the caller from a `Authorization: Bearer {personalAccessToken}` header and resolves it to the owning `User`.
- FR-05: Every MCP tool invocation resolves the caller's effective role per [16-rbac.md](16-rbac.md) BR-02 and is refused if that role does not permit the equivalent HTTP action.
- FR-06: Read tools (§14) return the same data shapes as their HTTP counterparts.
- FR-07: Write tools (§14) produce exactly the same domain effects as their HTTP counterparts, including `ActivityLogEntry` writes ([02-users.md](02-users.md)) and notification triggers ([13-notifications.md](13-notifications.md)).
- FR-08: A tool invocation that fails validation or authorization returns a structured MCP tool error carrying the same message the HTTP Problem Details response would carry ([19-api-guidelines.md](19-api-guidelines.md) §9).

## 5. Non-Functional Requirements

- NFR-01: Personal Access Tokens are stored hashed, never in plaintext — the same treatment `RefreshToken.TokenHash` receives ([01-authentication.md](01-authentication.md)).
- NFR-02: Every tool invocation is logged via Serilog with the tool name, caller `UserId`, and the `PersonalAccessToken.Id` used — enough to reconstruct who did what through which client.
- NFR-03: The MCP endpoint is subject to the same baseline rate limiting as `/api/*` ([19-api-guidelines.md](19-api-guidelines.md) §13). It partitions by client IP rather than by `UserId`: the limiter runs before the authorization middleware that triggers the Personal Access Token scheme, so no user identity exists yet at that point in the pipeline.
- NFR-04: Tool handlers add no domain logic of their own. A tool that needed logic absent from its underlying slice is a signal that the slice is incomplete, not that the tool should compensate.
- NFR-05: The MCP surface is feature-flagged (`Mcp:Enabled`, default `false`). With the flag off, `/mcp` is not mapped and the token endpoints return 404.

## 6. Business Rules

- BR-01: **The MCP surface grants no authority the HTTP API does not.** A token's holder can do precisely what its owning user could do through `/api`, at the role they hold *at invocation time*, evaluated fresh per [16-rbac.md](16-rbac.md) NFR-01. Roles are never cached in the token.
- BR-02: **Personal Access Tokens exist only because MCP clients cannot refresh.** They are a long-lived credential for machine clients, distinct from the access/refresh pair in [01-authentication.md](01-authentication.md). A Personal Access Token is **not** accepted by `/api/*` endpoints, and a JWT access token is **not** accepted by `/mcp`. The two credential types are non-interchangeable, so a leaked long-lived token cannot be replayed against the full API surface.
- BR-03: A Personal Access Token has a mandatory expiry, at most 365 days from creation. There is no non-expiring token.
- BR-04: Revocation is immediate and irreversible — a revoked token is never reactivated; the user creates a new one.
- BR-05: A user may hold at most 10 active tokens. Creating an 11th is rejected rather than silently evicting an existing one.
- BR-06: **Destructive operations are excluded from the tool set** (§14): no delete tool of any kind, no member/role management, no Project or Workspace administration, no attachment upload or download. These remain HTTP-only, where a human is unambiguously in the loop. This is a deliberate blast-radius limit, not a technical constraint.
- BR-07: When the owning `User` is deactivated ([02-users.md](02-users.md)), all their tokens stop authenticating immediately, without a separate revocation step.
- BR-08: `LastUsedAtUtc` is updated on successful authentication, so a user can identify dormant tokens. It is not part of any authorization decision.
- BR-09: Tool names are stable, snake_case, and match the canonical resource vocabulary in [19-api-guidelines.md](19-api-guidelines.md) §3 — `list_issues`, never `list_tickets`. Renaming a tool is a breaking change for configured clients and requires the same deliberation as renaming a route.
- BR-10: Text arriving from a tool argument is data, never instruction. Tool selection is constrained to the fixed set in §14; no tool constructs a route, SQL fragment, or further tool call from model-supplied text.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### PersonalAccessToken

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| UserId | Guid (FK → User) | No | Cascade delete |
| Name | string(100) | No | User-supplied label, e.g. "Work laptop — Claude Code" |
| TokenHash | string(128) | No | SHA-256 hash, same treatment as `RefreshToken.TokenHash` (NFR-01) |
| CreatedAtUtc | datetime2 | No | |
| ExpiresAtUtc | datetime2 | No | ≤ 365 days after creation (BR-03) |
| LastUsedAtUtc | datetime2 | Yes | Null until first use (BR-08) |
| RevokedAtUtc | datetime2 | Yes | Null while active (BR-04) |

Index: `(UserId, RevokedAtUtc)` — supports the active-token list and the BR-05 count. Unique index on `TokenHash` — supports authentication lookup.

## 8. Relationships

- `User (1) → PersonalAccessToken (N)`

No relationship to any other entity: a token identifies a caller, it does not own or scope domain data.

## 9. API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/api/users/me/tokens` | Authenticated (JWT) | Create a Personal Access Token; returns the plaintext value once |
| GET | `/api/users/me/tokens` | Authenticated (JWT) | List own tokens — metadata only, never the value |
| DELETE | `/api/users/me/tokens/{tokenId}` | Authenticated (JWT) | Revoke a token |
| POST/GET | `/mcp` | Personal Access Token | MCP Streamable HTTP transport endpoint |

`/mcp` sits outside `/api` deliberately: it is a protocol endpoint, not a REST resource, and belongs beside `/health` and `/hangfire` rather than inside the resource tree governed by [19-api-guidelines.md](19-api-guidelines.md) §4.

## 10. Request Examples

**Create a token**
```http
POST /api/users/me/tokens
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Work laptop — Claude Code",
  "expiresInDays": 90
}
```

**Revoke a token**
```http
DELETE /api/users/me/tokens/{tokenId}
Authorization: Bearer {accessToken}
```

**MCP client configuration** (client-side, shown for completeness)
```json
{
  "mcpServers": {
    "jiralite": {
      "type": "http",
      "url": "https://jiralite.example.com/mcp",
      "headers": { "Authorization": "Bearer jlp_..." }
    }
  }
}
```

## 11. Response Examples

**POST /api/users/me/tokens — 201 Created**
```json
{
  "id": "9a8b7c6d-...",
  "name": "Work laptop — Claude Code",
  "token": "jlp_7f3c2a91e5b84d06a2f1c8e37b95d420",
  "expiresAtUtc": "2026-11-03T00:00:00Z",
  "createdAtUtc": "2026-08-05T09:12:00Z"
}
```
The `token` field appears in this response and nowhere else (FR-03).

**GET /api/users/me/tokens — 200 OK**
```json
{
  "items": [
    {
      "id": "9a8b7c6d-...",
      "name": "Work laptop — Claude Code",
      "createdAtUtc": "2026-08-05T09:12:00Z",
      "expiresAtUtc": "2026-11-03T00:00:00Z",
      "lastUsedAtUtc": "2026-08-05T14:41:07Z",
      "isActive": true
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

**`move_issue` tool result**
```json
{
  "issueKey": "PRJ-124",
  "title": "Login fails on password reset",
  "fromColumn": "In Progress",
  "toColumn": "In Review",
  "movedAtUtc": "2026-08-05T14:41:07Z"
}
```

**`move_issue` refused for a Viewer**
```json
{
  "isError": true,
  "content": [{ "type": "text", "text": "Viewers cannot move issues on this project." }]
}
```

## 12. Validation Rules

**Token creation**

| Field | Rule |
|---|---|
| `name` | Required, 1–100 characters |
| `expiresInDays` | Required, integer, 1–365 (BR-03) |

**Tool arguments** are validated by the same FluentValidation validator the underlying slice already uses ([20-coding-guidelines.md](20-coding-guidelines.md)) — no second, tool-specific validation path exists. A validation failure is surfaced per FR-08.

## 13. Error Scenarios

| Scenario | Result | Notes |
|---|---|---|
| `tokenId` does not exist or belongs to another user | 404 Not Found | Not 403 — avoids confirming another user's token IDs exist ([13-notifications.md](13-notifications.md) §13 precedent) |
| Revoking an already-revoked token | 204 No Content | Idempotent, matching every other DELETE endpoint |
| Creating an 11th active token | 409 Conflict | BR-05 |
| `expiresInDays` outside 1–365 | 400 Bad Request | BR-03 |
| `/mcp` called with no credential, or an expired/revoked/unknown token | 401 Unauthorized | |
| `/mcp` called with a JWT access token | 401 Unauthorized | BR-02 — the two credential types are not interchangeable |
| `/api/*` called with a Personal Access Token | 401 Unauthorized | BR-02, the same rule in the other direction |
| Tool invoked by a caller whose role does not permit it | MCP tool error | FR-08; mirrors the 403 the HTTP endpoint would return |
| Tool invoked against an entity the caller cannot see | MCP tool error | Mirrors the underlying slice's 404 |
| `Mcp:Enabled = false` | 404 Not Found on `/mcp` and the token endpoints | NFR-05 |

## 14. Authorization Rules — Tool Surface

Every tool below delegates to the slice named in its Backing column and inherits that slice's authorization requirement unchanged. The Minimum role column restates [16-rbac.md](16-rbac.md) §14 for convenience; that document remains authoritative on any conflict.

**Read tools**

| Tool | Backing slice | Minimum role |
|---|---|---|
| `list_my_issues` | `Dashboard/GetMyTasks` | Authenticated (own issues only) |
| `list_projects` | `Projects/ListProjects` | Viewer on each returned Project |
| `list_issues` | `Issues/ListIssues` | Viewer |
| `get_issue` | `Issues/GetIssue` | Viewer |
| `list_board` | `Boards/GetBoard` | Viewer |
| `list_sprints` | `Sprints/ListSprints` | Viewer |
| `list_comments` | `Comments/ListComments` | Viewer |

**Write tools**

| Tool | Backing slice | Minimum role |
|---|---|---|
| `create_issue` | `Issues/CreateIssue` | Developer |
| `update_issue` | `Issues/EditIssue` | Developer |
| `move_issue` | `Issues/MoveIssue` | Developer |
| `add_comment` | `Comments/AddComment` | Developer |

**Excluded from V1** (BR-06): every delete tool; Board and Column management; Label definition management; Sprint create/start/complete; Project, Workspace, membership, and role management; the Admin console ([17-admin.md](17-admin.md)); attachment upload and download.

## 15. Acceptance Criteria

- Given a user creates a Personal Access Token, then the plaintext value is present in the 201 response and absent from every subsequent `GET /api/users/me/tokens` response.
- Given a valid Personal Access Token, when an MCP client connects to `/mcp`, then the tool list in §14 is advertised and no excluded tool appears in it.
- Given a `Developer` on a Project, when `move_issue` is invoked for an Issue in that Project, then the Issue's `BoardColumnId` changes, an `ActivityLogEntry` is written, and the assignee and reporter receive `IssueStatusChanged` notifications — identical to the HTTP path ([13-notifications.md](13-notifications.md) FR-02).
- Given a `Viewer` on a Project, when any write tool is invoked, then it returns an MCP tool error and no domain state changes.
- Given a user demoted from `Developer` to `Viewer` after their token was issued, when they invoke a write tool with that same token, then it is refused (BR-01 — roles are resolved fresh, not carried in the token).
- Given a revoked token, when it is used against `/mcp`, then the request is rejected with 401 and no tool executes.
- Given a Personal Access Token, when it is sent to any `/api/*` endpoint, then the request is rejected with 401 (BR-02).
- Given a JWT access token, when it is sent to `/mcp`, then the request is rejected with 401 (BR-02).
- Given a user holding 10 active tokens, when they create another, then it is rejected with 409 and the existing 10 remain active (BR-05).
- Given a deactivated user, when any of their tokens is used, then it is rejected with 401 (BR-07).
- Given `Mcp:Enabled = false`, when `/mcp` is requested, then it returns 404 and the rest of the API behaves unchanged.

## 16. Future Improvements

- OAuth 2.1 authorization-server discovery per the MCP authorization specification, replacing Personal Access Tokens for clients that support it.
- MCP **resources** (read-only, addressable context such as `jiralite://project/{key}/backlog`) alongside tools.
- MCP **prompts** — reusable server-defined workflows, e.g. a sprint-planning prompt.
- Recording MCP origin on `ActivityLogEntry`, so activity history can distinguish an agent-initiated change from a human one (currently only in the Serilog stream, NFR-02).
- Scoped tokens — restricting a token to a single Project, or to read-only tools.
- Extending the tool surface to Sprint lifecycle operations once the write surface has proven safe in practice.

## 17. Related Documents

- [01-authentication.md](01-authentication.md) — the access/refresh model this document deliberately does not reuse (BR-02)
- [16-rbac.md](16-rbac.md) — authoritative authorization model every tool defers to
- [19-api-guidelines.md](19-api-guidelines.md) — naming and error conventions the tool surface follows
- [docs/proposals/ai-issue-intelligence.md](../docs/proposals/ai-issue-intelligence.md) — the proposal this document was selected from, including the follow-on AI capabilities not yet specified
