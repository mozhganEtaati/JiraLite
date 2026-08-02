"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useList, useProjectRole } from "@/lib/hooks";
import type { Label } from "@/lib/types";
import { LabelChip } from "@/components/marks";
import {
  SettingsTabs,
  projectSettingsTabs,
} from "@/components/settings-tabs";
import { Empty, ErrorNote, Field, Loading, PageHead } from "@/components/ui";

const SWATCHES = [
  "#253C78",
  "#4B5F99",
  "#FF4F8B",
  "#5B2E6E",
  "#1F6F5C",
  "#A35E12",
  "#A32020",
  "#6B6A66",
];

export default function LabelsPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const qc = useQueryClient();
  const { canWrite } = useProjectRole(projectId);

  const labels = useList<Label>(
    ["labels", projectId],
    `/api/projects/${projectId}/labels`,
  );
  const invalidate = () =>
    qc.invalidateQueries({ queryKey: ["labels", projectId] });

  const [name, setName] = useState("");
  const [color, setColor] = useState(SWATCHES[0]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editColor, setEditColor] = useState(SWATCHES[0]);

  const create = useMutation({
    mutationFn: () =>
      api.post(`/api/projects/${projectId}/labels`, { name, color }),
    onSuccess: () => {
      setName("");
      invalidate();
    },
  });

  const save = useMutation({
    mutationFn: () =>
      api.patch(`/api/labels/${editingId}`, {
        name: editName,
        color: editColor,
      }),
    onSuccess: () => {
      setEditingId(null);
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/labels/${id}`),
    onSuccess: invalidate,
  });

  return (
    <>
      <PageHead
        eyebrow="Settings · Labels"
        title="Labels"
        meta="Labels are shared across the whole project. Deleting one removes it from every issue."
      />

      <SettingsTabs tabs={projectSettingsTabs(projectId)} />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.4fr)_minmax(260px,1fr)]">
        <div className="card overflow-hidden">
          {labels.isLoading ? (
            <Loading />
          ) : labels.items.length === 0 ? (
            <Empty
              title="No labels yet"
              hint="Labels group issues across boards and sprints — “billing”, “tech-debt”, that sort of thing."
            />
          ) : (
            <ul className="sheet">
              {labels.items.map((l) => (
                <li key={l.id} className="group px-3 py-2.5">
                  {editingId === l.id ? (
                    <form
                      className="flex flex-wrap items-end gap-2"
                      onSubmit={(e) => {
                        e.preventDefault();
                        save.mutate();
                      }}
                    >
                      <input
                        className="field w-auto flex-1"
                        value={editName}
                        onChange={(e) => setEditName(e.target.value)}
                      />
                      <Swatches value={editColor} onChange={setEditColor} />
                      <button type="submit" className="btn btn-primary btn-sm">
                        Save
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        onClick={() => setEditingId(null)}
                      >
                        Cancel
                      </button>
                    </form>
                  ) : (
                    <div className="flex items-center gap-3">
                      <LabelChip name={l.name} color={l.color} />
                      <span className="t-meta flex-1">{l.color}</span>
                      {canWrite && (
                        <span className="flex opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                          <button
                            type="button"
                            className="btn btn-bare btn-sm"
                            onClick={() => {
                              setEditingId(l.id);
                              setEditName(l.name);
                              setEditColor(l.color);
                            }}
                          >
                            Rename
                          </button>
                          <button
                            type="button"
                            className="btn btn-bare btn-sm"
                            onClick={() => remove.mutate(l.id)}
                          >
                            Delete
                          </button>
                        </span>
                      )}
                    </div>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        {canWrite && (
          <form
            className="card h-fit space-y-3.5 p-4"
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate();
            }}
          >
            <h2 className="t-eyebrow">New label</h2>
            <Field label="Name">
              <input
                className="field"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="billing"
                required
                maxLength={40}
              />
            </Field>
            <Field label="Colour">
              <Swatches value={color} onChange={setColor} />
            </Field>
            <ErrorNote error={create.error ?? save.error ?? remove.error} />
            <button
              type="submit"
              className="btn btn-primary"
              disabled={create.isPending || !name.trim()}
            >
              {create.isPending ? "Creating…" : "Create label"}
            </button>
          </form>
        )}
      </div>
    </>
  );
}

function Swatches({
  value,
  onChange,
}: {
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {SWATCHES.map((c) => (
        <button
          key={c}
          type="button"
          onClick={() => onChange(c)}
          aria-label={c}
          aria-pressed={value === c}
          style={{
            width: 22,
            height: 22,
            background: c,
            borderRadius: 2,
            outline:
              value === c ? "2px solid var(--color-ink)" : "1px solid transparent",
            outlineOffset: 2,
          }}
        />
      ))}
    </div>
  );
}
