# 11 — Attachments

## 1. Overview

Covers file Attachments on an Issue: Upload, Download, Preview. Files are stored via a shared `IFileStorage` abstraction — V1 uses local disk (a Docker-mounted volume), chosen specifically so a future move to blob storage is a configuration change, not a rewrite (see [00-project-overview.md](00-project-overview.md) Assumption 5, Recommendation 4).

## 2. Business Goal

Let Project members attach supporting files (screenshots, logs, documents) to an Issue, with safe upload limits and inline preview for common formats.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a Developer, I can upload a file to an Issue. |
| US-02 | As a Project member, I can download any Attachment on an Issue I can view. |
| US-03 | As a Project member, I can preview an image or PDF Attachment inline without downloading it. |
| US-04 | As the uploader (or a Project Admin), I can delete an Attachment. |

## 4. Functional Requirements

- FR-01: A Developer or Project Admin can upload a file to an Issue, up to the configured size limit.
- FR-02: Any Project member can list an Issue's Attachments and download any of them.
- FR-03: Any Project member can preview an Attachment inline if its content type supports preview (images, PDF).
- FR-04: The uploader, a Project Admin, or a Workspace Admin can delete an Attachment, which also removes the underlying file from storage.

## 5. Non-Functional Requirements

- NFR-01: Maximum Attachment size is 25 MB (configurable via application settings).
- NFR-02: File storage access goes exclusively through `IFileStorage` — no feature handler talks to the filesystem or a cloud SDK directly (see [20-coding-guidelines.md](20-coding-guidelines.md)).
- NFR-03: Stored file names on disk are generated (e.g., a GUID-based key), never the client-supplied original name, to avoid path traversal and collisions; the original name is preserved as metadata for display and download.

## 6. Business Rules

- BR-01: Disallowed content types (executables and scripts — e.g., `.exe`, `.dll`, `.sh`, `.bat`, `.cmd`, `.msi`) are rejected at upload regardless of declared size.
- BR-02: Preview is supported only for `image/png`, `image/jpeg`, `image/gif`, `image/webp`, and `application/pdf`. Requesting preview for any other content type returns 415.
- BR-03: Download is available for every Attachment regardless of content type, served with `Content-Disposition: attachment` and the original filename.
- BR-04: Deleting an Attachment is a hard delete — both the database row and the underlying stored file are removed; there is no recovery.
- BR-05: Attachments cannot be uploaded to or deleted from an Issue belonging to an archived Project ([05-projects.md](05-projects.md) BR-04).
- BR-06: `Viewer`-role Project members can view, download, and preview Attachments but cannot upload or delete them — consistent with the read-only role boundary applied elsewhere ([10-comments.md](10-comments.md) BR-02).
- BR-07: Authorship alone does not permanently entitle a user to delete their Attachment. The caller must currently hold `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) — or be Workspace Admin — **at the time of the delete request**. A user demoted to `Viewer` after uploading an Attachment can no longer delete it themselves (a Project Admin/Workspace Admin can still delete it via moderation) — see [16-rbac.md](16-rbac.md) BR-06.

## 7. Database Entities

Full canonical schema is consolidated in [18-database.md](18-database.md).

### Attachment

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| IssueId | Guid (FK → Issue) | No | |
| UploadedByUserId | Guid (FK → User) | No | |
| FileName | string(255) | No | Original client-supplied filename |
| StorageKey | string(512) | No | Internal reference used by `IFileStorage`; never exposed to clients |
| ContentType | string(100) | No | |
| SizeBytes | long | No | |
| CreatedAtUtc | datetime2 | No | |

## 8. Relationships

- `Issue (1) → Attachment (N)`
- `User (1) → Attachment (N)` as Uploader

## 9. API Endpoints

| Method | Route | Auth/Role | Description |
|---|---|---|---|
| GET | `/api/issues/{issueId}/attachments` | Project Member or Workspace Admin | List Attachment metadata |
| POST | `/api/issues/{issueId}/attachments` | Developer, Project Admin, or Workspace Admin | Upload file |
| GET | `/api/attachments/{attachmentId}/download` | Project Member or Workspace Admin | Download original file |
| GET | `/api/attachments/{attachmentId}/preview` | Project Member or Workspace Admin | Inline preview (images/PDF only) |
| DELETE | `/api/attachments/{attachmentId}` | Uploader, Project Admin, or Workspace Admin | Delete Attachment |

## 10. Request Examples

**Upload**
```http
POST /api/issues/{issueId}/attachments
Authorization: Bearer {accessToken}
Content-Type: multipart/form-data; boundary=...

[binary file data, field name "file"]
```

**Download**
```http
GET /api/attachments/{attachmentId}/download
Authorization: Bearer {accessToken}
```

## 11. Response Examples

**Upload — 201 Created**
```json
{
  "id": "f1e2d3c4-...",
  "issueId": "e5f6g7h8-...",
  "fileName": "stack-trace.png",
  "contentType": "image/png",
  "sizeBytes": 84213,
  "uploadedBy": { "id": "3c1a1e2e-...", "displayName": "Jane Doe" },
  "createdAtUtc": "2026-07-31T11:15:00Z"
}
```

**List Attachments — 200 OK**
```json
{
  "items": [
    {
      "id": "f1e2d3c4-...",
      "fileName": "stack-trace.png",
      "contentType": "image/png",
      "sizeBytes": 84213,
      "createdAtUtc": "2026-07-31T11:15:00Z"
    }
  ]
}
```

**Download/Preview — 200 OK**
Binary response with headers:
```
Content-Type: image/png
Content-Disposition: attachment; filename="stack-trace.png"   (download)
Content-Disposition: inline; filename="stack-trace.png"       (preview)
```

## 12. Validation Rules

| Field | Rule |
|---|---|
| file | Required, ≤ 25 MB, content type not in the disallowed list (BR-01) |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| File exceeds 25 MB | 413 Payload Too Large | |
| Disallowed content type on upload | 415 Unsupported Media Type | BR-01 |
| Preview requested for a non-previewable content type | 415 Unsupported Media Type | BR-02 |
| Upload/delete on an archived Project's Issue | 409 Conflict | BR-05 |
| Non-uploader, non-admin attempts delete | 403 Forbidden | |
| Viewer attempts upload/delete | 403 Forbidden | BR-06 |
| Uploader attempts delete after being demoted to Viewer | 403 Forbidden | BR-07 |
| Attachment not found | 404 Not Found | |

## 14. Authorization Rules

| Action | Requirement |
|---|---|
| View, list, download, preview | `ProjectMember` (any role) **or** `WorkspaceMember.Role = Admin` |
| Upload | `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) **or** `WorkspaceMember.Role = Admin` |
| Delete | (Attachment's `UploadedByUserId` matches the caller **and** the caller currently holds `ProjectMember.Role` in (`Developer`, `ProjectAdmin`) or is Workspace Admin — BR-07), **or** `ProjectMember.Role = ProjectAdmin`, **or** `WorkspaceMember.Role = Admin` |

## 15. Acceptance Criteria

- Given a file under 25 MB with an allowed content type, when uploaded to an Issue, then an `Attachment` record is created and the file is stored via `IFileStorage`.
- Given a PNG Attachment, when preview is requested, then the file is served inline with the correct content type.
- Given a ZIP Attachment, when preview is requested, then the request is rejected with 415 (download remains available).
- Given an Attachment, when its uploader deletes it, then both the database row and the stored file are removed.
- Given an archived Project, when uploading an Attachment to one of its Issues is attempted, then it is rejected with 409.

## 16. Future Improvements

- Thumbnail generation for image previews.
- Virus/malware scanning on upload.
- Migration path to blob storage (Azure Blob/S3-compatible) via a new `IFileStorage` implementation.
- Per-Workspace storage quota.
