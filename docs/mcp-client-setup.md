# MCP Client Setup

How to connect an MCP client — Claude Code, Claude Desktop, VS Code, or any other — to a JiraLite
deployment. The behaviour described here is specified in
[spec/23-mcp-server.md](../spec/23-mcp-server.md).

## 1. Check the server has MCP enabled

The MCP surface is off by default. A deployment turns it on with:

```
Mcp__Enabled=true
```

With the flag off, `/mcp` and the token endpoints return 404 — they are not mapped at all. If you
get a 404 while following this guide, that is the first thing to check.

## 2. Issue a personal access token

MCP clients cannot perform the short-lived access/refresh exchange the web API uses, so they
authenticate with a **Personal Access Token** instead. Create one with your normal access token:

```http
POST /api/users/me/tokens
Authorization: Bearer {accessToken}
Content-Type: application/json

{ "name": "Work laptop — Claude Code", "expiresInDays": 90 }
```

The response contains the plaintext value **once**:

```json
{
  "id": "9a8b7c6d-...",
  "name": "Work laptop — Claude Code",
  "token": "jlp_7f3c2a91e5b84d06a2f1c8e37b95d420",
  "expiresAtUtc": "2026-11-03T00:00:00Z",
  "createdAtUtc": "2026-08-05T09:12:00Z"
}
```

It is stored hashed and cannot be retrieved again. If you lose it, revoke it and create another.

Constraints worth knowing before you script this:

- Lifetime is 1–365 days. There is no non-expiring token.
- You may hold 10 active tokens. The 11th request is rejected with 409 rather than evicting one.
- The token authenticates `/mcp` only. Sent to `/api/*` it returns 401, and your `/api` access
  token sent to `/mcp` returns 401 in the same way.

## 3. Configure the client

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

The transport is Streamable HTTP, stateless — each tool call is its own authenticated request, so
there is no session to keep alive or resume.

## 4. What the client can do

Read tools: `list_my_issues`, `list_projects`, `list_issues`, `get_issue`, `list_board`,
`list_sprints`, `list_comments`.

Write tools: `create_issue`, `update_issue`, `move_issue`, `add_comment`.

Two things follow from how these are built, and are worth telling users up front:

- **Your role still decides everything.** Tools run the same authorization as the HTTP API, resolved
  at the moment of the call. A Viewer can read and cannot write. Being demoted takes effect
  immediately, on tokens issued before the demotion.
- **Nothing destructive is exposed.** There is no delete tool of any kind, and no project, board,
  label, sprint-lifecycle, membership, or admin tool. Those stay in the HTTP API on purpose.

`move_issue` requires the issue's `rowVersion`, which `get_issue` returns. If the move is refused
for a version mismatch, someone changed the issue in the meantime — re-read it and decide again
rather than retrying with the same value.

## 5. If a token is compromised

Revoke it:

```http
DELETE /api/users/me/tokens/{tokenId}
Authorization: Bearer {accessToken}
```

Revocation takes effect on the next request and cannot be undone — create a new token rather than
trying to restore the old one. `GET /api/users/me/tokens` lists your tokens with `lastUsedAtUtc`,
which is the fastest way to spot one you no longer recognise or no longer need.

Deactivating your account stops all of your tokens working at once, without revoking them
individually.
