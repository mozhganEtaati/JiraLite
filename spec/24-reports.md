# 24 — Reports

## 1. Overview

Covers the Sprint Report: one whole-team read of a single Sprint. Like [14-dashboard.md](14-dashboard.md) this is a query/projection context — it introduces no entities, only composed reads over `Sprint`, `Issue`, and `BoardColumn` ([00-project-overview.md](00-project-overview.md) §6, Activity & Reporting context).

Where the Dashboard is deliberately first-person — every view in it is scoped to the caller's own assignments — this document is the opposite: the Sprint entire, whoever it belongs to.

## 2. Business Goal

Let whoever is running a Sprint answer "are we going to make it?" without reading the board card by card: how far the work has travelled against how far through its calendar it is, where it sits, who is carrying it, and what is at risk.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Project member, I can see how much of the current Sprint is finished, by issue count and by story points. |
| US-02 | As a Project member, I can see that progress against how much of the Sprint's calendar has gone, so "40% done" means something. |
| US-03 | As a Project member, I can see what is blocked, why, and for how long. |
| US-04 | As a Project member, I can see how the Sprint is distributed across the team, including the work nobody has picked up. |
| US-05 | As a Project member, I can see one plain verdict on the Sprint together with every reason behind it. |

## 4. Functional Requirements

- FR-01: The Sprint Report returns the Sprint's own details, including how many Issues were carried out of it on completion.
- FR-02: It returns progress as issue counts and story-point sums, each with a completion percentage, and the number of Issues carrying no estimate.
- FR-03: It returns a count and point sum per `BoardColumn`, and a count per assignee including unassigned work.
- FR-04: It returns the Sprint's elapsed and remaining days and the share of its calendar already spent.
- FR-05: It returns the Sprint's risks: every blocked Issue with its reason and age, plus counts of overdue work, work due after the Sprint ends, unassigned work, and unestimated work.
- FR-06: It returns a health verdict of `OnTrack`, `AtRisk`, or `OffTrack`, together with every reason that produced it.

## 5. Non-Functional Requirements

- NFR-01: The Report introduces no index of its own. Its Issue set is reached through (`ProjectId`, `SprintId`, `Rank`), the index [09-issues.md](09-issues.md) NFR-02 already requires.
- NFR-02: Aggregation happens in application code rather than in SQL. A Sprint is tens of Issues, and the progress, status, assignee, and health figures are four readings of the same rows — fetching them once and counting four ways costs less than four round trips.

## 6. Business Rules

- BR-01: The Report is strictly read-only. No endpoint in this document creates, updates, or deletes any entity.
- BR-02: **Subtasks are excluded from every figure.** A Subtask's `SprintId` always tracks its parent's ([09-issues.md](09-issues.md) BR-11), so counting it beside the parent counts the same work twice and makes every percentage on the page wrong. This matches the Sprint Backlog listing ([07-backlog.md](07-backlog.md) BR-04).
- BR-03: Done-ness is the Issue's current `BoardColumn.IsDoneColumn` — Columns are status ([06-boards.md](06-boards.md)), so there is nothing else it could be.
- BR-04: `pace` is `null` for a `Planned` Sprint: nothing has elapsed, so there is nothing to be behind. For an `Active` Sprint it runs from `StartedAtUtc` — the source of truth for when the Sprint began ([08-sprints.md](08-sprints.md) BR-03) — to `PlannedEndDateUtc`, with `elapsedDays` clamped to the window. For a `Completed` Sprint it runs from `StartedAtUtc` to `CompletedAtUtc` and is fully elapsed, whatever the calendar said. Both ends are inclusive, so a one-day Sprint is one day long rather than zero.
- BR-05: Health is a **rule with its reasons attached**, never a bare percentage. A single "health %" cannot be traced back to anything and does not survive its first argument; a state plus the reasons that produced it can be acted on. A `Planned` Sprint has a `null` state and no reasons.
- BR-06: Every distinct problem is reported, each at the severity it actually reached — a Sprint far behind pace says so once as `WellBehindPace`, not twice at two severities. The state is the worst reason present.
- BR-07: The reasons and the thresholds that raise them:

  | Code | Raised when | State |
  |---|---|---|
  | `WellBehindPace` | at least 50% of the Sprint has elapsed **and** completion trails elapsed by more than 25 points | `OffTrack` |
  | `HeavilyBlocked` | there are at least **2** blocked Issues **and** they are at least 20% of open Issues | `OffTrack` |
  | `BehindPace` | completion trails elapsed by more than 10 points (and `WellBehindPace` did not fire) | `AtRisk` |
  | `BlockedWork` | at least one blocked Issue (and `HeavilyBlocked` did not fire) | `AtRisk` |
  | `OverdueWork` | at least one open Issue past its due date | `AtRisk` |
  | `DueAfterSprintEnd` | at least one open Issue due after `PlannedEndDateUtc` | `AtRisk` |
  | `EmptySprint` | the Sprint holds no Issues | `OnTrack` |

  Below half-elapsed a gap against the pace line is normal rather than alarming, which is why `WellBehindPace` carries an elapsed floor and `BehindPace` does not.

  `HeavilyBlocked` carries a count floor for the same kind of reason. On a small Sprint the share alone is twitchy — one blocker among five open Issues is already a fifth of the work — and a verdict of `OffTrack` for a single incident is one people learn to ignore. A lone blocker is always `BlockedWork`, whatever proportion of the Sprint it represents.

- BR-08: A `Completed` Sprint's figures describe **only what it finished**. Completion moves every unfinished Issue out of the Sprint ([08-sprints.md](08-sprints.md) BR-05), so what remains is Done by construction and reads 100%. `Sprint.CarriedForwardIssueCount` is what stops that being a lie; where it is `null` — Sprints completed before it was recorded — the figure is reported as unknown rather than as zero.
- BR-09: Status counts group by `BoardColumn.Name` with Done columns last, the same rule the Dashboard's status breakdown follows ([14-dashboard.md](14-dashboard.md) BR-10). Two Boards that both named a lane "In Progress" are one figure to the person reading it.
- BR-10: Unassigned work is returned as its own row with a `null` user rather than dropped. It is precisely what someone opening this page is looking for.
- BR-11: The point sums report `unestimatedIssues` beside them. A point figure read without knowing a third of the Sprint carries no estimate is worse than no point figure. This counts **every** Issue, because the point totals it qualifies include finished work; the separate `risks.unestimatedCount` counts only **open** Issues, because that is the work someone can still go and estimate. The two are expected to differ.
- BR-12: Blocked Issues are listed only where they are still open. BR-17 of [09-issues.md](09-issues.md) prevents an Issue in a Done column being blocked in the first place.

## 7. Database Entities

No new entities. This document composes reads over:

- `Sprint` — see [08-sprints.md](08-sprints.md), including `CarriedForwardIssueCount` (BR-08)
- `Issue` — see [09-issues.md](09-issues.md), including `IsBlocked`/`BlockedReason`/`BlockedSinceUtc`
- `BoardColumn` — see [06-boards.md](06-boards.md)

## 8. Relationships

No new relationships. Existing relationships are queried, not modified.

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/sprints/{sprintId}/report` | Project Member or Workspace Admin | The Sprint read whole |

## 10. Request Examples

```http
GET /api/sprints/{sprintId}/report
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**200 OK**
```json
{
  "sprint": {
    "id": "1f2e3d4c-...",
    "name": "Sprint 12",
    "goal": "Ship JWT refresh flow and workspace invitations",
    "status": "Active",
    "plannedStartDateUtc": "2026-08-03",
    "plannedEndDateUtc": "2026-08-14",
    "startedAtUtc": "2026-08-03T09:02:00Z",
    "completedAtUtc": null,
    "carriedForwardIssueCount": null
  },
  "pace": { "totalDays": 12, "elapsedDays": 7, "remainingDays": 5, "expectedPercent": 58 },
  "progress": {
    "issues": { "total": 18, "done": 7, "open": 11 },
    "points": { "total": 42.0, "done": 15.0, "open": 27.0, "unestimatedIssues": 3 },
    "donePercentByIssues": 39,
    "donePercentByPoints": 36
  },
  "byStatus": [
    { "name": "To Do", "count": 6, "points": 14.0, "isDone": false },
    { "name": "In Progress", "count": 5, "points": 13.0, "isDone": false },
    { "name": "Done", "count": 7, "points": 15.0, "isDone": true }
  ],
  "byAssignee": [
    { "user": { "id": "3c1a1e2e-...", "displayName": "Jane Doe", "avatarUrl": null },
      "total": 8, "done": 4, "open": 4, "points": 19.0, "blocked": 1 },
    { "user": null, "total": 3, "done": 0, "open": 3, "points": 5.0, "blocked": 0 }
  ],
  "risks": {
    "blocked": [
      {
        "id": "e5f6g7h8-...",
        "key": "JIRA-124",
        "title": "Support workspace invitations via email",
        "blockedReason": "Waiting on the payments vendor's security review",
        "blockedSinceUtc": "2026-08-04T11:20:00Z",
        "blockedDays": 6
      }
    ],
    "overdueCount": 2,
    "dueAfterSprintEndCount": 1,
    "unassignedCount": 3,
    "unestimatedCount": 3
  },
  "health": {
    "state": "AtRisk",
    "reasons": [
      { "code": "BehindPace", "detail": "39% done, 58% of the sprint elapsed" },
      { "code": "BlockedWork", "detail": "1 blocked issue" },
      { "code": "OverdueWork", "detail": "2 open issues are past their due date" }
    ]
  }
}
```

## 12. Validation Rules

No request parameters beyond the route's `sprintId`.

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Sprint not found | 404 Not Found | |
| Caller is not a Project member or Workspace Admin | 403 Forbidden | |
| Sprint holds no Issues | 200 OK | Zeros throughout, `EmptySprint` (BR-07) — not an error |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| Read the Sprint Report | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` — the same requirement as viewing the Sprint itself ([08-sprints.md](08-sprints.md) §14) |

The Report exposes nothing a Project member cannot already assemble by hand from the Sprint Backlog and the Board, so it does not narrow its audience further.

## 15. Acceptance Criteria

- Given a Sprint holding a parent Issue and its Subtask, when the Report is requested, then the total is 1 (BR-02).
- Given a Sprint of three Issues, one Done and estimated at 5 points, one open at 3, and one open with no estimate, when the Report is requested, then completion reads 33% by issue and 63% by point, and `unestimatedIssues` is 1 (FR-02, BR-11).
- Given Issues spread across To Do, In Progress, and Done, when the Report is requested, then the status buckets come back in that order regardless of the Columns' own `DisplayOrder`, with the Done bucket last (BR-09).
- Given a Sprint with unassigned work, when the Report is requested, then a row with a `null` user is present and `unassignedCount` matches it (BR-10).
- Given a `Planned` Sprint, when the Report is requested, then `pace` is null, the health state is null, and no reasons are returned (BR-04, BR-05).
- Given an `Active` Sprint on the last day of its window with nothing done, when the Report is requested, then the state is `OffTrack` and the only reason is `WellBehindPace` — never `WellBehindPace` and `BehindPace` together (BR-06, BR-07).
- Given a Sprint of ten open Issues of which one is blocked, when the Report is requested, then the state is `AtRisk` with reason `BlockedWork`; given four open Issues of which two are blocked, then the state is `OffTrack` with reason `HeavilyBlocked` (BR-07).
- Given a Sprint of only two open Issues of which one is blocked, when the Report is requested, then the state is `AtRisk` with reason `BlockedWork` — the single blocker is half the Sprint but does not clear the count floor (BR-07).
- Given a Sprint with one overdue Issue and one Issue due after the Sprint's end, when the Report is requested, then both `OverdueWork` and `DueAfterSprintEnd` are returned (BR-06).
- Given a Sprint holding no Issues, when the Report is requested, then it is 200 with `OnTrack` and reason `EmptySprint` (BR-07).
- Given a Sprint completed with two unfinished Issues carried out of it, when the Report is requested, then it reads 100% done over the one Issue left **and** reports `carriedForwardIssueCount` of 2 (BR-08).
- Given a user who is neither a Project member nor a Workspace Admin, when the Report is requested, then it is 403 (§14).

## 16. Future Improvements

- Cycle time and time-in-column, once status transitions are recorded. Today the only trace of a move is a free-text `ActivityLogEntry` summary with no from/to Column ([02-users.md](02-users.md)), so neither is derivable — both need a transition record written by the move handler, and both read as nothing until it has been accumulating for weeks.
- A true burndown, which needs either a daily snapshot job or a log of Sprint scope changes. Neither exists, so a burndown cannot be drawn backwards over Sprints already run.
- Velocity across Sprints. The data is already there — [08-sprints.md](08-sprints.md) BR-05 keeps `SprintId` on Done Issues permanently, so every completed Sprint's delivered points are recoverable — it simply has no endpoint yet.
- A Project-level roll-up across Sprints and Boards.
