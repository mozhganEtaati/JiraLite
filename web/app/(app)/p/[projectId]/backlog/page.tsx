"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useCursorList, useList, useProjectRole } from "@/lib/hooks";
import { fullDate } from "@/lib/format";
import type {
  BacklogItem,
  BoardItem,
  Issue,
  SprintItem,
} from "@/lib/types";
import {
  Avatar,
  IssueKey,
  PriorityMark,
  SprintStatusChip,
  TypeMark,
} from "@/components/marks";
import {
  Empty,
  ErrorNote,
  Loading,
  LoadMore,
  PageHead,
  Section,
} from "@/components/kit";
import { NewIssueButton } from "@/components/new-issue";

export default function BacklogPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const qc = useQueryClient();
  const { canWrite, canAdmin } = useProjectRole(projectId);

  const boards = useList<BoardItem>(
    ["boards", projectId],
    `/api/projects/${projectId}/boards`,
  );
  const scrumBoard = boards.items.find((b) => b.type === "Scrum");

  const sprints = useList<SprintItem>(
    ["sprints", scrumBoard?.id],
    `/api/boards/${scrumBoard?.id}/sprints`,
    undefined,
    { enabled: Boolean(scrumBoard) },
  );
  const openSprints = sprints.items.filter((s) => s.status !== "Completed");
  const [targetId, setTarget] = useState<string>("");
  const target =
    openSprints.find((s) => s.id === targetId) ??
    openSprints.find((s) => s.status === "Active") ??
    openSprints[0];

  const backlog = useCursorList<BacklogItem>(
    ["backlog", projectId],
    `/api/projects/${projectId}/backlog`,
    {},
    { limit: 40 },
  );

  const sprintItems = useCursorList<BacklogItem>(
    ["backlog", "sprint", target?.id],
    `/api/sprints/${target?.id}/backlog`,
    {},
    { limit: 40, enabled: Boolean(target) },
  );

  const assign = useMutation({
    mutationFn: (issueId: string) =>
      api.post(`/api/sprints/${target!.id}/issues`, { issueId }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["backlog"] }),
  });

  const remove = useMutation({
    mutationFn: (issueId: string) =>
      api.del(`/api/sprints/${target!.id}/issues/${issueId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["backlog"] }),
  });

  /** Rank is repositioned relative to the row above, so "up" needs the row two above. */
  const reorder = useMutation({
    mutationFn: async (v: { issueId: string; afterIssueId: string | null }) => {
      const issue = await api.get<Issue>(`/api/issues/${v.issueId}`);
      await api.patch(`/api/issues/${v.issueId}/rank`, {
        afterIssueId: v.afterIssueId,
        rowVersion: issue.rowVersion,
      });
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["backlog"] }),
  });

  if (boards.isLoading) return <Loading label="Loading the backlog" />;

  return (
    <>
      <PageHead
        eyebrow="Backlog"
        title="Plan the next sprint"
        meta="Ordered by rank. The top of the list is what the team picks up first."
        actions={canWrite && <NewIssueButton projectId={projectId} />}
      />

      <ErrorNote
        error={assign.error ?? remove.error ?? reorder.error}
        className="mb-3"
      />

      <div className="grid gap-5 lg:grid-cols-2">
        <Section
          title="Product backlog"
          count={backlog.items.length}
        >
          <div className="card overflow-hidden">
            {backlog.isLoading ? (
              <Loading />
            ) : backlog.items.length === 0 ? (
              <Empty
                title="The backlog is empty"
                hint="Every issue that is not in a sprint shows up here."
              />
            ) : (
              <ul className="sheet">
                {backlog.items.map((item, index) => (
                  <Row
                    key={item.id}
                    item={item}
                    index={index}
                    canWrite={canWrite}
                    onUp={
                      index > 0
                        ? () =>
                            reorder.mutate({
                              issueId: item.id,
                              afterIssueId:
                                index > 1 ? backlog.items[index - 2].id : null,
                            })
                        : undefined
                    }
                    onDown={
                      index < backlog.items.length - 1
                        ? () =>
                            reorder.mutate({
                              issueId: item.id,
                              afterIssueId: backlog.items[index + 1].id,
                            })
                        : undefined
                    }
                    action={
                      target && canWrite ? (
                        <button
                          type="button"
                          className="btn btn-ghost btn-sm"
                          onClick={() => assign.mutate(item.id)}
                          disabled={assign.isPending}
                        >
                          Add to sprint →
                        </button>
                      ) : undefined
                    }
                  />
                ))}
              </ul>
            )}
            <LoadMore
              hasNext={Boolean(backlog.hasNextPage)}
              loading={backlog.isFetchingNextPage}
              onClick={() => backlog.fetchNextPage()}
            />
          </div>
        </Section>

        <Section
          title="Sprint"
          count={sprintItems.items.length}
          actions={
            openSprints.length > 0 && (
              <select
                className="field h-7 w-auto py-0 text-[12px]"
                value={target?.id ?? ""}
                onChange={(e) => setTarget(e.target.value)}
                aria-label="Choose sprint"
              >
                {openSprints.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            )
          }
        >
          <div className="card overflow-hidden">
            {!scrumBoard ? (
              <Empty
                title="No Scrum board in this project"
                hint="Sprints belong to a Scrum board. Add one to start planning."
                action={
                  canAdmin ? (
                    <Link
                      href={`/p/${projectId}/boards`}
                      className="btn btn-primary btn-sm"
                    >
                      Add a board
                    </Link>
                  ) : undefined
                }
              />
            ) : !target ? (
              <Empty
                title="No sprint to plan"
                hint="Create a sprint, then drag work into it."
                action={
                  canAdmin ? (
                    <Link
                      href={`/p/${projectId}/sprints`}
                      className="btn btn-primary btn-sm"
                    >
                      Create a sprint
                    </Link>
                  ) : undefined
                }
              />
            ) : (
              <>
                <div className="flex items-center justify-between gap-2 border-b border-[var(--color-rule)] px-3 py-2">
                  <Link
                    href={`/p/${projectId}/sprints/${target.id}`}
                    className="text-[13px] font-medium hover:underline"
                  >
                    {target.name}
                  </Link>
                  <div className="flex items-center gap-2">
                    <span className="t-meta text-[11px]">
                      {fullDate(target.plannedStartDateUtc)} →{" "}
                      {fullDate(target.plannedEndDateUtc)}
                    </span>
                    <SprintStatusChip status={target.status} />
                  </div>
                </div>

                {sprintItems.items.length === 0 ? (
                  <Empty
                    title="This sprint is empty"
                    hint="Add issues from the backlog on the left."
                  />
                ) : (
                  <ul className="sheet">
                    {sprintItems.items.map((item, index) => (
                      <Row
                        key={item.id}
                        item={item}
                        index={index}
                        canWrite={canWrite}
                        action={
                          canWrite ? (
                            <button
                              type="button"
                              className="btn btn-bare btn-sm"
                              onClick={() => remove.mutate(item.id)}
                              disabled={remove.isPending}
                            >
                              ← Return
                            </button>
                          ) : undefined
                        }
                      />
                    ))}
                  </ul>
                )}
                <LoadMore
                  hasNext={Boolean(sprintItems.hasNextPage)}
                  loading={sprintItems.isFetchingNextPage}
                  onClick={() => sprintItems.fetchNextPage()}
                />
              </>
            )}
          </div>
        </Section>
      </div>
    </>
  );
}

function Row({
  item,
  index,
  canWrite,
  action,
  onUp,
  onDown,
}: {
  item: BacklogItem;
  index: number;
  canWrite: boolean;
  action?: React.ReactNode;
  onUp?: () => void;
  onDown?: () => void;
}) {
  return (
    <li className="group row-hover flex items-center gap-2.5 px-3 py-2">
      <span className="t-num w-6 shrink-0 text-[11px] text-[var(--color-ink-faint)]">
        {index + 1}
      </span>
      <TypeMark type={item.type} />
      <Link href={`/i/${item.id}`} className="key hover:underline">
        {item.key}
      </Link>
      <span className="min-w-0 flex-1 truncate text-[13px]">{item.title}</span>
      <PriorityMark priority={item.priority} />
      <Avatar user={item.assignee} size={20} />

      <span className="flex w-[124px] shrink-0 justify-end opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
        {canWrite && (onUp || onDown) && (
          <span className="mr-1 flex">
            <button
              type="button"
              className="btn btn-bare btn-sm"
              onClick={onUp}
              disabled={!onUp}
              aria-label="Move up"
            >
              ↑
            </button>
            <button
              type="button"
              className="btn btn-bare btn-sm"
              onClick={onDown}
              disabled={!onDown}
              aria-label="Move down"
            >
              ↓
            </button>
          </span>
        )}
        {action}
      </span>
    </li>
  );
}
