# 19 — API Guidelines

## 1. Overview

This document is the single source of truth for API-wide conventions — naming, routing, pagination, filtering, validation, and error handling. Every endpoint defined in [01](01-authentication.md)–[17](17-admin.md) must conform to it. Where an earlier feature document's illustrative request example conflicts with this document (see §5 pagination), this document wins.

## 2. Base URL & Versioning

- Root: `/api`
- No API versioning scheme in V1 — a single, unversioned route root ([00-project-overview.md](00-project-overview.md) Non-Goals). Introducing versioning (e.g., `/api/v1`) is a Future Improvement, not a default to design around prematurely.

## 3. Resource Naming

Resource names are plural nouns matching the canonical entity name exactly — no synonyms.

| Canonical resource | Route segment | Rejected alternatives |
|---|---|---|
| Issue | `/issues` | `/tickets`, `/items` |
| BoardColumn (as status) | (embedded in Board response) | separate `/statuses` endpoint |
| Sprint | `/sprints` | `/iterations`, `/cycles` |
| Workspace | `/workspaces` | `/spaces` |
| Organization | `/organizations` | `/accounts`, `/tenants` |
| Team | `/teams` | `/squads`, `/groups` |
| Estimate (field) | `estimate` | `points`, `storyPoints` |
| Rank (field) | `rank` | `order`, `position` |
| Role values | `Admin`, `ProjectAdmin`, `Developer`, `Viewer`, `Member` exactly | `Owner`, `Contributor`, `Guest` |

Any new endpoint introduced beyond [01](01-authentication.md)–[17](17-admin.md) must reuse these exact terms — do not introduce a new synonym for an existing concept.

## 4. Routing Conventions

- Nested routes reflect aggregate/ownership structure from [18-database.md](18-database.md) §8, not arbitrary convenience.
  - Valid: `/projects/{projectId}/issues` (Project owns Issues), `/issues/{issueId}/comments` (Issue owns Comments).
  - Invalid: skipping a level, e.g. there is no `/workspaces/{id}/issues` — an Issue's direct parent is Project, not Workspace.
- A resource accessed by its own globally unique Id does not repeat its ancestor's Id in the route: `/issues/{issueId}`, not `/projects/{projectId}/issues/{issueId}`, once the Id is known (list/create endpoints remain nested; get/update/delete-by-id endpoints are flat).
- Actions that are not pure CRUD (state transitions, non-idempotent operations) are modeled as a sub-resource verb on the entity: `POST /sprints/{sprintId}/start`, `POST /sprints/{sprintId}/complete`, `PATCH /issues/{issueId}/move`, `PATCH /issues/{issueId}/rank`.

## 5. Pagination

All list endpoints use **cursor-based pagination**. The parameters below are canonical and every feature document's examples ([07-backlog.md](07-backlog.md), [14-dashboard.md](14-dashboard.md), [17-admin.md](17-admin.md) included) use them consistently.

**Request query parameters**

| Parameter | Type | Notes |
|---|---|---|
| `limit` | int | Optional, default 25, max 100 |
| `cursor` | string | Optional, opaque token from a prior response's `nextCursor`; omit for the first page |

**Response envelope**

```json
{
  "items": [ ... ],
  "pageInfo": {
    "hasNextPage": true,
    "nextCursor": "eyJvZmZzZXQiOjI1fQ=="
  }
}
```

`nextCursor` is an opaque, server-generated token. Clients must not construct or parse it — only pass it back verbatim.

## 6. Filtering & Sorting

- Filter query parameters match entity field names exactly, in `camelCase` (e.g., `status`, `assigneeId`, `priority`, `type`, `labelId`) — no endpoint-specific aliases.
- Multiple values for the same filter are comma-separated and OR'd: `?priority=High,Critical`.
- Sorting uses a single `sort` parameter: a bare field name for ascending, prefixed with `-` for descending (e.g., `?sort=-createdAtUtc`). Only fields explicitly documented as sortable in a feature document are accepted; others return 400.

## 7. Request/Response Conventions

- JSON property casing: `camelCase` throughout (matches ASP.NET Core's default `System.Text.Json` behavior).
- Timestamps: ISO 8601 UTC with a `Z` suffix, e.g. `2026-07-31T10:00:00Z`.
- Date-only fields (e.g., `dueDateUtc`, `plannedStartDateUtc`): `YYYY-MM-DD`.
- Nested references to other entities (assignee, reporter, author) are always a minimal summary object — `{ id, displayName, avatarUrl }` for a User — never the full entity, to avoid over-fetching. Full entity detail requires calling that entity's own `GET` endpoint.

## 8. Validation

- All request validation happens in a single shared validation pipeline behavior ([20-coding-guidelines.md](20-coding-guidelines.md)), not ad hoc per-handler checks.
- A validation failure returns `400 Bad Request` with the Problem Details shape in §9, `errors` keyed by `camelCase` field name.

## 9. Error Handling

All error responses use **RFC 7807 Problem Details**, applied globally via exception-to-response middleware — no feature returns a bespoke error shape.

```json
{
  "type": "https://jiralite.dev/errors/validation-failed",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "errors": {
    "title": ["Title is required."]
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

For non-validation errors, `errors` is omitted and `detail` carries a human-readable explanation. `traceId` always correlates to the Serilog structured log entry for that request ([20-coding-guidelines.md](20-coding-guidelines.md)).

## 10. Status Code Usage

| Code | Meaning | Used for |
|---|---|---|
| 200 | OK | Successful GET/PATCH returning a body |
| 201 | Created | Successful POST creating a resource |
| 204 | No Content | Successful action with no response body (logout, delete) |
| 400 | Bad Request | Validation failure, malformed input |
| 401 | Unauthorized | Missing/invalid/expired credentials |
| 403 | Forbidden | Authenticated but not authorized for the action ([16-rbac.md](16-rbac.md)) |
| 404 | Not Found | Resource doesn't exist, or exists but the caller has no visibility into it (never used to leak existence — see [13-notifications.md](13-notifications.md) §13) |
| 409 | Conflict | Business-rule conflict (duplicate, invalid state transition, last-admin removal, archived write-lock) |
| 410 | Gone | Resource existed but is now permanently resolved (expired/consumed Invitation) |
| 413 | Payload Too Large | File upload exceeds size limit |
| 415 | Unsupported Media Type | Disallowed file content type |
| 500 | Internal Server Error | Unhandled exception; never exposes internal details in `detail` |

## 11. Concurrency

Endpoints that mutate frequently-reordered or frequently-contested state (`PATCH /issues/{issueId}/move`, `PATCH /issues/{issueId}/rank`, `PATCH /boards/{boardId}/columns/reorder`) require the current `rowVersion` (base64-encoded `rowversion`/`timestamp`) in the request body. A mismatch returns `409 Conflict`. The updated `rowVersion` is always returned in the response so the client can chain further edits.

## 12. Authentication & Authorization

- Every authenticated request carries `Authorization: Bearer {accessToken}` ([01-authentication.md](01-authentication.md)).
- Authorization is evaluated per-request via named policies, never inline role checks — see [16-rbac.md](16-rbac.md) BR-01, BR-02.

## 13. Rate Limiting

- Authentication endpoints (`/api/auth/*`) are rate-limited per [01-authentication.md](01-authentication.md) NFR-04.
- All other endpoints have a baseline per-user rate limit to prevent abuse; specific thresholds are an infrastructure configuration concern documented in [20-coding-guidelines.md](20-coding-guidelines.md), not enumerated per-endpoint here.

## 14. Related Documents

- [16-rbac.md](16-rbac.md) — authorization policy definitions referenced by every endpoint's Auth/Role column
- [18-database.md](18-database.md) — entity/field source of truth these conventions expose
- [20-coding-guidelines.md](20-coding-guidelines.md) — where these conventions are implemented (pipeline behaviors, middleware)
