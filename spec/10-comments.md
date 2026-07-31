# 10 — Comments

## 1. Overview

Covers discussion Comments on an Issue: Create, Edit, Delete. A Comment belongs to exactly one Issue and is owned by the Issue aggregate ([09-issues.md](09-issues.md)).

## 2. Business Goal

Let Project members discuss an Issue in context, with a clear, simple authorship and moderation model.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Developer, I can add a Markdown comment to an Issue. |
| US-02 | As a comment author, I can edit my own comment. |
| US-03 | As a comment author, I can delete my own comment. |
| US-04 | As a Project Admin, I can delete any comment for moderation purposes. |

## 4. Functional Requirements

- FR-01: A Developer or Project Admin can add a Comment to an Issue in a Project they belong to.
- FR-02: A Comment's author can edit its body.
- FR-03: A Comment's author, a Project Admin, or a Workspace Admin can delete it.
- FR-04: Any Project member can list an Issue's Comments, ordered oldest first.

## 5. Non-Functional Requirements

- NFR-01: Comment `Body` accepts Markdown up to 10,000 characters, with the same basic sanitization baseline as Issue `Description` ([09-issues.md](09-issues.md) NFR-01).

## 6. Business Rules

- BR-01: Only a Comment's author may edit its content. Project Admins/Workspace Admins may delete any comment (moderation) but may never edit another user's comment content.
- BR-02: `Viewer`-role Project members are read-only and cannot create, edit, or delete Comments — consistent with the read-only role boundary applied elsewhere ([09-issues.md](09-issues.md), [07-backlog.md](07-backlog.md), [08-sprints.md](08-sprints.md)).
- BR-03: Comments are hard-deleted — there is no recovery once deleted (see [18-database.md](18-database.md) soft-delete policy: Comment is explicitly excluded).
- BR-04: Comments cannot be created, edited, or deleted on an Issue belonging to an archived Project ([05-projects.md](05-projects.md) BR-04).
- BR-05: Creating a Comment triggers a Notification to the Issue's assignee, reporter, and any prior commenters on that Issue (excluding the comment's own author) — see [13-notifications.md](13-notifications.md).
- BR-06: Editing a Comment sets `UpdatedAtUtc`; a Comment that has never been edited has `UpdatedAtUtc = NULL`.
- BR-07: Authorship alone does not permanently entitle a user to edit or delete their Comment. The caller must currently hold `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) — or be Workspace Admin — **at the time of the edit/delete request**, evaluated fresh like any other write action. A user demoted to `Viewer` after authoring a Comment can no longer edit or delete it themselves (a Project Admin/Workspace Admin can still delete it via moderation) — see [16-rbac.md](16-rbac.md) BR-06.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Comment

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| IssueId | Guid (FK → Issue) | No | |
| AuthorUserId | Guid (FK → User) | No | |
| Body | string(10000) | No | Markdown |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | Yes | Null = never edited (BR-06) |

## 8. Relationships

- `Issue (1) → Comment (N)`
- `User (1) → Comment (N)` as Author

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/issues/{issueId}/comments` | Project Member or Workspace Admin | List Comments (oldest first, paginated) |
| POST | `/api/issues/{issueId}/comments` | Developer, Project Admin, or Workspace Admin | Add Comment |
| PATCH | `/api/comments/{commentId}` | Comment author | Edit Comment |
| DELETE | `/api/comments/{commentId}` | Comment author, Project Admin, or Workspace Admin | Delete Comment |

## 10. Request Examples

**Add Comment**
```http
POST /api/issues/{issueId}/comments
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "body": "Verified the fix in staging — looks good. cc @jane"
}
```

**Edit Comment**
```http
PATCH /api/comments/{commentId}
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "body": "Verified the fix in staging and production — looks good."
}
```

## 11. Response Examples

**Add Comment — 201 Created**
```json
{
  "id": "a1b2c3d4-...",
  "issueId": "e5f6g7h8-...",
  "author": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
  "body": "Verified the fix in staging — looks good. cc @jane",
  "createdAtUtc": "2026-07-31T11:00:00Z",
  "updatedAtUtc": null
}
```

**List Comments — 200 OK**
```json
{
  "items": [
    {
      "id": "a1b2c3d4-...",
      "author": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
      "body": "Verified the fix in staging — looks good. cc @jane",
      "createdAtUtc": "2026-07-31T11:00:00Z",
      "updatedAtUtc": null
    }
  ],
  "pageInfo": { "hasNextPage": false, "nextCursor": null }
}
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| Body | Required, 1–10,000 chars after trimming whitespace |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Empty or whitespace-only body | 400 Bad Request | |
| Non-author attempts edit | 403 Forbidden | BR-01 |
| Non-author, non-admin attempts delete | 403 Forbidden | |
| Viewer attempts create/edit/delete | 403 Forbidden | BR-02 |
| Author attempts edit/delete after being demoted to Viewer | 403 Forbidden | BR-07 |
| Write action on archived Project's Issue | 409 Conflict | BR-04 |
| Comment or Issue not found | 404 Not Found | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View Comments | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` |
| Create Comment | `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) **or** `WorkspaceMember.Role = Admin` |
| Edit Comment | Comment's `AuthorUserId` matches the caller **and** the caller currently holds `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) or is Workspace Admin (BR-07) |
| Delete Comment | (Comment's `AuthorUserId` matches the caller **and** the caller currently holds `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) or is Workspace Admin — BR-07), **or** `ProjectMember.Role = ProjectAdmin`, **or** `WorkspaceMember.Role = Admin` |

## 15. Acceptance Criteria

- Given a Developer on a Project, when they add a Comment to an Issue, then it is persisted with `UpdatedAtUtc = NULL` and appears in the Issue's comment list.
- Given a Comment authored by User A, when User B (non-admin) attempts to edit it, then the request is rejected with 403.
- Given a Comment authored by User A, when a Project Admin deletes it, then it is permanently removed.
- Given a new Comment is added to an Issue, then the assignee, reporter, and prior commenters (excluding the author) receive a Notification.
- Given an archived Project, when adding a Comment to one of its Issues is attempted, then it is rejected with 409.

## 16. Future Improvements

- @mentions with targeted notifications.
- Comment reactions (emoji).
- Threaded replies.
- Comment edit history.
