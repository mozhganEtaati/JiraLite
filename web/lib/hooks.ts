"use client";

import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { api, type Listed, type Paged, type Query } from "./api";
import type {
  ProjectRoleResult,
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
