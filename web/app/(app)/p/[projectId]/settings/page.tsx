"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useProjectRole } from "@/lib/hooks";
import { fullDate } from "@/lib/format";
import type { Project } from "@/lib/types";
import {
  SettingsTabs,
  projectSettingsTabs,
} from "@/components/settings-tabs";
import {
  Confirm,
  ErrorNote,
  Field,
  Loading,
  PageHead,
  Section,
} from "@/components/kit";

export default function ProjectSettingsPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const qc = useQueryClient();
  const router = useRouter();
  const { canAdmin, canDeleteProject } = useProjectRole(projectId);
  const [deleting, setDeleting] = useState(false);

  const { data: project } = useQuery({
    queryKey: ["project", projectId],
    queryFn: () => api.get<Project>(`/api/projects/${projectId}`),
  });

  // Both fields start at whatever the server last told us, and follow it if
  // the project changes underneath while this page is open.
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [seeded, setSeeded] = useState<Project | null>(null);
  if (project && seeded !== project) {
    setSeeded(project);
    setName(project.name);
    setDescription(project.description ?? "");
  }

  const save = useMutation({
    mutationFn: () =>
      api.patch(`/api/projects/${projectId}`, { name, description }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["project", projectId] }),
  });

  const archive = useMutation({
    mutationFn: (archived: boolean) =>
      api.post(
        `/api/projects/${projectId}/${archived ? "archive" : "unarchive"}`,
      ),
    onSuccess: () => qc.invalidateQueries(),
  });

  const remove = useMutation({
    mutationFn: () => api.del(`/api/projects/${projectId}`),
    onSuccess: () => {
      qc.invalidateQueries();
      router.replace("/dashboard");
    },
  });

  if (!project) return <Loading label="Loading settings" />;

  return (
    <>
      <PageHead
        eyebrow="Settings"
        title={project.name}
        meta={`Key ${project.key} · created ${fullDate(project.createdAtUtc)}`}
      />

      <SettingsTabs tabs={projectSettingsTabs(projectId)} />

      <div className="grid gap-6 lg:grid-cols-2">
        <Section title="Details">
          <form
            className="card space-y-3.5 p-4"
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
                disabled={!canAdmin}
              />
            </Field>
            <Field
              label="Key"
              hint="The prefix on every issue in this project. It cannot be changed."
            >
              <input className="field" value={project.key} disabled />
            </Field>
            <Field label="Description">
              <textarea
                className="field"
                rows={3}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                disabled={!canAdmin}
              />
            </Field>
            <ErrorNote error={save.error} />
            {canAdmin && (
              <button
                type="submit"
                className="btn btn-primary"
                disabled={save.isPending}
              >
                {save.isPending ? "Saving…" : "Save changes"}
              </button>
            )}
          </form>
        </Section>

        <Section title="Lifecycle">
          <div className="card space-y-4 p-4">
            <div>
              <p className="mb-1 font-medium">
                {project.isArchived ? "Archived" : "Active"}
              </p>
              <p className="mb-2.5 text-[13px] text-[var(--color-ink-soft)]">
                Archiving hides the project from lists and stops new work. Its
                issues stay readable.
              </p>
              {canAdmin && (
                <button
                  type="button"
                  className="btn btn-ghost"
                  onClick={() => archive.mutate(!project.isArchived)}
                  disabled={archive.isPending}
                >
                  {project.isArchived ? "Unarchive project" : "Archive project"}
                </button>
              )}
            </div>

            <hr className="border-t border-[var(--color-rule)]" />

            <div>
              <p className="mb-1 font-medium">Delete this project</p>
              <p className="mb-2.5 text-[13px] text-[var(--color-ink-soft)]">
                {canDeleteProject
                  ? "Removes the project, its boards, sprints and every issue in it."
                  : "Only a workspace admin can delete a project. Archiving is the reversible option."}
              </p>
              <button
                type="button"
                className="btn btn-danger"
                onClick={() => setDeleting(true)}
                disabled={!canDeleteProject}
              >
                Delete project
              </button>
            </div>

            <ErrorNote error={archive.error ?? remove.error} />
          </div>
        </Section>
      </div>

      <Confirm
        open={deleting}
        title={`Delete ${project.key}?`}
        body="Every board, sprint, issue and comment in this project is removed. This cannot be undone."
        confirmLabel="Delete project"
        pending={remove.isPending}
        onCancel={() => setDeleting(false)}
        onConfirm={() => remove.mutate()}
      />
    </>
  );
}
