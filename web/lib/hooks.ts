"use client";

import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { api, type Listed, type Paged, type Query } from "./api";
import type {
  MyStats,
  ProjectRoleResult,
  SprintReport,
  Workspace,
  WorkspaceRoleResult,
  WorkspaceItem,
} from "./types";

/**
 * Every paged endpoint in JiraLite is cursor based. This is the only place
 * the cursor is threaded, so no page ever hand-rolls pagination.
 */
export function useCursorList<T>(
  key: unknown[],
  path: string,
  query: Query = {},
  options: { enabled?: boolean; limit?: number } = {},
) {
  const q = useInfiniteQuery({
    queryKey: [...key, query],
    enabled: options.enabled ?? true,
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      api.get<Paged<T>>(path, {
        ...query,
        limit: options.limit ?? 25,
        cursor: pageParam,
      }),
    getNextPageParam: (last) => last.pageInfo?.nextCursor ?? undefined,
  });

  return {
    ...q,
    items: (q.data?.pages.flatMap((p) => p.items) ?? []) as T[],
  };
}

/** Endpoints that return the full collection in one shot. */
export function useList<T>(
  key: unknown[],
  path: string,
  query?: Query,
  options: { enabled?: boolean } = {},
) {
  const q = useQuery({
    queryKey: [...key, query ?? {}],
    enabled: options.enabled ?? true,
    queryFn: () => api.get<Listed<T>>(path, query),
  });
  return { ...q, items: q.data?.items ?? [] };
}

/**
 * The counts behind the dashboard figures. One shape, not a list, so it sits
 * outside useList — and it keeps the previous window on screen while a longer
 * one loads, rather than blanking the charts on every range change.
 */
export function useMyStats(days: number) {
  return useQuery({
    queryKey: ["my-stats", days],
    queryFn: () => api.get<MyStats>("/api/dashboard/my-stats", { days }),
    placeholderData: (previous) => previous,
  });
}

/**
 * One sprint read whole (spec/24-reports.md). Like useMyStats it is a shape
 * rather than a list, and it holds the sprint already on screen while another
 * loads — switching sprints should move the figures, not blank the page.
 */
export function useSprintReport(sprintId?: string) {
  return useQuery({
    queryKey: ["sprint-report", sprintId],
    enabled: Boolean(sprintId),
    queryFn: () => api.get<SprintReport>(`/api/sprints/${sprintId}/report`),
    placeholderData: (previous) => previous,
  });
}

/* ── role gates ───────────────────────────────────────────────
   Roles are never cached in the JWT (spec/16-rbac.md NFR-01), so
   the UI asks the server what the caller may do and hides the rest.
   ─────────────────────────────────────────────────────────── */

export function useWorkspaceRole(workspaceId?: string) {
  const q = useQuery({
    queryKey: ["workspace-role", workspaceId],
    enabled: Boolean(workspaceId),
    staleTime: 60_000,
    queryFn: () =>
      api.get<WorkspaceRoleResult>(`/api/workspaces/${workspaceId}/my-role`),
  });
  return { ...q, isAdmin: q.data?.effectiveRole === "Admin" };
}

/**
 * A workspace Admin comes back as "Admin" rather than a project role — their
 * authority is a strict superset of ProjectAdmin (spec/16-rbac.md BR-03), so
 * they rank above it here.
 */
const RANK: Record<string, number> = {
  Viewer: 1,
  Developer: 2,
  ProjectAdmin: 3,
  Admin: 4,
};

export function useProjectRole(projectId?: string) {
  const q = useQuery({
    queryKey: ["project-role", projectId],
    enabled: Boolean(projectId),
    staleTime: 60_000,
    queryFn: () =>
      api.get<ProjectRoleResult>(`/api/projects/${projectId}/my-role`),
  });
  const role = q.data?.effectiveRole ?? null;
  const level = role ? (RANK[role] ?? 0) : 0;
  return {
    ...q,
    role,
    viaWorkspaceAdmin: q.data?.viaWorkspaceAdmin ?? false,
    /** may create/edit issues, comments, labels */
    canWrite: level >= 2,
    /** may configure the project: boards, columns, members, sprints */
    canAdmin: level >= 3,
    /** the one thing a ProjectAdmin cannot do (BR-03) */
    canDeleteProject: level >= 4,
  };
}

export function useWorkspaces() {
  return useList<WorkspaceItem>(["workspaces"], "/api/workspaces");
}

/** The workspace itself, for the name every workspace screen puts in its head. */
export function useWorkspace(workspaceId?: string) {
  return useQuery({
    queryKey: ["workspace", workspaceId],
    enabled: Boolean(workspaceId),
    queryFn: () => api.get<Workspace>(`/api/workspaces/${workspaceId}`),
  });
}
