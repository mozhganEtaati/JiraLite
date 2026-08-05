"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useMemo } from "react";
import { api } from "@/lib/api";
import { useCursorList, useList, useProjectRole } from "@/lib/hooks";
import type {
  Board,
  BoardItem,
  IssueListItem,
  Label,
  ProjectMember,
  SprintItem,
} from "@/lib/types";
import { ISSUE_TYPES, PRIORITIES } from "@/lib/types";
import {
  Avatar,
  DueDate,
  IssueKey,
  PriorityMark,
  TypeMark,
} from "@/components/marks";
import {
  Empty,
  ErrorNote,
  Loading,
  LoadMore,
  PageHead,
} from "@/components/kit";
import { NewIssueButton } from "@/components/new-issue";

const FILTER_KEYS = [
  "type",
  "priority",
  "assigneeUserId",
  "boardColumnId",
  "labelId",
  "sprintId",
] as const;

function IssuesView() {
  const { projectId } = useParams<{ projectId: string }>();
  const params = useSearchParams();
  const router = useRouter();
  const { canWrite } = useProjectRole(projectId);

  const filters = useMemo(() => {
    const f: Record<string, string> = {};
    for (const k of FILTER_KEYS) {
      const v = params.get(k);
      if (v) f[k] = v;
    }
    return f;
  }, [params]);

  const activeCount = Object.keys(filters).length;

  function setFilter(key: string, value: string) {
    const next = new URLSearchParams(params.toString());
    if (value) next.set(key, value);
    else next.delete(key);
    router.replace(`/p/${projectId}/issues?${next.toString()}`);
  }

  const issues = useCursorList<IssueListItem>(
    ["project-issues", projectId],
    `/api/projects/${projectId}/issues`,
    filters,
    { limit: 30 },
  );

  const members = useList<ProjectMember>(
    ["project-members", projectId],
    `/api/projects/${projectId}/members`,
  );
  const labels = useList<Label>(
    ["labels", projectId],
    `/api/projects/${projectId}/labels`,
  );
  const boards = useList<BoardItem>(
    ["boards", projectId],
    `/api/projects/${projectId}/boards`,
  );
  const firstBoard = boards.items[0];
  const { data: board } = useQuery({
    queryKey: ["board", firstBoard?.id],
    enabled: Boolean(firstBoard),
    queryFn: () => api.get<Board>(`/api/boards/${firstBoard!.id}`),
  });
  const scrumBoard = boards.items.find((b) => b.type === "Scrum");
  const sprints = useList<SprintItem>(
    ["sprints", scrumBoard?.id],
    `/api/boards/${scrumBoard?.id}/sprints`,
    undefined,
    { enabled: Boolean(scrumBoard) },
  );

  const columnName = useMemo(
    () => Object.fromEntries((board?.columns ?? []).map((c) => [c.id, c.name])),
    [board],
  );

  return (
    <>
      <PageHead
        eyebrow="Issues"
        title="Every issue in this project"
        meta="JiraLite has no global search — filter here instead. The filters live in the URL, so a filtered view is shareable."
        actions={canWrite && <NewIssueButton projectId={projectId} />}
      />

      <div className="card mb-4 flex flex-wrap items-end gap-2 p-2.5">
        <Select
          label="Type"
          value={filters.type ?? ""}
          onChange={(v) => setFilter("type", v)}
          options={ISSUE_TYPES.map((t) => [t, t])}
        />
        <Select
          label="Priority"
          value={filters.priority ?? ""}
          onChange={(v) => setFilter("priority", v)}
          options={PRIORITIES.map((p) => [p, p])}
        />
        <Select
          label="Assignee"
          value={filters.assigneeUserId ?? ""}
          onChange={(v) => setFilter("assigneeUserId", v)}
          options={members.items.map((m) => [m.userId, m.displayName])}
        />
        <Select
          label="Column"
          value={filters.boardColumnId ?? ""}
          onChange={(v) => setFilter("boardColumnId", v)}
          options={(board?.columns ?? []).map((c) => [c.id, c.name])}
        />
        <Select
          label="Label"
          value={filters.labelId ?? ""}
          onChange={(v) => setFilter("labelId", v)}
          options={labels.items.map((l) => [l.id, l.name])}
        />
        {scrumBoard && (
          <Select
            label="Sprint"
            value={filters.sprintId ?? ""}
            onChange={(v) => setFilter("sprintId", v)}
            options={sprints.items.map((s) => [s.id, s.name])}
          />
        )}
        {activeCount > 0 && (
          <button
            type="button"
            className="btn btn-bare btn-sm mb-[1px]"
            onClick={() => router.replace(`/p/${projectId}/issues`)}
          >
            Clear {activeCount} {activeCount === 1 ? "filter" : "filters"}
          </button>
        )}
      </div>

      <ErrorNote error={issues.error} className="mb-3" />

      <div className="card overflow-hidden">
        {issues.isLoading ? (
          <Loading />
        ) : issues.items.length === 0 ? (
          <Empty
            title={activeCount ? "No issues match these filters" : "No issues yet"}
            hint={
              activeCount
                ? "Loosen a filter, or clear them all."
                : "Create the first issue to get the board moving."
            }
            action={
              activeCount ? (
                <button
                  type="button"
                  className="btn btn-ghost btn-sm"
                  onClick={() => router.replace(`/p/${projectId}/issues`)}
                >
                  Clear filters
                </button>
              ) : canWrite ? (
                <NewIssueButton projectId={projectId} />
              ) : undefined
            }
          />
        ) : (
          <ul className="sheet">
            {issues.items.map((i) => (
              <li key={i.id}>
                <Link
                  href={`/i/${i.id}`}
                  className="row-hover flex items-center gap-3 px-3 py-2.5"
                >
                  <TypeMark type={i.type} />
                  <IssueKey issueKey={i.key} />
                  <span className="min-w-0 flex-1 truncate">{i.title}</span>
                  {columnName[i.boardColumnId] && (
                    <span className="chip hidden md:inline-flex">
                      {columnName[i.boardColumnId]}
                    </span>
                  )}
                  <PriorityMark priority={i.priority} />
                  <DueDate value={i.dueDateUtc} />
                  <Avatar user={i.assignee} size={20} />
                </Link>
              </li>
            ))}
          </ul>
        )}
        <LoadMore
          hasNext={Boolean(issues.hasNextPage)}
          loading={issues.isFetchingNextPage}
          onClick={() => issues.fetchNextPage()}
        />
      </div>
    </>
  );
}

function Select({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: [string, string][];
}) {
  return (
    <label className="block">
      <span className="t-eyebrow mb-1 block text-[10px]">{label}</span>
      <select
        className="field h-8 w-auto min-w-[118px] py-0 text-[12px]"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="">Any</option>
        {options.map(([v, l]) => (
          <option key={v} value={v}>
            {l}
          </option>
        ))}
      </select>
    </label>
  );
}

export default function IssuesPage() {
  return (
    <Suspense fallback={<Loading />}>
      <IssuesView />
    </Suspense>
  );
}
