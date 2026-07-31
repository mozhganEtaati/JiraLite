# 02 — Users

## 1. Overview

Covers a User's profile, avatar, notification preferences, and activity history. Authentication (credentials, tokens) is defined in [01-authentication.md](01-authentication.md); this document covers the User-facing data created after an account exists.

## 2. Business Goal

Let every user personalize their identity within the platform (name, avatar), control how they're notified, and review a history of their own actions.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a user, I can view and edit my display name so others recognize me. |
| US-02 | As a user, I can upload an avatar image so my identity is visually recognizable. |
| US-03 | As a user, I can control whether I receive email and/or in-app notifications. |
| US-04 | As a user, I can view a history of my own actions across the platform. |
| US-05 | As a user, I can view another user's basic public profile (e.g., to see who is assigned to an issue). |
| US-06 | As a user, I can deactivate my own account so I can no longer log in. |

## 4. Functional Requirements

- FR-01: A `UserProfile` and default `NotificationPreference` are created automatically when a `User` registers (system action, not a separate API call).
- FR-02: A user can update their own display name.
- FR-03: A user can upload, replace, or remove their own avatar image.
- FR-04: A user can view and update their own notification preferences.
- FR-05: A user can retrieve a paginated history of `ActivityLogEntry` records where they are the actor.
- FR-06: Any authenticated user can retrieve another user's public profile subset (id, display name, avatar) for display purposes (e.g., rendering an assignee).
- FR-07: A user can deactivate their own account (`User.IsActive = false`), after which they can no longer log in or refresh a session ([01-authentication.md](01-authentication.md) BR-08).

## 5. Non-Functional Requirements

- NFR-01: Avatar uploads are limited to common image formats and a bounded file size (see §12) to prevent abuse.
- NFR-02: Activity history queries must be efficiently paginated (indexed on actor + time) since this table grows unbounded (see [18-database.md](18-database.md) §4, `ActivityLogEntry` indexes).
- NFR-03: A user's email is never exposed via the public profile endpoint (`GET /api/users/{userId}`) — only via `GET /api/users/me`.

## 6. Business Rules

- BR-01: `UserProfile` and `NotificationPreference` have a strict 1:1 relationship with `User`, created at registration with default values (`DisplayName` defaults to the email local-part; both notification channels default to enabled).
- BR-02: Avatar files are stored via the shared file storage abstraction (`IFileStorage`, see [11-attachments.md](11-attachments.md) for the pattern) — not as a database blob. `UserProfile.AvatarUrl` stores a reference, not file bytes.
- BR-03: Uploading a new avatar replaces the previous one; the previous file is deleted from storage.
- BR-04: `NotificationPreference` in V1 is a global per-channel toggle (`EmailEnabled`, `InAppEnabled`) applied uniformly across all notification types defined in [13-notifications.md](13-notifications.md). Per-category granularity is a Future Improvement (§16).
- BR-05: `ActivityLogEntry` records are immutable and system-written only. No endpoint allows a client to create, edit, or delete an entry directly — entries are written by the feature handler responsible for the action being logged (e.g., `CreateIssue`, `MoveIssue`, `AddComment`).
- BR-06: Each `ActivityLogEntry` stores a precomputed, human-readable `Summary` string at write time (e.g., "moved Issue JIRA-123 to Done") rather than requiring readers to reconstruct meaning from raw field diffs.
- BR-07: Account deactivation is **self-service only** in V1 — no Workspace Admin or platform-level role can deactivate another User's account, since `User` is a platform-level entity outside any single Workspace's authority ([16-rbac.md](16-rbac.md) BR-04). A Workspace Admin's only lever over a problematic member remains removing their Workspace access ([03-workspaces.md](03-workspaces.md)), not their account.
- BR-08: Deactivation is effectively permanent within V1 scope — there is no reactivation endpoint, since a deactivated user cannot log in to request one. Reactivation is deferred to a Future Improvement requiring out-of-band (support-assisted) tooling (§16).
- BR-09: A deactivated User's existing `WorkspaceMember`, `ProjectMember`, `TeamMember`, and `Issue.AssigneeUserId`/`ReporterUserId` references are left untouched — deactivation blocks authentication only, it does not retroactively remove memberships or reassign work. This matches the existing pattern where `User` is never hard-deleted (referential integrity for historical records is preserved).

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### UserProfile

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| UserId | Guid (FK → User, unique) | No | 1:1 with User |
| DisplayName | string(100) | No | |
| AvatarUrl | string(2048) | Yes | Null = no avatar set |
| UpdatedAtUtc | datetime2 | No | |

### NotificationPreference

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| UserId | Guid (FK → User, unique) | No | 1:1 with User |
| EmailEnabled | bool | No | Default `true` |
| InAppEnabled | bool | No | Default `true` |
| UpdatedAtUtc | datetime2 | No | |

### ActivityLogEntry

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| ActorUserId | Guid (FK → User) | No | Who performed the action |
| WorkspaceId | Guid (FK → Workspace) | No | Scoping for workspace-wide activity |
| ProjectId | Guid (FK → Project) | Yes | Null for workspace/team-level actions |
| EntityType | string(50) | No | e.g. `Issue`, `Comment`, `Project`, `Sprint` |
| EntityId | Guid | No | Id of the affected entity |
| Action | string(50) | No | e.g. `Created`, `Updated`, `Deleted`, `StatusChanged`, `Commented`, `Assigned` |
| Summary | string(500) | No | Precomputed human-readable description (BR-06) |
| OccurredAtUtc | datetime2 | No | |

## 8. Relationships

- `User (1) → UserProfile (1)`
- `User (1) → NotificationPreference (1)`
- `User (1) → ActivityLogEntry (N)` as `ActorUserId`
- `ActivityLogEntry (N) → Workspace (1)`, `ActivityLogEntry (N) → Project (0..1)`

## 9. API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/users/me` | Authenticated | Get current user's full profile |
| PATCH | `/api/users/me` | Authenticated | Update display name |
| PUT | `/api/users/me/avatar` | Authenticated | Upload/replace avatar (multipart/form-data) |
| DELETE | `/api/users/me/avatar` | Authenticated | Remove avatar |
| GET | `/api/users/me/notification-preferences` | Authenticated | Get own preferences |
| PATCH | `/api/users/me/notification-preferences` | Authenticated | Update own preferences |
| GET | `/api/users/me/activity` | Authenticated | Paginated own activity history |
| GET | `/api/users/{userId}` | Authenticated | Public profile subset of another user |
| POST | `/api/users/me/deactivate` | Authenticated | Deactivate own account (BR-07, BR-08) |

Pagination scheme for `/activity` follows [19-api-guidelines.md](19-api-guidelines.md).

## 10. Request Examples

**Update display name**
```http
PATCH /api/users/me
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "displayName": "Jane Doe"
}
```

**Upload avatar**
```http
PUT /api/users/me/avatar
Authorization: Bearer {accessToken}
Content-Type: multipart/form-data; boundary=...

[binary image data, field name "file"]
```

**Update notification preferences**
```http
PATCH /api/users/me/notification-preferences
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "emailEnabled": false,
  "inAppEnabled": true
}
```

**Deactivate account**
```http
POST /api/users/me/deactivate
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**GET /api/users/me — 200 OK**
```json
{
  "id": "3c1a1e2e-6b1a-4e9a-9c3e-1a2b3c4d5e6f",
  "email": "jane.doe@example.com",
  "displayName": "Jane Doe",
  "avatarUrl": "https://cdn.jiralite.local/avatars/3c1a1e2e.png",
  "createdAtUtc": "2026-07-31T10:00:00Z"
}
```

**GET /api/users/{userId} — 200 OK**
```json
{
  "id": "3c1a1e2e-6b1a-4e9a-9c3e-1a2b3c4d5e6f",
  "displayName": "Jane Doe",
  "avatarUrl": "https://cdn.jiralite.local/avatars/3c1a1e2e.png"
}
```

**GET /api/users/me/activity — 200 OK**
```json
{
  "items": [
    {
      "id": "a1b2c3d4-...",
      "entityType": "Issue",
      "entityId": "e5f6g7h8-...",
      "action": "StatusChanged",
      "summary": "moved Issue JIRA-123 to Done",
      "occurredAtUtc": "2026-07-31T09:45:00Z"
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

**Deactivate account — 204 No Content**
(empty body — any refresh token presented afterward is rejected per [01-authentication.md](01-authentication.md) BR-08)

## 12. Validation Rules

| Field | Rule |
|---|---|
| displayName | Required, 1–100 chars |
| avatar file | Content-Type must be `image/png`, `image/jpeg`, or `image/webp`; max 5 MB |
| emailEnabled / inAppEnabled | Boolean, required |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Display name empty or too long | 400 Bad Request | |
| Avatar file wrong content type | 400 Bad Request | |
| Avatar file exceeds size limit | 413 Payload Too Large | |
| Requested `userId` does not exist | 404 Not Found | |
| No avatar to delete | 204 No Content | Idempotent — not an error |
| Deactivate an already-deactivated account | 204 No Content | Idempotent — not an error |

## 14. Authorization Rules

| Endpoint | Requirement |
|---|---|
| All `/api/users/me/*` endpoints, including deactivate | Authenticated; operates only on the caller's own `UserId` from the access token — no role check needed since access is always self-scoped (BR-07: no other role can deactivate a different user) |
| `GET /api/users/{userId}` | Authenticated; any logged-in user may view any other user's public subset |

## 15. Acceptance Criteria

- Given a newly registered user, when their account is created, then a `UserProfile` and `NotificationPreference` exist with default values without any additional API call.
- Given a valid image under the size limit, when a user uploads an avatar, then `AvatarUrl` is updated and the previous file (if any) is deleted from storage.
- Given updated notification preferences, when a notification is later triggered for that user, the delivery pipeline ([13-notifications.md](13-notifications.md)) honors the current `EmailEnabled`/`InAppEnabled` values.
- Given a user performs an action (e.g., creates an issue), then a corresponding `ActivityLogEntry` is written with a precomputed `Summary` and appears in `GET /api/users/me/activity`.
- Given another user's id, when `GET /api/users/{userId}` is called, then only `id`, `displayName`, and `avatarUrl` are returned — never `email`.
- Given an active account, when the user calls `POST /api/users/me/deactivate`, then `IsActive` becomes `false`, all their `RefreshToken`s are treated as revoked, and a subsequent login attempt fails with the same generic message as invalid credentials ([01-authentication.md](01-authentication.md) §15).
- Given a deactivated user's `WorkspaceMember`/`ProjectMember`/assigned Issues, when deactivation completes, then none of those records are modified or removed (BR-09).

## 16. Future Improvements

- Per-category notification preferences (not just global per-channel toggles).
- Timezone and locale settings on `UserProfile`.
- Avatar image resizing/thumbnail generation on upload.
- Activity history filtering by entity type or date range.
- Profile visibility/privacy settings.
- Support-assisted account reactivation flow (BR-08).
- Workspace Admin ability to request removal of a disruptive member's platform account (currently out of scope — accounts are self-service only, BR-07).
