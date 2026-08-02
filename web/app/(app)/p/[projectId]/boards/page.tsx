"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useList, useProjectRole } from "@/lib/hooks";
import type { BoardItem, BoardTypeName } from "@/lib/types";
import {
  SettingsTabs,
  projectSettingsTabs,
} from "@/components/settings-tabs";
import {
  Confirm,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Modal,
  PageHead,
} from "@/components/ui";

export default function BoardsPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const qc = useQueryClient();
  const { canAdmin } = useProjectRole(projectId);
  const [creating, setCreating] = useState(false);
  const [renaming, setRenaming] = useState<BoardItem | null>(null);
  const [deleting, setDeleting] = useState<BoardItem | null>(null);
  const [newName, setNewName] = useState("");

  const boards = useList<BoardItem>(
    ["boards", projectId],
    `/api/projects/${projectId}/boards`,
  );
  const invalidate = () =>
    qc.invalidateQueries({ queryKey: ["boards", projectId] });

  const rename = useMutation({
    mutationFn: () =>
      api.patch(`/api/boards/${renaming!.id}`, { name: newName }),
    onSuccess: () => {
      setRenaming(null);
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: () => api.del(`/api/boards/${deleting!.id}`),
    onSuccess: () => {
      setDeleting(null);
      invalidate();
    },
  });

  return (
    <>
      <PageHead
        eyebrow="Settings · Boards"
        title="Boards"
        meta="A Kanban board runs continuously. A Scrum board is the only place sprints can live."
        actions={
          canAdmin && (
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setCreating(true)}
            >
              New board
            </button>
          )
        }
      />

      <SettingsTabs tabs={projectSettingsTabs(projectId)} />

      <ErrorNote error={rename.error ?? remove.error} className="mb-3" />

      <div className="card overflow-hidden">
        {boards.isLoading ? (
          <Loading />
        ) : boards.items.length === 0 ? (
          <Empty
            title="No boards"
            hint="Issues need a board to sit on."
            action={
              canAdmin ? (
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={() => setCreating(true)}
                >
                  Create a board
                </button>
              ) : undefined
            }
          />
        ) : (
          <ul className="sheet">
            {boards.items.map((b) => (
              <li
                key={b.id}
                className="group flex items-center gap-3 px-3 py-3"
              >
                <Link
                  href={`/p/${projectId}/board?board=${b.id}`}
                  className="min-w-0 flex-1 truncate font-medium hover:underline"
                >
                  {b.name}
                </Link>
                <span className="chip">{b.type}</span>
                <Link
                  href={`/p/${projectId}/boards/${b.id}/columns`}
                  className="btn btn-bare btn-sm"
                >
                  Columns
                </Link>
                {canAdmin && (
                  <span className="flex opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                    <button
                      type="button"
                      className="btn btn-bare btn-sm"
                      onClick={() => {
                        setRenaming(b);
                        setNewName(b.name);
                      }}
                    >
                      Rename
                    </button>
                    <button
                      type="button"
                      className="btn btn-bare btn-sm"
                      onClick={() => setDeleting(b)}
                    >
                      Delete
                    </button>
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <CreateBoardDialog
        open={creating}
        onClose={() => setCreating(false)}
        projectId={projectId}
      />

      <Modal
        open={Boolean(renaming)}
        onClose={() => setRenaming(null)}
        title="Rename board"
        width={400}
      >
        <form
          className="space-y-3.5"
          onSubmit={(e) => {
            e.preventDefault();
            rename.mutate();
          }}
        >
          <Field label="Name">
            <input
              className="field"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              required
            />
          </Field>
          <div className="flex justify-end gap-2">
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => setRenaming(null)}
            >
              Cancel
            </button>
            <button type="submit" className="btn btn-primary">
              Save
            </button>
          </div>
        </form>
      </Modal>

      <Confirm
        open={Boolean(deleting)}
        title={`Delete ${deleting?.name ?? "board"}?`}
        body="The board and its columns are removed. Issues on it must be moved first."
        confirmLabel="Delete board"
        pending={remove.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => remove.mutate()}
      />
    </>
  );
}

function CreateBoardDialog({
  open,
  onClose,
  projectId,
}: {
  open: boolean;
  onClose: () => void;
  projectId: string;
}) {
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [type, setType] = useState<BoardTypeName>("Kanban");

  const create = useMutation({
    mutationFn: () =>
      api.post(`/api/projects/${projectId}/boards`, { name, type }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["boards", projectId] });
      setName("");
      onClose();
    },
  });

  return (
    <Modal open={open} onClose={onClose} title="New board">
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
            placeholder="Delivery"
            required
          />
        </Field>
        <div>
          <span className="label">Type</span>
          <div className="grid gap-2 sm:grid-cols-2">
            {(
              [
                ["Kanban", "Continuous flow. No sprints."],
                ["Scrum", "Time-boxed sprints you start and complete."],
              ] as [BoardTypeName, string][]
            ).map(([t, hint]) => (
              <button
                key={t}
                type="button"
                onClick={() => setType(t)}
                aria-pressed={type === t}
                className="plate p-2.5 text-left"
                data-slip={type === t ? "true" : undefined}
              >
                <span className="block text-[13px] font-medium">{t}</span>
                <span className="block text-[12px] text-[var(--color-ink-soft)]">
                  {hint}
                </span>
              </button>
            ))}
          </div>
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
            {create.isPending ? "Creating…" : "Create board"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
