# 13 — Notifications

## 1. Overview

Covers Notification delivery — Email and In-App — triggered by events elsewhere in the system (Comments, Issue assignment, Issue status changes). Delivery is gated per recipient by their [NotificationPreference](02-users.md). Workspace invitation emails ([03-workspaces.md](03-workspaces.md)) are a related but separate email-only flow, since an invitee may not yet have a `User` account — see BR-06.

## 2. Business Goal

Keep users informed of activity relevant to them without building a configurable subscription/watcher system — triggers are implicit and fixed (assignee, reporter, prior commenters).

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a user, I receive an in-app Notification when I'm assigned an Issue. |
| US-02 | As a user, I receive an in-app and/or email Notification when someone comments on an Issue I'm involved in. |
| US-03 | As a user, I can view my Notifications and mark them read. |
| US-04 | As a user, my channel preferences ([02-users.md](02-users.md)) control whether I get emailed, shown in-app, both, or neither. |

## 4. Functional Requirements

- FR-01: When an Issue's `AssigneeUserId` changes to a different, non-null user, that user receives a Notification (type `IssueAssigned`).
- FR-02: When an Issue's `BoardColumnId` changes, its assignee and reporter each receive a Notification (type `IssueStatusChanged`), excluding whoever performed the move.
- FR-03: When a Comment is added, the Issue's assignee, reporter, and prior commenters each receive a Notification (type `CommentAdded`), excluding the comment's author ([10-comments.md](10-comments.md) BR-05).
- FR-04: A user can list their own Notifications, see an unread count, and mark one or all as read.
- FR-05: Delivery per recipient is gated by that recipient's current `NotificationPreference` at the moment the triggering event occurs.

## 5. Non-Functional Requirements

- NFR-01: Email delivery is always asynchronous, dispatched via a Hangfire background job — never sent inline within the triggering HTTP request ([00-project-overview.md](00-project-overview.md) Assumption 9).
- NFR-02: Failed email delivery relies on Hangfire's built-in automatic retry — no custom retry logic is implemented in application code.

## 6. Business Rules

- BR-01: A user is never notified of their own actions (e.g., the person who added a comment, or performed the move/assignment, is excluded from that event's recipient list).
- BR-02: At the moment a triggering event occurs, each candidate recipient's `NotificationPreference` is evaluated independently: if `InAppEnabled = true`, a `Notification` row is created for them; if `EmailEnabled = true`, an email job is enqueued for them. If both are `false` for a recipient, no record and no email are produced for that recipient.
- BR-03: `IssueAssigned` fires only when the assignee actually changes to a different, non-null user — not on every Issue edit.
- BR-04: A `Notification` row's content is immutable after creation; the only fields that change afterward are `IsRead` and `ReadAtUtc`.
- BR-05: Each `Notification` stores a precomputed `Summary` at creation time (same pattern as `ActivityLogEntry`, [02-users.md](02-users.md) BR-06), not raw data requiring client-side reconstruction.
- BR-06: Workspace invitation emails are sent directly to the invitee's email address via the same background email mechanism, but do **not** create a `Notification` row, since the invitee may not yet have a `User` account ([03-workspaces.md](03-workspaces.md)).

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Notification

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| RecipientUserId | Guid (FK → User) | No | |
| Type | string(30) | No | `IssueAssigned` \| `IssueStatusChanged` \| `CommentAdded` |
| Summary | string(500) | No | Precomputed human-readable text (BR-05) |
| EntityType | string(50) | No | e.g. `Issue` |
| EntityId | Guid | No | Id of the referenced entity |
| IsRead | bool | No | Default `false` |
| CreatedAtUtc | datetime2 | No | |
| ReadAtUtc | datetime2 | Yes | Null until read |

## 8. Relationships

- `User (1) → Notification (N)` as Recipient

## 9. API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/notifications` | Authenticated | List own Notifications, paginated, newest first |
| GET | `/api/notifications/unread-count` | Authenticated | Get unread count |
| PATCH | `/api/notifications/{notificationId}/read` | Authenticated | Mark one Notification read |
| POST | `/api/notifications/read-all` | Authenticated | Mark all own Notifications read |

Preference management: `GET/PATCH /api/users/me/notification-preferences` — see [02-users.md](02-users.md).

## 10. Request Examples

**Mark one read**
```http
PATCH /api/notifications/{notificationId}/read
Authorization: Bearer {accessToken}
```

**Mark all read**
```http
POST /api/notifications/read-all
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**GET /api/notifications — 200 OK**
```json
{
  "items": [
    {
      "id": "d4e5f6a7-...",
      "type": "CommentAdded",
      "summary": "Jane Doe commented on JIRA-124",
      "entityType": "Issue",
      "entityId": "e5f6g7h8-...",
      "isRead": false,
      "createdAtUtc": "2026-07-31T11:35:00Z"
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

**GET /api/notifications/unread-count — 200 OK**
```json
{ "unreadCount": 3 }
```

## 12. Validation Rules

No client-supplied content fields — all Notification data is system-generated. Route parameters (`notificationId`) must be valid GUIDs.

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| `notificationId` does not exist or does not belong to the caller | 404 Not Found | Not 403 — avoids confirming another user's notification IDs exist |
| Mark an already-read Notification as read | 200 OK | Idempotent — not an error |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| List, count, mark read (one or all) | Authenticated; always scoped to the caller's own `RecipientUserId` — no role check needed |

## 15. Acceptance Criteria

- Given an Issue is assigned to User B by User A, then User B receives an `IssueAssigned` Notification (subject to their preferences) and User A does not.
- Given a Comment added by User A on an Issue assigned to User B, reported by User C, then User B and User C each receive a `CommentAdded` Notification; User A does not.
- Given a recipient with `InAppEnabled = false` and `EmailEnabled = true`, when a triggering event occurs, then no `Notification` row is created but an email is still dispatched.
- Given an unread Notification, when marked read, then `IsRead = true` and `ReadAtUtc` is set.
- Given a Workspace invitation is created, then an email is sent to the invitee's address without creating a `Notification` row.

## 16. Future Improvements

- Per-category notification preferences (see [02-users.md](02-users.md) §16).
- Digest emails (daily/weekly summary instead of per-event).
- Push notifications (mobile/web push).
- @mention-triggered notifications ([10-comments.md](10-comments.md) §16).
