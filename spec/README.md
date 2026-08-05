# JiraLite — Engineering Specification

JiraLite is a lightweight, backend-only project management platform inspired by Jira and Linear, built for small software teams, startups, and internal engineering teams. It prioritizes simplicity, maintainability, and developer experience over enterprise-level configurability.

This directory is the single source of truth for JiraLite's domain model, API, database, and engineering conventions. All implementation work must conform to these documents. Where a document's content depends on another, it references it rather than repeating it — always resolve conflicts in favor of the referenced document, not local restatement.

## Technology Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| API | ASP.NET Core Web API |
| Database | SQL Server |
| ORM | Entity Framework Core |
| Architecture style | Vertical Slice Architecture |
| Auth | JWT Authentication (access + refresh tokens) |
| Background jobs | Hangfire |
| Logging | Serilog |
| API docs | Swagger / OpenAPI |
| Containerization | Docker |

## How to Read This Specification

Documents are numbered in dependency order — each one assumes the concepts defined in earlier documents and does not redefine them. Read in order on first pass; use as independent reference thereafter.

| # | Document | Covers |
|---|---|---|
| 00 | [Project Overview](00-project-overview.md) | Vision, target users, goals, non-goals, architecture summary |
| 01 | [Authentication](01-authentication.md) | Login, Register, Logout, Refresh Token |
| 02 | [Users](02-users.md) | Profile, Avatar, Notification Preferences, Activity History |
| 03 | [Workspaces](03-workspaces.md) | Organization, Workspaces, Members, Invitations |
| 04 | [Teams](04-teams.md) | Team management, Team Members, Team Leads |
| 05 | [Projects](05-projects.md) | Create, Edit, Delete, Archive |
| 06 | [Boards](06-boards.md) | Scrum/Kanban boards, multiple boards, custom columns |
| 07 | [Backlog](07-backlog.md) | Product backlog, sprint backlog, ranking, drag & drop ordering |
| 08 | [Sprints](08-sprints.md) | Create, Start, Complete |
| 09 | [Issues](09-issues.md) | Task/Story/Bug/Epic/Subtask, all issue fields |
| 10 | [Comments](10-comments.md) | Create, Edit, Delete |
| 11 | [Attachments](11-attachments.md) | Upload, Download, Preview |
| 12 | [Labels](12-labels.md) | CRUD |
| 13 | [Notifications](13-notifications.md) | Email, In-App |
| 14 | [Dashboard](14-dashboard.md) | My Tasks, My Projects, Recent Activity |
| 15 | [Calendar](15-calendar.md) | Due Dates, Sprint Timeline |
| 16 | [RBAC](16-rbac.md) | Roles (Admin, Project Admin, Developer, Viewer), permissions |
| 17 | [Admin](17-admin.md) | User/Role/Project/Workspace administration |
| 18 | [Database](18-database.md) | Entities, columns, types, keys, indexes, constraints, relationships |
| 19 | [API Guidelines](19-api-guidelines.md) | Naming, routing, pagination, filtering, validation, error handling |
| 20 | [Coding Guidelines](20-coding-guidelines.md) | Folder structure, naming, DTOs, validation, logging, DI, config, migrations |
| 21 | [Roadmap](21-roadmap.md) | Phased delivery plan |
| 22 | [Tasks](22-tasks.md) | Implementation task breakdown |
| 23 | [MCP Server](23-mcp-server.md) | Model Context Protocol tool surface, Personal Access Tokens |

## Domain Model Summary

JiraLite's core hierarchy: an **Organization** contains **Workspaces**; a Workspace contains **Teams** and **Projects**; a Project contains **Boards**, **Sprints**, and **Issues**; an Issue may have **Comments**, **Attachments**, and **Labels**, and may be a parent or child of other Issues (Epic → Story/Task/Bug → Subtask, via a single self-referencing `Issue` entity — see [09-issues.md](09-issues.md)).

A Board's **Column** is the effective status of every Issue placed on it — JiraLite does not maintain a separate global status enum (see [06-boards.md](06-boards.md) and [09-issues.md](09-issues.md)).

Authorization uses four fixed roles with code-defined permission sets — there is no dynamic, user-configurable permission matrix (see [16-rbac.md](16-rbac.md)).

## Explicitly Out of Scope for V1

To keep the system simple, the following are **not** part of V1 and must not be introduced by any downstream document without an explicit product decision to add them:

- Full-text/global search
- Issue watchers/subscribers as a separate configurable list
- Time tracking / worklogs
- Issue linking beyond parent/child (blocks, relates-to, etc.)
- Custom fields or workflow builders
- Dynamic/user-editable permission schemes
- Multi-region or event-driven architecture (microservices, event bus, event sourcing, outbox pattern)

---

Next: [00-project-overview.md](00-project-overview.md)

Generate the next document?
