"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { api, fetchBlobUrl } from "@/lib/api";
import { useCursorList, useList, useProjectRole } from "@/lib/hooks";
import { ago, cx, fileSize, fullDate } from "@/lib/format";
import { useSession } from "@/lib/providers";
import { PRIORITIES } from "@/lib/types";
import type {
  Attachment,
  BoardItem,
  Comment,
  Issue,
  Label,
  PriorityName,
  ProjectMember,
  Subtask,
} from "@/lib/types";
import {
  Avatar,
  DueDate,
  IssueKey,
  LabelChip,
  PriorityMark,
  TypeMark,
} from "@/components/marks";
import {
  Confirm,
  Empty,
  ErrorNote,
  Field,
  Loading,
  LoadMore,
  PageHead,
  Section,
} from "@/components/kit";

export default function IssuePage() {
  const { issueId } = useParams<{ issueId: string }>();
  const qc = useQueryClient();
  const router = useRouter();
  const { me } = useSession();

  const { data: issue, isLoading, error } = useQuery({
    queryKey: ["issue", issueId],
    queryFn: () => api.get<Issue>(`/api/issues/${issueId}`),
  });

  // The issue payload carries no projectId, and no endpoint maps one back.
  // The issue key is prefixed with the project key, so match on that.
  const myProjects = useCursorList<{ id: string; key: string }>(
    ["my-projects"],
    "/api/dashboard/my-projects",
    {},
    { limit: 50 },
  );
  const projectKey = issue?.key.split("-")[0];
  const projectId =
    myProjects.items.find((p) => p.key === projectKey)?.id ?? null;

  if (isLoading) return <Loading label="Opening issue" />;
  if (error) return <ErrorNote error={error} />;
  if (!issue) return null;

  return (
    <IssueDetail
      issue={issue}
      projectId={projectId ?? null}
      meId={me?.id}
      onDeleted={() => {
        qc.invalidateQueries();
        router.back();
      }}
    />
  );
}

function IssueDetail({
  issue,
  projectId,
  meId,
  onDeleted,
}: {
  issue: Issue;
  projectId: string | null;
  meId?: string;
  onDeleted: () => void;
}) {
  const qc = useQueryClient();
  const { canWrite } = useProjectRole(projectId ?? undefined);
  const refresh = () => qc.invalidateQueries({ queryKey: ["issue", issue.id] });

  const [deleting, setDeleting] = useState(false);
  const [title, setTitle] = useState(issue.title);
  const [description, setDescription] = useState(issue.description ?? "");
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    setTitle(issue.title);
    setDescription(issue.description ?? "");
    setDirty(false);
  }, [issue.id, issue.title, issue.description]);

  const save = useMutation({
    mutationFn: (patch: Record<string, unknown>) =>
      api.patch(`/api/issues/${issue.id}`, patch),
    onSuccess: () => {
      refresh();
      qc.invalidateQueries({ queryKey: ["board-issues"] });
      setDirty(false);
    },
  });

  const remove = useMutation({
    mutationFn: () => api.del(`/api/issues/${issue.id}`),
    onSuccess: onDeleted,
  });

  const subtasks = useList<Subtask>(
    ["subtasks", issue.id],
    `/api/issues/${issue.id}/subtasks`,
  );

  const members = useList<ProjectMember>(
    ["project-members", projectId],
    `/api/projects/${projectId}/members`,
    undefined,
    { enabled: Boolean(projectId) },
  );

  return (
    <>
      <PageHead
        eyebrow={
          <span className="flex items-center gap-2">
            <TypeMark type={issue.type} size={11} />
            {issue.type}
            {projectId && (
              <>
                {" · "}
                <Link
                  href={`/p/${projectId}/issues`}
                  className="underline decoration-[var(--color-pink)] underline-offset-2"
                >
                  all issues
                </Link>
              </>
            )}
          </span>
        }
        title={
          <span className="flex items-center gap-2.5">
            <IssueKey issueKey={issue.key} />
            <span className="truncate">{issue.title}</span>
          </span>
        }
        actions={
          canWrite && (
            <button
              type="button"
              className="btn btn-danger"
              onClick={() => setDeleting(true)}
            >
              Delete
            </button>
          )
        }
      />

      <ErrorNote error={save.error ?? remove.error} className="mb-3" />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.7fr)_minmax(260px,1fr)]">
        <div className="space-y-6">
          {canWrite && (
            <Section title="Title">
              <input
                className="field"
                value={title}
                onChange={(e) => {
                  setTitle(e.target.value);
                  setDirty(true);
                }}
              />
            </Section>
          )}

          <Section
            title="Description"
            actions={
              dirty &&
              canWrite && (
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={() => save.mutate({ title, description })}
                  disabled={save.isPending}
                >
                  {save.isPending ? "Saving…" : "Save"}
                </button>
              )
            }
          >
            {canWrite ? (
              <textarea
                className="field"
                rows={7}
                value={description}
                placeholder="What is the context? How would someone know this is done?"
                onChange={(e) => {
                  setDescription(e.target.value);
                  setDirty(true);
                }}
              />
            ) : (
              <div className="card p-3 text-[14px] whitespace-pre-wrap">
                {issue.description || (
                  <span className="text-[var(--color-ink-faint)]">
                    No description.
                  </span>
                )}
              </div>
            )}
          </Section>

          {subtasks.items.length > 0 && (
            <Section title="Subtasks" count={subtasks.items.length}>
              <div className="card overflow-hidden">
                <ul className="sheet">
                  {subtasks.items.map((s) => (
                    <li key={s.id}>
                      <Link
                        href={`/i/${s.id}`}
                        className="row-hover flex items-center gap-2.5 px-3 py-2"
                      >
                        <TypeMark type="Subtask" />
                        <IssueKey issueKey={s.key} />
                        <span className="min-w-0 flex-1 truncate text-[13px]">
                          {s.title}
                        </span>
                        <PriorityMark priority={s.priority} />
                        <Avatar user={s.assignee} size={20} />
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            </Section>
          )}

          <Attachments issueId={issue.id} canWrite={canWrite} />
          <Comments issueId={issue.id} canWrite={canWrite} meId={meId} />
        </div>

        <aside className="space-y-4">
          <div className="card divide-y divide-[var(--color-rule-soft)]">
            <Row label="Status">
              <span className="chip">Column set on the board</span>
            </Row>

            <Row label="Assignee">
              {canWrite ? (
                <select
                  className="field h-8 py-0 text-[13px]"
                  value={issue.assignee?.id ?? ""}
                  onChange={(e) =>
                    save.mutate({ assigneeUserId: e.target.value || null })
                  }
                >
                  <option value="">Nobody</option>
                  {members.items.map((m) => (
                    <option key={m.userId} value={m.userId}>
                      {m.displayName}
                    </option>
                  ))}
                </select>
              ) : (
                <span className="flex items-center gap-2">
                  <Avatar user={issue.assignee} size={20} />
                  {issue.assignee?.displayName ?? "Nobody"}
                </span>
              )}
            </Row>

            <Row label="Reporter">
              <span className="flex items-center gap-2 text-[13px]">
                <Avatar user={issue.reporter} size={20} />
                {issue.reporter.displayName}
              </span>
            </Row>

            <Row label="Priority">
              {canWrite ? (
                <select
                  className="field h-8 py-0 text-[13px]"
                  value={issue.priority}
                  onChange={(e) =>
                    save.mutate({ priority: e.target.value as PriorityName })
                  }
                >
                  {PRIORITIES.map((p) => (
                    <option key={p}>{p}</option>
                  ))}
                </select>
              ) : (
                <span className="flex items-center gap-2 text-[13px]">
                  <PriorityMark priority={issue.priority} />
                  {issue.priority}
                </span>
              )}
            </Row>

            <Row label="Due">
              {canWrite ? (
                <input
                  type="date"
                  className="field h-8 py-0 text-[13px]"
                  value={issue.dueDateUtc?.slice(0, 10) ?? ""}
                  onChange={(e) =>
                    save.mutate({ dueDateUtc: e.target.value || null })
                  }
                />
              ) : (
                <DueDate value={issue.dueDateUtc} />
              )}
            </Row>

            <Row label="Estimate">
              <span className="t-num text-[13px]">
                {issue.estimate ?? "—"}
                {issue.estimate ? " pts" : ""}
              </span>
            </Row>

            <Row label="Sprint">
              <span className="t-meta">
                {issue.sprintId ? "In a sprint" : "Product backlog"}
              </span>
            </Row>

            <Row label="Rank">
              <span className="t-meta truncate" title={issue.rank}>
                {issue.rank}
              </span>
            </Row>
          </div>

          <IssueLabels
            issueId={issue.id}
            projectId={projectId}
            attached={issue.labels}
            canWrite={canWrite}
            onChange={refresh}
          />
        </aside>
      </div>

      <Confirm
        open={deleting}
        title={`Delete ${issue.key}?`}
        body="The issue and its comments and attachments are removed. This cannot be undone."
        confirmLabel="Delete issue"
        pending={remove.isPending}
        onCancel={() => setDeleting(false)}
        onConfirm={() => remove.mutate()}
      />
    </>
  );
}

function Row({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex items-center justify-between gap-3 px-3 py-2">
      <span className="t-eyebrow shrink-0 text-[10px]">{label}</span>
      <span className="min-w-0 text-right">{children}</span>
    </div>
  );
}

/* ── labels ───────────────────────────────────────────────── */

function IssueLabels({
  issueId,
  projectId,
  attached,
  canWrite,
  onChange,
}: {
  issueId: string;
  projectId: string | null;
  attached: Label[];
  canWrite: boolean;
  onChange: () => void;
}) {
  const all = useList<Label>(
    ["labels", projectId],
    `/api/projects/${projectId}/labels`,
    undefined,
    { enabled: Boolean(projectId) },
  );

  const attach = useMutation({
    mutationFn: (labelId: string) =>
      api.post(`/api/issues/${issueId}/labels`, { labelId }),
    onSuccess: onChange,
  });
  const detach = useMutation({
    mutationFn: (labelId: string) =>
      api.del(`/api/issues/${issueId}/labels/${labelId}`),
    onSuccess: onChange,
  });

  const unattached = all.items.filter(
    (l) => !attached.some((a) => a.id === l.id),
  );

  return (
    <Section title="Labels" count={attached.length}>
      <div className="card space-y-2 p-3">
        {attached.length === 0 && (
          <p className="text-[13px] text-[var(--color-ink-faint)]">
            No labels on this issue.
          </p>
        )}
        <div className="flex flex-wrap gap-1.5">
          {attached.map((l) => (
            <span key={l.id} className="inline-flex items-center">
              <LabelChip name={l.name} color={l.color} />
              {canWrite && (
                <button
                  type="button"
                  className="btn btn-bare btn-sm px-1"
                  onClick={() => detach.mutate(l.id)}
                  aria-label={`Remove ${l.name}`}
                >
                  ×
                </button>
              )}
            </span>
          ))}
        </div>

        {canWrite && unattached.length > 0 && (
          <select
            className="field h-8 py-0 text-[13px]"
            value=""
            onChange={(e) => e.target.value && attach.mutate(e.target.value)}
          >
            <option value="">Add a label…</option>
            {unattached.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        )}
        <ErrorNote error={attach.error ?? detach.error} />
      </div>
    </Section>
  );
}

/* ── attachments ──────────────────────────────────────────── */

function Attachments({
  issueId,
  canWrite,
}: {
  issueId: string;
  canWrite: boolean;
}) {
  const qc = useQueryClient();
  const files = useList<Attachment>(
    ["attachments", issueId],
    `/api/issues/${issueId}/attachments`,
  );

  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append("file", file);
      return api.post(`/api/issues/${issueId}/attachments`, form);
    },
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ["attachments", issueId] }),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/attachments/${id}`),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ["attachments", issueId] }),
  });

  async function download(a: Attachment) {
    const url = await fetchBlobUrl(`/api/attachments/${a.id}/download`);
    const link = document.createElement("a");
    link.href = url;
    link.download = a.fileName;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <Section
      title="Attachments"
      count={files.items.length}
      actions={
        canWrite && (
          <label className="btn btn-ghost btn-sm cursor-pointer">
            {upload.isPending ? "Uploading…" : "Add file"}
            <input
              type="file"
              className="sr-only"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) upload.mutate(f);
                e.target.value = "";
              }}
            />
          </label>
        )
      }
    >
      <div className="card overflow-hidden">
        {files.items.length === 0 ? (
          <p className="px-3 py-4 text-[13px] text-[var(--color-ink-faint)]">
            No files attached.
          </p>
        ) : (
          <ul className="sheet">
            {files.items.map((a) => (
              <li
                key={a.id}
                className="group flex items-center gap-3 px-3 py-2"
              >
                <span className="min-w-0 flex-1 truncate text-[13px]">
                  {a.fileName}
                </span>
                <span className="t-meta">{fileSize(a.sizeBytes)}</span>
                <button
                  type="button"
                  className="btn btn-bare btn-sm"
                  onClick={() => download(a)}
                >
                  Download
                </button>
                {canWrite && (
                  <button
                    type="button"
                    className="btn btn-bare btn-sm opacity-0 group-hover:opacity-100 focus:opacity-100"
                    onClick={() => remove.mutate(a.id)}
                  >
                    Remove
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
      <ErrorNote error={upload.error ?? remove.error} className="mt-2" />
    </Section>
  );
}

/* ── comments ─────────────────────────────────────────────── */

function Comments({
  issueId,
  canWrite,
  meId,
}: {
  issueId: string;
  canWrite: boolean;
  meId?: string;
}) {
  const qc = useQueryClient();
  const [body, setBody] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editBody, setEditBody] = useState("");

  const list = useCursorList<Comment>(
    ["comments", issueId],
    `/api/issues/${issueId}/comments`,
    {},
    { limit: 20 },
  );
  const invalidate = () =>
    qc.invalidateQueries({ queryKey: ["comments", issueId] });

  const add = useMutation({
    mutationFn: () => api.post(`/api/issues/${issueId}/comments`, { body }),
    onSuccess: () => {
      setBody("");
      invalidate();
    },
  });
  const edit = useMutation({
    mutationFn: () => api.patch(`/api/comments/${editingId}`, { body: editBody }),
    onSuccess: () => {
      setEditingId(null);
      invalidate();
    },
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/comments/${id}`),
    onSuccess: invalidate,
  });

  return (
    <Section title="Comments" count={list.items.length}>
      <div className="card overflow-hidden">
        {list.items.length === 0 ? (
          <Empty
            title="No comments yet"
            hint="Explain what you found, or what you decided."
          />
        ) : (
          <ul className="sheet">
            {list.items.map((c) => (
              <li key={c.id} className="group px-3 py-3">
                <div className="mb-1.5 flex items-center gap-2">
                  <Avatar user={c.author} size={20} />
                  <span className="text-[13px] font-medium">
                    {c.author.displayName}
                  </span>
                  <span className="t-meta text-[11px]">
                    {ago(c.createdAtUtc)}
                    {c.updatedAtUtc && " · edited"}
                  </span>
                  {canWrite && c.author.id === meId && (
                    <span className="ml-auto flex opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                      <button
                        type="button"
                        className="btn btn-bare btn-sm"
                        onClick={() => {
                          setEditingId(c.id);
                          setEditBody(c.body);
                        }}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="btn btn-bare btn-sm"
                        onClick={() => remove.mutate(c.id)}
                      >
                        Delete
                      </button>
                    </span>
                  )}
                </div>

                {editingId === c.id ? (
                  <div className="space-y-2">
                    <textarea
                      className="field"
                      rows={3}
                      value={editBody}
                      onChange={(e) => setEditBody(e.target.value)}
                    />
                    <div className="flex gap-2">
                      <button
                        type="button"
                        className="btn btn-primary btn-sm"
                        onClick={() => edit.mutate()}
                        disabled={edit.isPending}
                      >
                        Save
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        onClick={() => setEditingId(null)}
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                ) : (
                  <p className="text-[14px] whitespace-pre-wrap">{c.body}</p>
                )}
              </li>
            ))}
          </ul>
        )}
        <LoadMore
          hasNext={Boolean(list.hasNextPage)}
          loading={list.isFetchingNextPage}
          onClick={() => list.fetchNextPage()}
        />
      </div>

      {canWrite && (
        <form
          className="mt-3 space-y-2"
          onSubmit={(e) => {
            e.preventDefault();
            add.mutate();
          }}
        >
          <Field label="Add a comment">
            <textarea
              className="field"
              rows={3}
              value={body}
              onChange={(e) => setBody(e.target.value)}
              placeholder="What did you find?"
            />
          </Field>
          <ErrorNote error={add.error ?? edit.error ?? remove.error} />
          <button
            type="submit"
            className={cx("btn btn-primary")}
            disabled={add.isPending || !body.trim()}
          >
            {add.isPending ? "Posting…" : "Comment"}
          </button>
        </form>
      )}
    </Section>
  );
}

