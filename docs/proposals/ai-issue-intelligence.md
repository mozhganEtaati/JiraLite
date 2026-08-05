# Proposal — AI Issue Intelligence

**Status:** Draft / for discussion
**Date:** 2026-08-05
**Scope:** An optional AI capability layer over the existing JiraLite domain, built on `Microsoft.Extensions.AI`.

---

## 1. Why This, and Why It Fits

JiraLite already stores everything an AI feature needs, and none of it has to be invented:

| Existing asset | What it enables |
|---|---|
| `Issue.Title` / `Issue.Description` | Semantic similarity, duplicate detection, triage |
| `Issue.Estimate` on closed issues | Historical estimation grounded in the team's own data |
| `ActivityLogEntry` | Digests, "what changed", stale/at-risk detection |
| `Sprint` + closed issues | Velocity-aware sprint planning |
| Hangfire | Backfill and scheduled jobs with zero new infrastructure |
| Vertical slices with self-contained request/response contracts | Each slice maps almost mechanically onto an AI tool definition |

The important design point: **every suggestion is grounded in this workspace's own history**, not in the model's general knowledge. That is what separates this from a thin wrapper over a chat API.

## 2. Alignment With Existing Principles

This proposal is deliberately constrained by [spec/00-project-overview.md](../../spec/00-project-overview.md) §5:

- **No speculative abstraction.** `IChatClient` / `IEmbeddingGenerator` from `Microsoft.Extensions.AI` *are* the abstraction — no hand-rolled `IAiService` wrapper on top.
- **One feature, one slice.** Each capability is a slice under `Features/Intelligence/`, not a shared "AI service" layer.
- **Simplicity over configurability.** No prompt-template engine, no per-workspace model configuration in V1. One model per deployment, configured once.
- **AI is advisory, never authoritative.** No AI output mutates domain state without an explicit user action. Suggestions are returned; the user accepts them through the existing endpoints.

## 3. Candidate Ideas

Ranked by value-to-effort. Sections 4–6 recommend a subset.

### 3.1 Duplicate issue detection

On create (and on demand), embed `Title + Description`, run a vector similarity search across open issues in the same project, and return likely duplicates above a threshold. Advisory only — creation is never blocked.

### 3.2 Estimate and assignee suggestion (retrieval-grounded)

Retrieve the *k* most similar **closed** issues in the project, feed their real `Estimate` values and who resolved them to the model, and return a structured suggestion with the supporting issue keys. The cited issues are the feature's credibility: the user can check the reasoning.

### 3.3 Sprint planning assistant

Given the ranked backlog, the last N sprints' completed points, and team capacity, propose a sprint scope with reasoning ("don't pull this Epic — three of its subtasks are still open"). Implemented with tool calling over existing read slices rather than stuffing the whole backlog into a prompt.

### 3.4 MCP server for JiraLite

Expose read and write slices as tools over the Model Context Protocol, so JiraLite can be driven from any MCP client (Claude Code, VS Code, Claude Desktop): *"what are my tasks today? move the login bug to In Progress and assign it to Sara."*

This is the highest-differentiation item and the cheapest to build correctly, because the slices already have clean contracts and the existing JWT policies keep enforcing authorization — the MCP surface authenticates as a real user and can do exactly what that user could do through the HTTP API, no more.

### 3.5 Daily digest / standup summary

A Hangfire job summarizing the last 24 hours of `ActivityLogEntry` per user, plus at-risk issues (due soon, no movement, blocked parent), delivered through the existing notification pipeline.

### 3.6 Automatic triage

Suggest `Type`, `Priority`, and labels for a new issue. Labels are constrained to those actually defined in the project — the model selects from a closed set, it does not invent values.

### 3.7 Comment thread summarization

For long threads: a short summary plus extracted decisions and open questions.

## 4. Recommended Scope

Take **3.1 + 3.2 + 3.4** as a single coherent increment, "Issue Intelligence":

1. They share one infrastructure investment (embeddings + one `IChatClient` registration).
2. 3.1 and 3.2 are read-only and advisory — low blast radius, easy to evaluate.
3. 3.4 is the genuinely novel part, and it reuses the slices already written rather than adding domain surface.

3.5 (digest) is a good follow-on: it needs no embeddings at all and reuses Hangfire and notifications end to end.

## 5. Proposed Architecture

### 5.1 Packages

| Concern | Choice |
|---|---|
| Model abstraction | `Microsoft.Extensions.AI` — `IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>` |
| Provider (dev) | Local model via Ollama, so the test suite and local runs cost nothing |
| Provider (prod) | Azure OpenAI or another hosted provider — swapped at DI registration only |
| Vector storage | SQL Server (see §5.3) |
| Background work | Hangfire (already present) |
| MCP | `ModelContextProtocol` C# SDK, hosted in the same process behind its own endpoint |

No new datastore, no message bus, no separate service. Consistent with the architectural exclusions in [spec/00-project-overview.md](../../spec/00-project-overview.md) §4.

### 5.2 Folder layout

```
src/Api/
  Common/
    Ai/
      AiOptions.cs               // model ids, thresholds, feature flag
      EmbeddingText.cs           // canonical "what text represents an issue"
      ServiceCollectionExtensions.cs
  Features/
    Intelligence/
      SuggestDuplicates.cs       // POST /api/projects/{projectId}/issues/suggest-duplicates
      SuggestEstimate.cs         // GET  /api/issues/{issueId}/suggest-estimate
    Mcp/
      JiraLiteTools.cs           // MCP tool surface delegating to existing handlers
```

### 5.3 Data model

One new table, owned by the Intelligence slice:

| Column | Type | Notes |
|---|---|---|
| `IssueId` | `uniqueidentifier` | PK, FK → `Issues`, cascade delete |
| `Vector` | `vector(768)` *or* `varbinary(max)` | see decision below |
| `ContentHash` | `char(64)` | SHA-256 of the embedded text; skip re-embedding when unchanged |
| `Model` | `nvarchar(100)` | which embedding model produced it — required for safe model migration |
| `GeneratedAtUtc` | `datetime2` | |

**Open decision:** SQL Server 2025 has a native `VECTOR` type and `VECTOR_DISTANCE()`. The current Compose stack pins `mssql/server:2022`, which does not. Two options:

- **Bump the image to 2025** and use native vector search. Cleanest, but changes the deployment baseline for everyone.
- **Store the vector as `varbinary` and compute cosine similarity in memory.** At this project's scale (a few thousand issues per project, filtered by `ProjectId` first) this is entirely adequate and adds no dependency. Recommended for V1; the column type is the only thing that changes later.

### 5.4 Keeping embeddings fresh

- `CreateIssue` / `EditIssue` enqueue a Hangfire job when `Title` or `Description` changed.
- A one-off backfill job embeds existing issues in batches.
- `ContentHash` makes every job idempotent — re-running costs nothing.
- If the embedding is missing or stale, suggestion endpoints degrade to returning no suggestions. **The AI path is never on the critical path of a write.**

### 5.5 Endpoint shapes

```
POST /api/projects/{projectId}/issues/suggest-duplicates
  { "title": "...", "description": "..." }
  → { "candidates": [ { "issueKey": "PRJ-12", "title": "...", "similarity": 0.89 } ] }

GET  /api/issues/{issueId}/suggest-estimate
  → { "suggestedEstimate": 5, "confidence": "medium",
      "basedOn": [ { "issueKey": "PRJ-48", "estimate": 5, "similarity": 0.81 } ] }
```

Both follow [spec/19-api-guidelines.md](../../spec/19-api-guidelines.md): same routing conventions, same Problem Details error shape, same auth policies as the issues they read.

## 6. Delivery Phases

| Phase | Deliverable | Definition of Done |
|---|---|---|
| A | `Microsoft.Extensions.AI` wired up, `AiOptions`, feature flag off by default, Ollama in Compose for dev | App boots with AI disabled and with AI enabled; no behavior change when disabled |
| B | `IssueEmbedding` table, backfill + incremental Hangfire jobs | Every issue in a seeded project has a current embedding; re-running the job is a no-op |
| C | `SuggestDuplicates` slice | Creating a near-copy of an existing issue surfaces it above threshold; unrelated issues do not |
| D | `SuggestEstimate` slice | Suggestion returns cited issue keys; returns empty (not an error) when the project has too little history |
| E | MCP server | An MCP client authenticated as a Developer can list and move issues; a Viewer is refused by the same policies as the HTTP API |

## 7. Risks and Guardrails

| Risk | Mitigation |
|---|---|
| Model output leaks across tenants | Every retrieval is filtered by `ProjectId` **in the SQL query**, before anything reaches the model — never by prompt instruction |
| MCP write tools cause unintended mutations | Writes go through the existing handlers and policies; destructive operations (delete) are excluded from the tool surface in V1 |
| Latency added to `CreateIssue` | Embedding is asynchronous via Hangfire; duplicate suggestion is a separate call the client makes before submitting |
| Provider outage | Feature flag + graceful degradation: suggestion endpoints return empty results, never 5xx the caller's workflow |
| Cost | Embeddings are cached by `ContentHash`; chat calls are per explicit user request, not per write |
| Prompt injection via issue descriptions | Retrieved issue text is data, not instruction; tool calls are constrained to a fixed allowlist and re-checked against the caller's role |

## 8. Conflict With a Declared Non-Goal

[spec/README.md](../../spec/README.md) and [spec/00-project-overview.md](../../spec/00-project-overview.md) §4 list **full-text/global search** as explicitly out of scope for V1, and state that such a feature must not be reintroduced without an explicit product decision.

Semantic similarity search is adjacent to that exclusion. This proposal keeps within the letter of it — the search is project-scoped, never global, and its output is a suggestion attached to a specific issue rather than a search endpoint — but the overlap is real and should be an explicit, recorded decision rather than something that slips in through an AI feature.

## 9. Explicitly Not in Scope

- Global natural-language search across all workspaces
- AI-authored issue content written directly to the database without user confirmation
- Per-workspace model or prompt configuration
- Fine-tuning or training on customer data
- Agentic multi-step workflows that mutate state without a human in the loop

## 10. Next Steps

1. Decide on §5.3 (SQL Server 2025 native vectors vs. in-memory cosine).
2. Record the §8 decision.
3. If approved, promote this to `spec/23-ai-intelligence.md` in the standard spec format (FR/BR/acceptance criteria) and add the task breakdown to [spec/22-tasks.md](../../spec/22-tasks.md).
