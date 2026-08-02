"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import {
  useCursorList,
  useList,
  useProjectRole,
} from "@/lib/hooks";
import { useQuery } from "@tanstack/react-query";
import { fullDate } from "@/lib/format";
import { PROJECT_ROLES } from "@/lib/types";
import type {
  Project,
  ProjectMember,
  ProjectRoleName,
  WorkspaceMember,
} from "@/lib/types";
import { Avatar } from "@/components/marks";
import {
  Empty,
  ErrorNote,
  Field,
  Loading,
  Modal,
  PageHead,
} from "@/components/ui";
import {
  SettingsTabs,
  projectSettingsTabs,
} from "@/components/settings-tabs";

export default function ProjectMembersPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const qc = useQueryClient();
  const { canAdmin } = useProjectRole(projectId);
  const [adding, setAdding] = useState(false);

  const { data: project } = useQuery({
    queryKey: ["project", projectId],
    queryFn: () => api.get<Project>(`/api/projects/${projectId}`),
  });

  const members = useList<ProjectMember>(
    ["project-members", projectId],
    `/api/projects/${projectId}/members`,
  );

  const changeRole = useMutation({
    mutationFn: (v: { userId: string; role: ProjectRoleName }) =>
      api.patch(`/api/projects/${projectId}/members/${v.userId}`, {
        role: v.role,
      }),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ["project-members", projectId] }),
  });

  const remove = useMutation({
    mutationFn: (userId: string) =>
      api.del(`/api/projects/${projectId}/members/${userId}`),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ["project-members", projectId] }),
  });

  return (
    <>
      <PageHead
        eyebrow="Settings · Members"
        title="Who can work on this project"
        meta="Workspace admins have full access to every project without being listed here."
        actions={
          canAdmin && (
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setAdding(true)}
            >
              Add member
            </button>
          )
        }
      />

      <SettingsTabs tabs={projectSettingsTabs(projectId)} />

      <ErrorNote error={changeRole.error ?? remove.error} className="mb-3" />

      <div className="card overflow-hidden">
        {members.isLoading ? (
          <Loading />
        ) : members.items.length === 0 ? (
          <Empty
            title="No project members yet"
            hint="Add people from the workspace so they can pick up issues."
          />
        ) : (
          <ul className="sheet">
            {members.items.map((m) => (
              <li key={m.userId} className="flex items-center gap-3 px-3 py-2.5">
                <Avatar user={m} size={26} />
                <Link
                  href={`/u/${m.userId}`}
                  className="min-w-0 flex-1 truncate font-medium hover:underline"
                >
                  {m.displayName}
                </Link>
                <span className="t-meta hidden sm:inline">
                  joined {fullDate(m.joinedAtUtc)}
                </span>
                {canAdmin ? (
                  <select
                    className="field h-8 w-auto py-0 text-[12px]"
                    value={m.role}
                    onChange={(e) =>
                      changeRole.mutate({
                        userId: m.userId,
                        role: e.target.value as ProjectRoleName,
                      })
                    }
                    aria-label={`Role for ${m.displayName}`}
                  >
                    {PROJECT_ROLES.map((r) => (
                      <option key={r}>{r}</option>
                    ))}
                  </select>
                ) : (
                  <span className="chip">{m.role}</span>
                )}
                {canAdmin && (
                  <button
                    type="button"
                    className="btn btn-bare btn-sm"
                    onClick={() => remove.mutate(m.userId)}
                  >
                    Remove
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      {project && (
        <AddMemberDialog
          open={adding}
          onClose={() => setAdding(false)}
          projectId={projectId}
          workspaceId={project.workspaceId}
          existing={members.items.map((m) => m.userId)}
        />
      )}
    </>
  );
}

function AddMemberDialog({
  open,
  onClose,
  projectId,
  workspaceId,
  existing,
}: {
  open: boolean;
  onClose: () => void;
  projectId: string;
  workspaceId: string;
  existing: string[];
}) {
  const qc = useQueryClient();
  const [userId, setUserId] = useState("");
  const [role, setRole] = useState<ProjectRoleName>("Developer");

  const candidates = useCursorList<WorkspaceMember>(
    ["workspace-members", workspaceId],
    `/api/workspaces/${workspaceId}/members`,
    {},
    { limit: 100, enabled: open },
  );

  const add = useMutation({
    mutationFn: () =>
      api.post(`/api/projects/${projectId}/members`, { userId, role }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["project-members", projectId] });
      setUserId("");
      onClose();
    },
  });

  const available = candidates.items.filter((c) => !existing.includes(c.userId));

  return (
    <Modal open={open} onClose={onClose} title="Add a project member">
      <form
        className="space-y-3.5"
        onSubmit={(e) => {
          e.preventDefault();
          add.mutate();
        }}
      >
        <Field
          label="Person"
          hint="Only workspace members can join a project."
        >
          <select
            className="field"
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
            required
          >
            <option value="">Choose someone</option>
            {available.map((c) => (
              <option key={c.userId} value={c.userId}>
                {c.displayName}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Role">
          <select
            className="field"
            value={role}
            onChange={(e) => setRole(e.target.value as ProjectRoleName)}
          >
            {PROJECT_ROLES.map((r) => (
              <option key={r}>{r}</option>
            ))}
          </select>
        </Field>

        <ErrorNote error={add.error} />

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={add.isPending || !userId}
          >
            {add.isPending ? "Adding…" : "Add member"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

