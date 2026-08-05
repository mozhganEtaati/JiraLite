"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useList, useProjectRole } from "@/lib/hooks";
import { fullDate } from "@/lib/format";
import type { BoardItem, SprintItem } from "@/lib/types";
import { SprintStatusChip } from "@/components/marks";
import {
  Empty,
  ErrorNote,
  Field,
  Loading,
  Modal,
  PageHead,
} from "@/components/kit";

export default function SprintsPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const { canAdmin } = useProjectRole(projectId);
  const [creating, setCreating] = useState(false);

  const boards = useList<BoardItem>(
    ["boards", projectId],
    `/api/projects/${projectId}/boards`,
  );
  const scrumBoards = boards.items.filter((b) => b.type === "Scrum");
  const [boardId, setBoardId] = useState<string>("");
  const board = scrumBoards.find((b) => b.id === boardId) ?? scrumBoards[0];

  const sprints = useList<SprintItem>(
    ["sprints", board?.id],
    `/api/boards/${board?.id}/sprints`,
    undefined,
    { enabled: Boolean(board) },
  );

  if (boards.isLoading) return <Loading label="Loading sprints" />;

  if (scrumBoards.length === 0) {
    return (
      <>
        <PageHead eyebrow="Sprints" title="Sprints" />
        <div className="card">
          <Empty
            title="Sprints need a Scrum board"
            hint="Kanban boards run continuously and have no sprints. Add a Scrum board to plan in time boxes."
            action={
              canAdmin ? (
                <Link
                  href={`/p/${projectId}/boards`}
                  className="btn btn-primary btn-sm"
                >
                  Add a Scrum board
                </Link>
              ) : undefined
            }
          />
        </div>
      </>
    );
  }

  const ordered = sprints.items.slice().sort((a, b) => {
    const rank = { Active: 0, Planned: 1, Completed: 2 } as const;
    return (
      rank[a.status] - rank[b.status] ||
      a.plannedStartDateUtc.localeCompare(b.plannedStartDateUtc)
    );
  });

  return (
    <>
      <PageHead
        eyebrow="Sprints"
        title={board?.name ?? "Sprints"}
        meta="One sprint runs at a time. Completing it carries unfinished work forward."
        actions={
          <>
            {scrumBoards.length > 1 && (
              <select
                className="field w-auto"
                value={board?.id}
                onChange={(e) => setBoardId(e.target.value)}
                aria-label="Choose board"
              >
                {scrumBoards.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            )}
            {canAdmin && (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setCreating(true)}
              >
                New sprint
              </button>
            )}
          </>
        }
      />

      <div className="card overflow-hidden">
        {ordered.length === 0 ? (
          <Empty
            title="No sprints yet"
            hint="A sprint is a dated box of work. Create one, fill it from the backlog, then start it."
            action={
              canAdmin ? (
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={() => setCreating(true)}
                >
                  Create the first sprint
                </button>
              ) : undefined
            }
          />
        ) : (
          <ul className="sheet">
            {ordered.map((s) => (
              <li key={s.id}>
                <Link
                  href={`/p/${projectId}/sprints/${s.id}`}
                  className="row-hover flex items-center gap-3 px-3 py-3"
                >
                  <SprintStatusChip status={s.status} />
                  <span className="min-w-0 flex-1 truncate font-medium">
                    {s.name}
                  </span>
                  <span className="t-meta">
                    {fullDate(s.plannedStartDateUtc)} →{" "}
                    {fullDate(s.plannedEndDateUtc)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>

      {board && (
        <CreateSprintDialog
          open={creating}
          onClose={() => setCreating(false)}
          boardId={board.id}
        />
      )}
    </>
  );
}

function CreateSprintDialog({
  open,
  onClose,
  boardId,
}: {
  open: boolean;
  onClose: () => void;
  boardId: string;
}) {
  const qc = useQueryClient();
  const today = new Date().toISOString().slice(0, 10);
  const twoWeeks = new Date(Date.now() + 14 * 864e5).toISOString().slice(0, 10);

  const [name, setName] = useState("");
  const [goal, setGoal] = useState("");
  const [start, setStart] = useState(today);
  const [end, setEnd] = useState(twoWeeks);

  const create = useMutation({
    mutationFn: () =>
      api.post(`/api/boards/${boardId}/sprints`, {
        name,
        goal: goal || undefined,
        plannedStartDateUtc: start,
        plannedEndDateUtc: end,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["sprints"] });
      setName("");
      setGoal("");
      onClose();
    },
  });

  return (
    <Modal open={open} onClose={onClose} title="New sprint">
      <form
        className="space-y-3.5"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <Field label="Name">
          <input
            className="field"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Sprint 15"
            required
          />
        </Field>
        <Field label="Goal" hint="One sentence the team can hold themselves to.">
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
              required
            />
          </Field>
          <Field label="Ends">
            <input
              className="field"
              type="date"
              value={end}
              onChange={(e) => setEnd(e.target.value)}
              required
            />
          </Field>
        </div>
        <ErrorNote error={create.error} />
        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={create.isPending || !name.trim()}
          >
            {create.isPending ? "Creating…" : "Create sprint"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
