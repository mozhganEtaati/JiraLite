"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useCursorList, useList, useProjectRole } from "@/lib/hooks";
import { fullDate } from "@/lib/format";
import type { BacklogItem, BoardItem, Sprint, SprintItem } from "@/lib/types";
import {
  Avatar,
  IssueKey,
  PriorityMark,
  SprintStatusChip,
  TypeMark,
} from "@/components/marks";
import {
  Confirm,
  Empty,
  ErrorNote,
  Field,
  Loading,
  LoadMore,
  Modal,
  PageHead,
} from "@/components/kit";

export default function SprintPage() {
  const { projectId, sprintId } = useParams<{
    projectId: string;
    sprintId: string;
  }>();
  const qc = useQueryClient();
  const router = useRouter();
  const { canWrite, canAdmin } = useProjectRole(projectId);

  const [editing, setEditing] = useState(false);
  const [completing, setCompleting] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [carryTo, setCarryTo] = useState("");

  const { data: sprint, isLoading } = useQuery({
    queryKey: ["sprint", sprintId],
    queryFn: () => api.get<Sprint>(`/api/sprints/${sprintId}`),
  });

  const issues = useCursorList<BacklogItem>(
    ["backlog", "sprint", sprintId],
    `/api/sprints/${sprintId}/backlog`,
    {},
    { limit: 50 },
  );

  const boards = useList<BoardItem>(
    ["boards", projectId],
    `/api/projects/${projectId}/boards`,
  );
  const siblings = useList<SprintItem>(
    ["sprints", sprint?.boardId],
    `/api/boards/${sprint?.boardId}/sprints`,
    undefined,
    { enabled: Boolean(sprint?.boardId) },
  );

  const start = useMutation({
    mutationFn: () => api.post(`/api/sprints/${sprintId}/start`),
    onSuccess: () => qc.invalidateQueries(),
  });

  const complete = useMutation({
    mutationFn: () =>
      api.post(`/api/sprints/${sprintId}/complete`, {
        moveIncompleteIssuesToSprintId: carryTo || undefined,
      }),
    onSuccess: () => {
      qc.invalidateQueries();
      setCompleting(false);
    },
  });

  const remove = useMutation({
    mutationFn: () => api.del(`/api/sprints/${sprintId}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sprints"] });
      router.replace(`/p/${projectId}/sprints`);
    },
  });

  const removeIssue = useMutation({
    mutationFn: (issueId: string) =>
      api.del(`/api/sprints/${sprintId}/issues/${issueId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["backlog"] }),
  });

  if (isLoading || !sprint) return <Loading label="Loading sprint" />;

  const board = boards.items.find((b) => b.id === sprint.boardId);
  const carryCandidates = siblings.items.filter(
    (s) => s.id !== sprintId && s.status !== "Completed",
  );

  return (
    <>
      <PageHead
        eyebrow={`Sprint · ${board?.name ?? "board"}`}
        title={sprint.name}
        meta={
          <>
            {fullDate(sprint.plannedStartDateUtc)} →{" "}
            {fullDate(sprint.plannedEndDateUtc)}
            {sprint.startedAtUtc && ` · started ${fullDate(sprint.startedAtUtc)}`}
            {sprint.completedAtUtc &&
              ` · completed ${fullDate(sprint.completedAtUtc)}`}
          </>
        }
        actions={
          <>
            <SprintStatusChip status={sprint.status} />
            {canAdmin && sprint.status === "Planned" && (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => start.mutate()}
                disabled={start.isPending}
              >
                {start.isPending ? "Starting…" : "Start sprint"}
              </button>
            )}
            {canAdmin && sprint.status === "Active" && (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setCompleting(true)}
              >
                Complete sprint
              </button>
            )}
            {canAdmin && (
              <button
                type="button"
                className="btn btn-ghost"
                onClick={() => setEditing(true)}
              >
                Edit
              </button>
            )}
            {canAdmin && sprint.status === "Planned" && (
              <button
                type="button"
                className="btn btn-danger"
                onClick={() => setDeleting(true)}
              >
                Delete
              </button>
            )}
          </>
        }
      />

      <ErrorNote error={start.error ?? remove.error} className="mb-3" />

      {sprint.goal && (
        <div
          className="mb-5 border-l-[3px] px-4 py-3"
          style={{
            borderColor: "var(--color-pink)",
            background: "var(--color-surface)",
          }}
        >
          <div className="t-eyebrow mb-1">Goal</div>
          <p className="text-[14px]">{sprint.goal}</p>
        </div>
      )}

      <div className="card overflow-hidden">
        <div className="flex items-center justify-between border-b border-[var(--color-rule)] px-3 py-2">
          <h2 className="t-eyebrow">
            Issues in this sprint{" "}
            <span className="t-num text-[11px]">{issues.items.length}</span>
          </h2>
          <Link href={`/p/${projectId}/backlog`} className="btn btn-bare btn-sm">
            Plan from backlog
          </Link>
        </div>

        {issues.items.length === 0 ? (
          <Empty
            title="Nothing in this sprint yet"
            hint="Pull issues in from the product backlog."
            action={
              <Link
                href={`/p/${projectId}/backlog`}
                className="btn btn-primary btn-sm"
              >
                Open the backlog
              </Link>
            }
          />
        ) : (
          <ul className="sheet">
            {issues.items.map((i) => (
              <li
                key={i.id}
                className="group row-hover flex items-center gap-2.5 px-3 py-2"
              >
                <TypeMark type={i.type} />
                <Link href={`/i/${i.id}`} className="key hover:underline">
                  {i.key}
                </Link>
                <span className="min-w-0 flex-1 truncate text-[13px]">
                  {i.title}
                </span>
                <PriorityMark priority={i.priority} />
                <Avatar user={i.assignee} size={20} />
                {canWrite && (
                  <button
                    type="button"
                    className="btn btn-bare btn-sm opacity-0 transition-opacity group-hover:opacity-100 focus:opacity-100"
                    onClick={() => removeIssue.mutate(i.id)}
                  >
                    Remove
                  </button>
                )}
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

      <EditSprintDialog
        open={editing}
        onClose={() => setEditing(false)}
        sprint={sprint}
      />

      <Modal
        open={completing}
        onClose={() => setCompleting(false)}
        title="Complete sprint"
        width={440}
      >
        <p className="text-[13px] text-[var(--color-ink-soft)]">
          Issues that are not in a done column move to the sprint you pick, or
          back to the product backlog.
        </p>
        <div className="mt-4">
          <Field label="Move unfinished work to">
            <select
              className="field"
              value={carryTo}
              onChange={(e) => setCarryTo(e.target.value)}
            >
              <option value="">The product backlog</option>
              {carryCandidates.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <ErrorNote error={complete.error} className="mt-3" />
        <div className="mt-4 flex justify-end gap-2">
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => setCompleting(false)}
          >
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => complete.mutate()}
            disabled={complete.isPending}
          >
            {complete.isPending ? "Completing…" : "Complete sprint"}
          </button>
        </div>
      </Modal>

      <Confirm
        open={deleting}
        title="Delete this sprint?"
        body="The sprint is removed and its issues return to the product backlog."
        confirmLabel="Delete sprint"
        pending={remove.isPending}
        onCancel={() => setDeleting(false)}
        onConfirm={() => remove.mutate()}
      />
    </>
  );
}

function EditSprintDialog({
  open,
  onClose,
  sprint,
}: {
  open: boolean;
  onClose: () => void;
  sprint: Sprint;
}) {
  const qc = useQueryClient();
  const [name, setName] = useState(sprint.name);
  const [goal, setGoal] = useState(sprint.goal ?? "");
  const [start, setStart] = useState(sprint.plannedStartDateUtc.slice(0, 10));
  const [end, setEnd] = useState(sprint.plannedEndDateUtc.slice(0, 10));

  const save = useMutation({
    mutationFn: () =>
      api.patch(`/api/sprints/${sprint.id}`, {
        name,
        goal: goal || undefined,
        plannedStartDateUtc: start,
        plannedEndDateUtc: end,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sprint", sprint.id] });
      qc.invalidateQueries({ queryKey: ["sprints"] });
      onClose();
    },
  });

  return (
    <Modal open={open} onClose={onClose} title="Edit sprint">
      <form
        className="space-y-3.5"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        <Field label="Name">
          <input
            className="field"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
          />
        </Field>
        <Field label="Goal">
          <textarea
            className="field"
            rows={2}
            value={goal}
            onChange={(e) => setGoal(e.target.value)}
          />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Starts">
            <input
              className="field"
              type="date"
              value={start}
              onChange={(e) => setStart(e.target.value)}
            />
          </Field>
          <Field label="Ends">
            <input
              className="field"
              type="date"
              value={end}
              onChange={(e) => setEnd(e.target.value)}
            />
          </Field>
        </div>
        <ErrorNote error={save.error} />
        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={save.isPending}
          >
            {save.isPending ? "Saving…" : "Save changes"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
