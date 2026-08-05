"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useProjectRole } from "@/lib/hooks";
import type { Board, BoardColumn } from "@/lib/types";
import {
  Confirm,
  ErrorNote,
  Field,
  Loading,
  PageHead,
} from "@/components/kit";

export default function ColumnsPage() {
  const { projectId, boardId } = useParams<{
    projectId: string;
    boardId: string;
  }>();
  const qc = useQueryClient();
  const { canAdmin } = useProjectRole(projectId);
  const [deleting, setDeleting] = useState<BoardColumn | null>(null);
  const [name, setName] = useState("");

  const { data: board, isLoading } = useQuery({
    queryKey: ["board", boardId],
    queryFn: () => api.get<Board>(`/api/boards/${boardId}`),
  });
  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["board", boardId] });
    qc.invalidateQueries({ queryKey: ["board-issues", boardId] });
  };

  const add = useMutation({
    mutationFn: () =>
      api.post(`/api/boards/${boardId}/columns`, {
        name,
        isDefault: false,
        isDoneColumn: false,
      }),
    onSuccess: () => {
      setName("");
      invalidate();
    },
  });

  const edit = useMutation({
    mutationFn: (v: { id: string; patch: Record<string, unknown> }) =>
      api.patch(`/api/boards/${boardId}/columns/${v.id}`, v.patch),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (id: string) =>
      api.del(`/api/boards/${boardId}/columns/${id}`),
    onSuccess: () => {
      setDeleting(null);
      invalidate();
    },
  });

  /** Reorder sends the whole order back, each row with its own version. */
  const reorder = useMutation({
    mutationFn: (columns: BoardColumn[]) =>
      api.patch(`/api/boards/${boardId}/columns/reorder`, {
        columns: columns.map((c) => ({
          columnId: c.id,
          rowVersion: c.rowVersion,
        })),
      }),
    onSuccess: invalidate,
  });

  if (isLoading || !board) return <Loading label="Loading columns" />;

  const ordered = board.columns
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder);

  const swap = (from: number, to: number) => {
    const next = ordered.slice();
    const [moved] = next.splice(from, 1);
    next.splice(to, 0, moved);
    reorder.mutate(next);
  };

  return (
    <>
      <PageHead
        eyebrow={`Board · ${board.name}`}
        title="Columns"
        meta="A column is the issue's status. The done column is what “finished” means when a sprint completes."
        actions={
          <Link
            href={`/p/${projectId}/board?board=${boardId}`}
            className="btn btn-ghost"
          >
            Back to board
          </Link>
        }
      />

      <ErrorNote
        error={add.error ?? edit.error ?? remove.error ?? reorder.error}
        className="mb-3"
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.5fr)_minmax(240px,1fr)]">
        <div className="card overflow-hidden">
          <ul className="sheet">
            {ordered.map((c, i) => (
              <li key={c.id} className="flex flex-wrap items-center gap-3 px-3 py-2.5">
                <span className="t-num w-5 text-[11px] text-[var(--color-ink-faint)]">
                  {i + 1}
                </span>
                <span className="min-w-0 flex-1 truncate font-medium">
                  {c.name}
                </span>

                <label className="flex items-center gap-1.5 text-[12px] text-[var(--color-ink-soft)]">
                  <input
                    type="checkbox"
                    className="accent-[var(--color-blue)]"
                    checked={c.isDefault}
                    disabled={!canAdmin}
                    onChange={(e) =>
                      edit.mutate({
                        id: c.id,
                        patch: { isDefault: e.target.checked },
                      })
                    }
                  />
                  New issues land here
                </label>

                <label className="flex items-center gap-1.5 text-[12px] text-[var(--color-ink-soft)]">
                  <input
                    type="checkbox"
                    className="accent-[var(--color-pink)]"
                    checked={c.isDoneColumn}
                    disabled={!canAdmin}
                    onChange={(e) =>
                      edit.mutate({
                        id: c.id,
                        patch: { isDoneColumn: e.target.checked },
                      })
                    }
                  />
                  Counts as done
                </label>

                {canAdmin && (
                  <span className="flex">
                    <button
                      type="button"
                      className="btn btn-bare btn-sm"
                      onClick={() => swap(i, i - 1)}
                      disabled={i === 0 || reorder.isPending}
                      aria-label={`Move ${c.name} left`}
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      className="btn btn-bare btn-sm"
                      onClick={() => swap(i, i + 1)}
                      disabled={i === ordered.length - 1 || reorder.isPending}
                      aria-label={`Move ${c.name} right`}
                    >
                      ↓
                    </button>
                    <button
                      type="button"
                      className="btn btn-bare btn-sm"
                      onClick={() => setDeleting(c)}
                    >
                      Delete
                    </button>
                  </span>
                )}
              </li>
            ))}
          </ul>
        </div>

        {canAdmin && (
          <form
            className="card h-fit space-y-3.5 p-4"
            onSubmit={(e) => {
              e.preventDefault();
              add.mutate();
            }}
          >
            <h2 className="t-eyebrow">Add a column</h2>
            <Field label="Name">
              <input
                className="field"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="In Review"
                required
              />
            </Field>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={add.isPending || !name.trim()}
            >
              {add.isPending ? "Adding…" : "Add column"}
            </button>
          </form>
        )}
      </div>

      <Confirm
        open={Boolean(deleting)}
        title={`Delete ${deleting?.name ?? "column"}?`}
        body="Move any issues out of this column first, or the delete is rejected."
        confirmLabel="Delete column"
        pending={remove.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      />
    </>
  );
}
