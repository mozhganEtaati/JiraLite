# 18 — Database

## 1. Overview

This is the canonical, consolidated schema for JiraLite. Each feature document ([01](01-authentication.md)–[17](17-admin.md)) defines the fields it owns; this document is authoritative if any listing differs — in particular, audit columns (`CreatedAtUtc`/`UpdatedAtUtc`) are shown here in full even where a feature document abbreviated them for readability.

## 2. Global Conventions

These rules apply to every table unless a table's section explicitly states an exception.

| Rule | Convention |
|---|---|
| Primary keys | `Guid`, generated application-side (not database `IDENTITY`) — avoids enumeration, since IDs appear in URLs (Attachments, Invitations). |
| Table naming | Singular, `PascalCase`, matching the C# entity name exactly (`Issue`, not `Issues`). |
| Column naming | `PascalCase`, matching the C# property name exactly (EF Core default convention). |
| Audit fields | `CreatedAtUtc` (+ `CreatedByUserId` where a distinct actor exists) and `UpdatedAtUtc` (+ `UpdatedByUserId`) on every table **except**: pure join tables (`IssueLabel`), append-only logs (`ActivityLogEntry`, `Notification` — these use a single `CreatedAtUtc`/`OccurredAtUtc` since they are never updated except a narrow read-state field), and `User` (self-service actor, no distinct "created by"). |
| Soft delete | Applied **only** to `User.IsActive` (deactivation) and `Project.IsArchived` / `Workspace.IsArchived` (archiving). No other table has a soft-delete flag — see [00-project-overview.md](00-project-overview.md) §11 rationale. All other deletions are hard deletes. |
| Foreign keys | `NOT NULL` + `ON DELETE NO ACTION` (`RESTRICT`) by default. `ON DELETE CASCADE` is used only for children strictly owned by their aggregate root ([00-project-overview.md](00-project-overview.md) §7) and only where SQL Server's single-cascade-path restriction allows it (see §9 below). |
| Indexes | Every foreign key column is indexed by convention (not repeated per table below unless a *composite* index is needed). |
| Concurrency | A `RowVersion` (SQL Server `rowversion`) column is present on tables subject to concurrent drag-and-drop or reordering: `Issue`, `BoardColumn`. |

## 3. Identity & Access

### User
Purpose: platform-level identity and credentials.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| Email | string(256) | No | Unique index (case-insensitive collation) |
| PasswordHash | string(200) | No | |
| IsActive | bool | No | Default `true` (soft delete — §2) |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

Relationships: 1:1 → `UserProfile`, `NotificationPreference`. 1:N → `RefreshToken`, `Notification` (recipient), `ActivityLogEntry` (actor), and referenced as Reporter/Assignee/Author/Owner across most other tables.

### RefreshToken
Purpose: session renewal credential, rotated on use.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| UserId | Guid | No | FK → User, `ON DELETE CASCADE` (owned by User aggregate) |
| TokenHash | string(128) | No | SHA-256 hash |
| ExpiresAtUtc | datetime2 | No | |
| CreatedAtUtc | datetime2 | No | |
| RevokedAtUtc | datetime2 | Yes | |
| ReplacedByTokenId | Guid | Yes | FK → RefreshToken, `ON DELETE NO ACTION` (self-referencing) |

Index: (`UserId`, `RevokedAtUtc`) for active-token lookups.

### PersonalAccessToken
Purpose: long-lived machine credential for MCP clients, which cannot participate in the access/refresh exchange ([23-mcp-server.md](23-mcp-server.md) BR-02). Not interchangeable with `RefreshToken` or a JWT access token.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| UserId | Guid | No | FK → User, `ON DELETE CASCADE` (owned by User aggregate) |
| Name | string(100) | No | User-supplied label |
| TokenHash | string(128) | No | SHA-256 hash, unique |
| CreatedAtUtc | datetime2 | No | |
| ExpiresAtUtc | datetime2 | No | ≤ 365 days after creation ([23-mcp-server.md](23-mcp-server.md) BR-03) |
| LastUsedAtUtc | datetime2 | Yes | Null until first use |
| RevokedAtUtc | datetime2 | Yes | |

Indexes: unique (`TokenHash`) for authentication lookup; (`UserId`, `RevokedAtUtc`) for the active-token list and count.

### UserProfile
Purpose: display identity.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| UserId | Guid | No | FK → User, unique, `ON DELETE CASCADE` |
| DisplayName | string(100) | No | |
| AvatarUrl | string(2048) | Yes | |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### NotificationPreference
Purpose: per-user channel toggles.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| UserId | Guid | No | FK → User, unique, `ON DELETE CASCADE` |
| EmailEnabled | bool | No | Default `true` |
| InAppEnabled | bool | No | Default `true` |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

## 4. Activity & Notifications

### ActivityLogEntry
Purpose: immutable, append-only record of platform actions.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| ActorUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| WorkspaceId | Guid | No | FK → Workspace, `ON DELETE NO ACTION` |
| ProjectId | Guid | Yes | FK → Project, `ON DELETE NO ACTION`; set to `NULL` by application code when the Project is deleted ([05-projects.md](05-projects.md) BR-06) |
| EntityType | string(50) | No | |
| EntityId | Guid | No | |
| Action | string(50) | No | |
| Summary | string(500) | No | |
| OccurredAtUtc | datetime2 | No | |

Index: (`WorkspaceId`, `OccurredAtUtc`), (`ActorUserId`, `OccurredAtUtc`).

No `UpdatedAtUtc` — rows are never modified after insert (§2).

### Notification
Purpose: in-app notification record.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| RecipientUserId | Guid | No | FK → User, `ON DELETE CASCADE` |
| Type | string(30) | No | |
| Summary | string(500) | No | |
| EntityType | string(50) | No | |
| EntityId | Guid | No | |
| IsRead | bool | No | Default `false` |
| CreatedAtUtc | datetime2 | No | |
| ReadAtUtc | datetime2 | Yes | |

Index: (`RecipientUserId`, `IsRead`, `CreatedAtUtc`).

## 5. Workspace & Membership

### Organization
Purpose: top-level tenant boundary.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| Name | string(200) | No | |
| OwnerUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### Workspace
Purpose: grouping of Teams/Projects within an Organization.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| OrganizationId | Guid | No | FK → Organization, `ON DELETE NO ACTION` |
| Name | string(200) | No | |
| Description | string(1000) | Yes | |
| IsArchived | bool | No | Default `false` (soft delete — §2) |
| CreatedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### WorkspaceMember
Purpose: User↔Workspace membership with role.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| WorkspaceId | Guid | No | FK → Workspace, `ON DELETE CASCADE` |
| UserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| Role | string(20) | No | `Admin` \| `Member` |
| CreatedAtUtc | datetime2 | No | |

Constraint: unique (`WorkspaceId`, `UserId`).

### Invitation
Purpose: pending offer to join a Workspace.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| WorkspaceId | Guid | No | FK → Workspace, `ON DELETE CASCADE` |
| Email | string(256) | No | |
| Role | string(20) | No | `Admin` \| `Member` |
| Token | string(64) | No | Unique index |
| Status | string(20) | No | `Pending` \| `Accepted` \| `Declined` \| `Expired` \| `Revoked` |
| InvitedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| ExpiresAtUtc | datetime2 | No | |
| CreatedAtUtc | datetime2 | No | |
| AcceptedAtUtc | datetime2 | Yes | |
| AcceptedByUserId | Guid | Yes | FK → User, `ON DELETE NO ACTION` |

### Team
Purpose: sub-grouping of Workspace members.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| WorkspaceId | Guid | No | FK → Workspace, `ON DELETE CASCADE` |
| Name | string(100) | No | |
| Description | string(500) | Yes | |
| CreatedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### TeamMember
Purpose: User↔Team membership.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| TeamId | Guid | No | FK → Team, `ON DELETE CASCADE` |
| UserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| IsLead | bool | No | Default `false` |
| CreatedAtUtc | datetime2 | No | |

Constraint: unique (`TeamId`, `UserId`).

## 6. Project Planning

### Project
Purpose: container for Boards/Sprints/Issues.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| WorkspaceId | Guid | No | FK → Workspace, `ON DELETE NO ACTION` (deletion orchestrated at application level — see §9) |
| Key | string(10) | No | Unique per Workspace (case-insensitive), immutable |
| Name | string(200) | No | |
| Description | string(1000) | Yes | |
| IsArchived | bool | No | Default `false` (soft delete — §2) |
| CreatedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

Constraint: unique (`WorkspaceId`, `Key`).

### ProjectMember
Purpose: User↔Project membership with role.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| ProjectId | Guid | No | FK → Project, `ON DELETE CASCADE` |
| UserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| Role | string(20) | No | `ProjectAdmin` \| `Developer` \| `Viewer` |
| CreatedAtUtc | datetime2 | No | |

Constraint: unique (`ProjectId`, `UserId`).

### Board
Purpose: visual arrangement of a Project's Issues.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| ProjectId | Guid | No | FK → Project, `ON DELETE NO ACTION` (see §9) |
| Name | string(100) | No | |
| Type | string(20) | No | `Scrum` \| `Kanban`, immutable |
| DisplayOrder | int | No | |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

### BoardColumn
Purpose: status lane on a Board.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| BoardId | Guid | No | FK → Board, `ON DELETE CASCADE` |
| Name | string(100) | No | |
| DisplayOrder | int | No | |
| IsDefault | bool | No | Exactly one `true` per Board |
| IsDoneColumn | bool | No | At least one `true` per Board |
| RowVersion | rowversion | No | Concurrency token |

### Sprint
Purpose: time-boxed iteration on a Scrum Board.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| BoardId | Guid | No | FK → Board, `ON DELETE NO ACTION` (see §9) |
| ProjectId | Guid | No | FK → Project, `ON DELETE NO ACTION`, denormalized from Board |
| Name | string(100) | No | |
| Goal | string(500) | Yes | |
| Status | string(20) | No | `Planned` \| `Active` \| `Completed` |
| PlannedStartDateUtc | date | No | |
| PlannedEndDateUtc | date | No | |
| StartedAtUtc | datetime2 | Yes | |
| CompletedAtUtc | datetime2 | Yes | |
| CreatedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| CreatedAtUtc | datetime2 | No | |

## 7. Work Tracking

### Issue
Purpose: central work-item entity (Task/Story/Bug/Epic/Subtask).

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| ProjectId | Guid | No | FK → Project, `ON DELETE NO ACTION` (see §9) |
| Number | int | No | Sequential per Project, immutable |
| Key | string(20) | No | `{Project.Key}-{Number}`, immutable |
| Type | string(20) | No | `Epic` \| `Story` \| `Task` \| `Bug` \| `Subtask`, immutable |
| ParentIssueId | Guid | Yes | FK → Issue, `ON DELETE NO ACTION` (self-referencing) |
| Title | string(255) | No | |
| Description | string(50000) | Yes | Markdown |
| Priority | string(20) | No | `Low` \| `Medium` \| `High` \| `Critical` |
| BoardColumnId | Guid | No | FK → BoardColumn, `ON DELETE NO ACTION` (see §9) |
| SprintId | Guid | Yes | FK → Sprint, `ON DELETE NO ACTION` (see §9) |
| Rank | string(255) | No | |
| AssigneeUserId | Guid | Yes | FK → User, `ON DELETE NO ACTION` |
| ReporterUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| DueDateUtc | date | Yes | |
| Estimate | decimal(5,2) | Yes | |
| CreatedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| CreatedAtUtc | datetime2 | No | |
| UpdatedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| UpdatedAtUtc | datetime2 | No | |
| RowVersion | rowversion | No | Concurrency token |

Constraint: unique (`ProjectId`, `Number`).
Indexes: (`ProjectId`, `BoardColumnId`), (`ProjectId`, `SprintId`, `Rank`), (`ProjectId`, `AssigneeUserId`), (`ProjectId`, `DueDateUtc`).

### Comment
Purpose: discussion entry on an Issue.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| IssueId | Guid | No | FK → Issue, `ON DELETE CASCADE` |
| AuthorUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| Body | string(10000) | No | Markdown |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | Yes | Null = never edited |

### Attachment
Purpose: uploaded file on an Issue.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| IssueId | Guid | No | FK → Issue, `ON DELETE CASCADE` |
| UploadedByUserId | Guid | No | FK → User, `ON DELETE NO ACTION` |
| FileName | string(255) | No | |
| StorageKey | string(512) | No | |
| ContentType | string(100) | No | |
| SizeBytes | long | No | |
| CreatedAtUtc | datetime2 | No | |

### Label
Purpose: reusable Project-scoped tag.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid | No | PK |
| ProjectId | Guid | No | FK → Project, `ON DELETE CASCADE` |
| Name | string(50) | No | Unique per Project (case-insensitive) |
| Color | string(7) | No | `#RRGGBB` |
| CreatedAtUtc | datetime2 | No | |

Constraint: unique (`ProjectId`, `Name`).

### IssueLabel
Purpose: Issue↔Label join.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| IssueId | Guid | No | FK → Issue, `ON DELETE CASCADE`, composite PK |
| LabelId | Guid | No | FK → Label, `ON DELETE CASCADE`, composite PK |

No audit fields (pure join table — §2).

## 8. Entity Relationship Summary

See [00-project-overview.md](00-project-overview.md) §7–8 for the narrative version. In FK direction:

`RefreshToken/PersonalAccessToken/UserProfile/NotificationPreference/Notification → User` · `Workspace → Organization` · `WorkspaceMember/Invitation/Team/Project → Workspace` · `TeamMember → Team` · `ProjectMember/Board/Label → Project` · `BoardColumn → Board` · `Sprint → Board, Project` · `Issue → Project, BoardColumn, Sprint(0..1), Issue(0..1, self)` · `Comment/Attachment → Issue` · `IssueLabel → Issue, Label` · `ActivityLogEntry → User, Workspace, Project(0..1)`

## 9. Why Some Owning Relationships Use `NO ACTION` Instead of `CASCADE`

`Issue` is reachable from `Project` both directly (`Issue.ProjectId`) and indirectly (`Project → Board → BoardColumn → Issue`, and `Project → Sprint → Issue`). SQL Server rejects multiple cascade paths converging on the same table. Rather than fight this with partial cascades, every FK on `Issue`, `Board`, `Sprint`, and `Project→Workspace` is `NO ACTION`, and the cross-entity deletion/nullification effects already specified as business rules — Project deletion cascading to Boards/Sprints/Issues/Labels and nulling `ActivityLogEntry.ProjectId` ([05-projects.md](05-projects.md) BR-06), Sprint deletion nulling `Issue.SprintId` ([08-sprints.md](08-sprints.md) BR-06), Epic deletion detaching children ([09-issues.md](09-issues.md) BR-06), Board deletion being blocked while any Sprint references it ([06-boards.md](06-boards.md) BR-09) — are executed explicitly in application code inside a single transaction. This keeps deletion side effects auditable and testable rather than implicit in the database engine. Every business-rule document that orchestrates or is affected by a Project-level delete is listed here specifically so this section stays the complete checklist for that transaction — if a future document introduces a new entity referencing `Project`, `Board`, or `Sprint` with `NO ACTION`, its deletion/nullification behavior must be added to both this list and [05-projects.md](05-projects.md) BR-06.

## 10. Related Documents

- [00-project-overview.md](00-project-overview.md) — domain narrative and bounded contexts
- [19-api-guidelines.md](19-api-guidelines.md) — how these entities are exposed over the API
- [20-coding-guidelines.md](20-coding-guidelines.md) — EF Core configuration and migration strategy
