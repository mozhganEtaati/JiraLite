"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "@/lib/api";
import { useList } from "@/lib/hooks";
import {
  ISSUE_TYPES,
  PRIORITIES,
  type IssueTypeName,
  type PriorityName,
  type ProjectMember,
} from "@/lib/types";
import { ErrorNote, Field, Modal } from "@/components/kit";
import { TypeMark } from "@/components/marks";

export function NewIssueButton({
  projectId,
  label = "New issue",
  sprintId,
}: {
  projectId: string;
  label?: string;
  sprintId?: string;
}) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button
        type="button"
        className="btn btn-primary"
        onClick={() => setOpen(true)}
      >
        {label}
      </button>
      <NewIssueDialog
        open={open}
        onClose={() => setOpen(false)}
        projectId={projectId}
        sprintId={sprintId}
      />
    </>
  );
}

export function NewIssueDialog({
  open,
  onClose,
  projectId,
  sprintId,
}: {
  open: boolean;
  onClose: () => void;
  projectId: string;
  sprintId?: string;
}) {
  const qc = useQueryClient();
  const members = useList<ProjectMember>(
    ["project-members", projectId],
    `/api/projects/${projectId}/members`,
    undefined,
    { enabled: open },
  );

  const [type, setType] = useState<IssueTypeName>("Task");
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [priority, setPriority] = useState<PriorityName>("Medium");
  const [assigneeUserId, setAssignee] = useState("");
  const [dueDateUtc, setDue] = useState("");
  const [estimate, setEstimate] = useState("");

  const create = useMutation({
    mutationFn: async () => {
      const issue = await api.post<{ id: string }>(
        `/api/projects/${projectId}/issues`,
        {
          type,
          title,
          description: description || undefined,
          priority,
          assigneeUserId: assigneeUserId || undefined,
          dueDateUtc: dueDateUtc || undefined,
          estimate: estimate ? Number(estimate) : undefined,
        },
      );
      if (sprintId) {
        await api.post(`/api/sprints/${sprintId}/issues`, { issueId: issue.id });
      }
      return issue;
    },
    onSuccess: () => {
      qc.invalidateQueries();
      setTitle("");
      setDescription("");
      setEstimate("");
      onClose();
    },
  });

  return (
    <Modal open={open} onClose={onClose} title="New issue" width={520}>
      <form
        className="space-y-3.5"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <div>
          <span className="label">Type</span>
          <div className="flex flex-wrap gap-1.5">
            {ISSUE_TYPES.map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => setType(t)}
                aria-pressed={type === t}
                className={
                  type === t
                    ? "chip chip-ink h-8 gap-2 px-2.5"
                    : "chip h-8 gap-2 px-2.5"
                }
              >
                <TypeMark type={t} size={11} />
                {t}
              </button>
            ))}
          </div>
        </div>

        <Field label="Title" error={create.error ? undefined : undefined}>
          <input
            className="field"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            required
            maxLength={300}
            placeholder="What needs to happen?"
          />
        </Field>

        <Field label="Description">
          <textarea
            className="field"
            rows={3}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </Field>

        <div className="grid grid-cols-2 gap-3">
          <Field label="Priority">
            <select
              className="field"
              value={priority}
              onChange={(e) => setPriority(e.target.value as PriorityName)}
            >
              {PRIORITIES.map((p) => (
                <option key={p}>{p}</option>
              ))}
            </select>
          </Field>

          <Field label="Assignee">
            <select
              className="field"
              value={assigneeUserId}
              onChange={(e) => setAssignee(e.target.value)}
            >
              <option value="">Nobody yet</option>
              {members.items.map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.displayName}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Due date">
            <input
              className="field"
              type="date"
              value={dueDateUtc}
              onChange={(e) => setDue(e.target.value)}
            />
          </Field>

          <Field label="Estimate" hint="Story points.">
            <input
              className="field"
              type="number"
              min="0"
              step="0.5"
              value={estimate}
              onChange={(e) => setEstimate(e.target.value)}
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
            disabled={create.isPending || !title.trim()}
          >
            {create.isPending ? "Creating…" : "Create issue"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
