/**
 * Response contracts, transcribed from the slice records in src/Api/Features.
 * Where the backend uses a `public static class X { public const string ... }`
 * instead of an enum, the union here mirrors the constant list exactly.
 */

export type UserSummary = {
  id: string;
  displayName: string;
  avatarUrl: string | null;
};

export type IssueTypeName = "Epic" | "Story" | "Task" | "Bug" | "Subtask";
export const ISSUE_TYPES: IssueTypeName[] = [
  "Epic",
  "Story",
  "Task",
  "Bug",
  "Subtask",
];

export type PriorityName = "Low" | "Medium" | "High" | "Critical";
export const PRIORITIES: PriorityName[] = ["Low", "Medium", "High", "Critical"];

export type BoardTypeName = "Scrum" | "Kanban";
export type SprintStatusName = "Planned" | "Active" | "Completed";
export type ProjectRoleName = "ProjectAdmin" | "Developer" | "Viewer";
export const PROJECT_ROLES: ProjectRoleName[] = [
  "ProjectAdmin",
  "Developer",
  "Viewer",
];
export type WorkspaceRoleName = "Admin" | "Member";
export type InvitationStatusName =
  | "Pending"
  | "Accepted"
  | "Declined"
  | "Expired"
  | "Revoked";

/* ── auth & users ─────────────────────────────────────────── */

export type AuthTokens = {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
};

export type Me = {
  id: string;
  email: string;
  displayName: string;
  avatarUrl: string | null;
  createdAtUtc: string;
};

export type NotificationPrefs = { emailEnabled: boolean; inAppEnabled: boolean };

export type ActivityEntry = {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  summary: string;
  occurredAtUtc: string;
};

/* ── organizations & workspaces ───────────────────────────── */

export type OrganizationItem = {
  id: string;
  name: string;
  createdAtUtc: string;
};

export type Organization = {
  id: string;
  name: string;
  ownerUserId: string;
  createdAtUtc: string;
};

export type WorkspaceItem = {
  id: string;
  organizationId: string;
  name: string;
  description: string | null;
  isArchived: boolean;
  role: WorkspaceRoleName;
};

export type Workspace = {
  id: string;
  organizationId: string;
  name: string;
  description: string | null;
  isArchived: boolean;
  createdAtUtc: string;
};

export type WorkspaceMember = {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  role: WorkspaceRoleName;
  joinedAtUtc: string;
};

export type Invitation = {
  id: string;
  email: string;
  role: WorkspaceRoleName;
  status: InvitationStatusName;
  expiresAtUtc: string;
  createdAtUtc: string;
};

/* ── teams ────────────────────────────────────────────────── */

export type TeamItem = {
  id: string;
  name: string;
  description: string | null;
  createdAtUtc: string;
};

export type TeamMember = {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  isLead: boolean;
  joinedAtUtc: string;
};

export type Team = {
  id: string;
  workspaceId: string;
  name: string;
  description: string | null;
  members: TeamMember[];
};

/* ── projects ─────────────────────────────────────────────── */

export type ProjectListItem = {
  id: string;
  key: string;
  name: string;
  description: string | null;
  isArchived: boolean;
};

export type Project = {
  id: string;
  workspaceId: string;
  key: string;
  name: string;
  description: string | null;
  isArchived: boolean;
  createdAtUtc: string;
};

export type ProjectMember = {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  role: ProjectRoleName;
  joinedAtUtc: string;
};

export type ProjectRoleResult = {
  projectId: string;
  effectiveRole: ProjectRoleName | null;
  viaWorkspaceAdmin: boolean;
};

export type WorkspaceRoleResult = {
  workspaceId: string;
  effectiveRole: WorkspaceRoleName | null;
};

/* ── boards ───────────────────────────────────────────────── */

export type BoardItem = {
  id: string;
  name: string;
  type: BoardTypeName;
  displayOrder: number;
};

export type BoardColumn = {
  id: string;
  name: string;
  displayOrder: number;
  isDefault: boolean;
  isDoneColumn: boolean;
  rowVersion: string;
};

export type Board = {
  id: string;
  projectId: string;
  name: string;
  type: BoardTypeName;
  columns: BoardColumn[];
};

export type BoardCardIssue = {
  id: string;
  key: string;
  title: string;
  type: IssueTypeName;
  priority: PriorityName;
  assignee: UserSummary | null;
};

export type BoardColumnGroup = { columnId: string; issues: BoardCardIssue[] };

/* ── issues ───────────────────────────────────────────────── */

export type IssueListItem = {
  id: string;
  key: string;
  number: number;
  type: IssueTypeName;
  title: string;
  priority: PriorityName;
  boardColumnId: string;
  sprintId: string | null;
  assignee: UserSummary | null;
  dueDateUtc: string | null;
};

export type IssueLabel = { id: string; name: string; color: string };

export type Issue = {
  id: string;
  key: string;
  number: number;
  type: IssueTypeName;
  parentIssueId: string | null;
  title: string;
  description: string | null;
  priority: PriorityName;
  boardColumnId: string;
  sprintId: string | null;
  rank: string;
  assignee: UserSummary | null;
  reporter: UserSummary;
  dueDateUtc: string | null;
  estimate: number | null;
  labels: IssueLabel[];
  subtaskCount: number;
  rowVersion: string;
};

export type Subtask = {
  id: string;
  key: string;
  number: number;
  title: string;
  priority: PriorityName;
  boardColumnId: string;
  assignee: UserSummary | null;
};

export type BacklogItem = {
  id: string;
  key: string;
  title: string;
  type: IssueTypeName;
  priority: PriorityName;
  rank: string;
  assignee: UserSummary | null;
};

/* ── sprints ──────────────────────────────────────────────── */

export type SprintItem = {
  id: string;
  boardId: string;
  name: string;
  status: SprintStatusName;
  plannedStartDateUtc: string;
  plannedEndDateUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
};

export type Sprint = SprintItem & { goal: string | null };

/* ── comments, attachments, labels ────────────────────────── */

export type Comment = {
  id: string;
  author: UserSummary;
  body: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
};

export type Attachment = {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
};

export type Label = { id: string; name: string; color: string };

/* ── notifications ────────────────────────────────────────── */

export type NotificationItem = {
  id: string;
  type: "IssueAssigned" | "IssueStatusChanged" | "CommentAdded";
  summary: string;
  entityType: string;
  entityId: string;
  isRead: boolean;
  createdAtUtc: string;
};

/* ── dashboard & calendar ─────────────────────────────────── */

export type MyTask = {
  id: string;
  key: string;
  type: IssueTypeName;
  title: string;
  priority: PriorityName;
  dueDateUtc: string | null;
  projectKey: string;
  columnName: string;
};

export type MyProject = {
  id: string;
  key: string;
  name: string;
  role: ProjectRoleName | WorkspaceRoleName;
};

export type MyStats = {
  days: number;
  totals: {
    assigned: number;
    open: number;
    done: number;
    overdue: number;
    dueSoon: number;
  };
  byStatus: { name: string; count: number; isDone: boolean }[];
  byPriority: { priority: PriorityName; count: number }[];
  activity: {
    date: string;
    created: number;
    moved: number;
    commented: number;
  }[];
};

export type FeedEntry = {
  id: string;
  actor: UserSummary;
  summary: string;
  entityType: string;
  entityId: string;
  occurredAtUtc: string;
};

export type DueDateEntry = {
  id: string;
  key: string;
  title: string;
  type: IssueTypeName;
  dueDateUtc: string;
  assignee: UserSummary | null;
};

export type TimelineSprint = {
  id: string;
  name: string;
  status: SprintStatusName;
  plannedStartDateUtc: string;
  plannedEndDateUtc: string;
};

/* ── admin ────────────────────────────────────────────────── */

export type AdminOverview = {
  workspaceId: string;
  memberCount: number;
  teamCount: number;
  projectCount: number;
  activeProjectCount: number;
  archivedProjectCount: number;
  pendingInvitationCount: number;
};

export type AdminUser = {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  email: string;
  isActive: boolean;
  workspaceRole: WorkspaceRoleName;
  joinedAtUtc: string;
  projectRoles: { projectId: string; projectKey: string; role: string }[];
};

export type AdminProject = {
  projectId: string;
  key: string;
  name: string;
  isArchived: boolean;
  memberCount: number;
  issueCount: number;
  createdAtUtc: string;
};

export type AdminRole = { scope: string; role: string; description: string };
