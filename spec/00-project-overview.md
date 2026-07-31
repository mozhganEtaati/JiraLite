# 00 — Project Overview

## 1. Overview

JiraLite is a backend-only project management API for small software teams, startups, and internal engineering teams. It combines familiar Jira/Linear concepts — projects, boards, sprints, issues — with a deliberately reduced feature set, favoring simplicity and developer experience over enterprise-level configurability.

See [README.md](README.md) for the full technology stack and document index.

## 2. Target Users

| User type | Primary needs |
|---|---|
| Small software teams | Plan and track work without heavyweight process overhead |
| Startups | Fast onboarding, low operational complexity, self-hostable via Docker |
| Internal engineering teams | Sprint planning, issue tracking, and visibility into team activity |

## 3. Goals

- Manage projects across one or more workspaces.
- Track work items (issues) through customizable board columns.
- Plan and run sprints (Scrum) or continuous flow (Kanban).
- Collaborate via comments, attachments, and labels.
- Give every user visibility into their own tasks, recent activity, and upcoming due dates.
- Provide simple, predictable role-based access control.

## 4. Non-Goals

JiraLite explicitly does not aim to replicate full Jira. The following are out of scope for V1 and must not be reintroduced by any other document without an explicit product decision:

- Full-text/global search
- Issue watchers/subscribers as a separate configurable list
- Time tracking / worklogs
- Issue linking beyond parent/child (blocks, relates-to, duplicates, etc.)
- Custom fields or workflow builders
- Dynamic, user-editable permission schemes
- API versioning scheme (single unversioned `/api` root for V1)

Architecturally, JiraLite avoids:

- Microservices
- Event bus / event-driven messaging
- Event sourcing
- Outbox pattern
- Complex CQRS (separate read/write databases, projections, etc.)
- Generic Repository Pattern (EF Core's `DbContext`/`DbSet<T>` is used directly)

## 5. Design Principles

1. **Simplicity over configurability.** Prefer a fixed, well-understood model (e.g., four roles, board columns as status) over building generic engines (permission matrices, workflow builders).
2. **One feature, one slice.** Every use case is a self-contained vertical slice — request, validation, handler, response — not spread across technical layers.
3. **No speculative abstraction.** Do not introduce interfaces, services, or patterns for hypothetical future needs (e.g., no repository layer, no message bus) unless a concrete V1 requirement needs it (e.g., file storage is abstracted because local-disk-to-blob-storage migration is a known likely need — see [11-attachments.md](11-attachments.md)).
4. **Consistency over local optimization.** Naming, routing, error handling, and validation follow one global convention (see [19-api-guidelines.md](19-api-guidelines.md)), never a per-feature variant.
5. **Explicit business rules over implicit schema constraints.** Where the database schema alone cannot prevent an invalid state (e.g., a Subtask having its own Subtask), the rule is enforced in application validation and documented in the relevant feature document, not assumed.

## 6. Architectural Approach

JiraLite is a **modular monolith** using **Vertical Slice Architecture (VSA)**:

- The codebase is organized by feature (`Features/Issues/CreateIssue`, `Features/Boards/AddColumn`, etc.), not by technical layer (no top-level `Controllers/`, `Services/`, `Repositories/`).
- Each slice owns its request/response contracts, validation, and handler logic. Slices do not call each other directly; shared needs go through the common domain model and `DbContext`.
- EF Core's `DbContext` is injected directly into handlers. No repository or unit-of-work abstraction is introduced on top of it.
- Cross-cutting concerns (validation pipeline, logging, exception-to-Problem-Details mapping, JWT/authorization policies) live in a shared `Common/` layer consumed by all slices.
- Background work (email delivery, notification dispatch, rank rebalancing) runs via Hangfire jobs, decoupled from the request/response cycle.

Full folder structure and conventions are defined in [20-coding-guidelines.md](20-coding-guidelines.md).

## 7. Bounded Contexts

JiraLite's domain is organized into six bounded contexts. These are logical groupings used to reason about ownership and change — they do not correspond to separate deployables or databases.

| Context | Responsibility | Primary documents |
|---|---|---|
| Identity & Access | Authentication, user identity, profile, credentials | [01](01-authentication.md), [02](02-users.md), [16](16-rbac.md) |
| Workspace & Membership | Organizations, workspaces, members, invitations, teams | [03](03-workspaces.md), [04](04-teams.md) |
| Project Planning | Projects, boards, columns, sprints, backlog ordering | [05](05-projects.md), [06](06-boards.md), [07](07-backlog.md), [08](08-sprints.md) |
| Work Tracking | Issues, comments, attachments, labels | [09](09-issues.md), [10](10-comments.md), [11](11-attachments.md), [12](12-labels.md) |
| Collaboration & Notifications | Notification delivery (email, in-app) | [13](13-notifications.md) |
| Activity & Reporting | Dashboard and calendar read views, activity history | [02](02-users.md), [14](14-dashboard.md), [15](15-calendar.md) |

Admin capability ([17-admin.md](17-admin.md)) is not a separate bounded context — it is an administrative overlay (Admin-role-only endpoints) over Identity & Access, Workspace & Membership, and Project Planning.

## 8. Domain Overview

A high-level narrative of how core entities relate; authoritative field-level detail lives in [18-database.md](18-database.md) and each entity's owning feature document.

- An **Organization** owns one or more **Workspaces**.
- A **Workspace** has **Members** (with a workspace-level role) and **Teams**.
- A **Workspace** owns one or more **Projects**.
- A **Project** has **Members** (with a project-level role), **Boards**, **Sprints**, and **Issues**.
- A **Board** (Scrum or Kanban) has one or more **Columns**; a Column is the effective status for any Issue placed on it.
- A **Sprint** belongs to a Project; an Issue's `SprintId` is nullable — unset means the issue is in the product backlog.
- An **Issue** may have a parent Issue (`ParentIssueId`), modeling Epic → Story/Task/Bug → Subtask as one entity with a `Type` discriminator, not five separate tables.
- An **Issue** may have **Comments**, **Attachments**, and **Labels**.
- A **User** receives **Notifications** (email and/or in-app) based on their involvement with an Issue (assignee, reporter, commenter).

## 9. Related Documents

- [README.md](README.md) — full document index and technology stack
- [16-rbac.md](16-rbac.md) — roles and authorization policy detail
- [18-database.md](18-database.md) — full schema
- [19-api-guidelines.md](19-api-guidelines.md) — API-wide conventions
- [20-coding-guidelines.md](20-coding-guidelines.md) — folder structure and code conventions
- [21-roadmap.md](21-roadmap.md) — phased delivery plan
