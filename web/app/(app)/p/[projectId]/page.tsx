"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { useCursorList, useList, useProjectRole } from "@/lib/hooks";
import { fullDate } from "@/lib/format";
import type {
  BoardItem,
  IssueListItem,
  Project,
  ProjectMember,
  SprintItem,
} from "@/lib/types";
import {
  Avatar,
  DueDate,
  IssueKey,
  PriorityMark,
  SprintStatusChip,
  TypeMark,
} from "@/components/marks";
import { Empty, Loading, PageHead, Section } from "@/components/kit";
import { NewIssueButton } from "@/components/new-issue";

export default function ProjectOverviewPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const { role, canWrite, viaWorkspaceAdmin } = useProjectRole(projectId);

  const { data: project } = useQuery({
    queryKey: ["project", projectId],
    queryFn: () => api.get<Project>(`/api/projects/${projectId}`),
  });

  const boards = useList<BoardItem>(
    ["boards", projectId],
    `/api/projects/${projectId}/boards`,
  );
  const members = useList<ProjectMember>(
    ["project-members", projectId],
    `/api/projects/${projectId}/members`,
  );
  const issues = useCursorList<IssueListItem>(
    ["project-issues", projectId],
    `/api/projects/${projectId}/issues`,
    {},
    { limit: 8 },
  );

  const scrumBoard = boards.items.find((b) => b.type === "Scrum");
  const sprints = useList<SprintItem>(
    ["sprints", scrumBoard?.id],
    `/api/boards/${scrumBoard?.id}/sprints`,
    undefined,
    { enabled: Boolean(scrumBoard) },
  );
  const active = sprints.items.find((s) => s.status === "Active");

  if (!project) return <Loading label="Opening project" />;

  return (
    <>
      <PageHead
        eyebrow={project.key}
        title={project.name}
        meta={
          <>
            Created {fullDate(project.createdAtUtc)} · your role:{" "}
            <strong className="font-medium text-[var(--color-ink)]">
              {role ?? "none"}
            </strong>
            {viaWorkspaceAdmin && " (workspace admin)"}
            {project.isArchived && " · archived"}
          </>
        }
        actions={canWrite && <NewIssueButton projectId={projectId} />}
      />

      {project.description && (
        <p className="mb-6 max-w-[70ch] text-[14px] text-[var(--color-ink-soft)]">
          {project.description}
        </p>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.6fr)_minmax(260px,1fr)]">
        <Section title="Latest issues">
          <div className="card overflow-hidden">
            {issues.items.length === 0 ? (
              <Empty
                title="No issues yet"
                hint="Everything the team is working on lives here."
                action={
                  canWrite ? <NewIssueButton projectId={projectId} /> : undefined
                }
              />
            ) : (
              <ul className="sheet">
                {issues.items.map((i) => (
                  <li key={i.id}>
                    <Link
                      href={`/i/${i.id}`}
                      className="row-hover flex items-center gap-3 px-3 py-2.5"
                    >
                      <TypeMark type={i.type} />
                      <IssueKey issueKey={i.key} />
                      <span className="min-w-0 flex-1 truncate">{i.title}</span>
                      <PriorityMark priority={i.priority} />
                      <DueDate value={i.dueDateUtc} />
                      <Avatar user={i.assignee} size={20} />
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {issues.items.length > 0 && (
            <Link
              href={`/p/${projectId}/issues`}
              className="t-meta mt-2 inline-block underline decoration-[var(--color-pink)] underline-offset-2"
            >
              See all issues
            </Link>
          )}
        </Section>

        <div className="space-y-6">
          <Section title="Current sprint">
            <div className="card p-3">
              {active ? (
                <>
                  <div className="mb-1 flex items-center justify-between gap-2">
                    <Link
                      href={`/p/${projectId}/sprints/${active.id}`}
                      className="font-medium hover:underline"
                    >
                      {active.name}
                    </Link>
                    <SprintStatusChip status={active.status} />
                  </div>
                  <p className="t-meta">
                    {fullDate(active.plannedStartDateUtc)} →{" "}
                    {fullDate(active.plannedEndDateUtc)}
                  </p>
                </>
              ) : (
                <p className="text-[13px] text-[var(--color-ink-soft)]">
                  No sprint is running.{" "}
                  {scrumBoard ? (
                    <Link
                      href={`/p/${projectId}/sprints`}
                      className="text-[var(--color-blue)] underline decoration-[var(--color-pink)] underline-offset-2"
                    >
                      Plan one
                    </Link>
                  ) : (
                    "Add a Scrum board to run sprints."
                  )}
                </p>
              )}
            </div>
          </Section>

          <Section title="Boards" count={boards.items.length}>
            <div className="card overflow-hidden">
              <ul className="sheet">
                {boards.items.map((b) => (
                  <li key={b.id}>
                    <Link
                      href={`/p/${projectId}/board?board=${b.id}`}
                      className="row-hover flex items-center justify-between px-3 py-2"
                    >
                      <span className="text-[13px]">{b.name}</span>
                      <span className="chip">{b.type}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          </Section>

          <Section title="Members" count={members.items.length}>
            <div className="card overflow-hidden">
              <ul className="sheet">
                {members.items.map((m) => (
                  <li
                    key={m.userId}
                    className="flex items-center gap-2.5 px-3 py-2"
                  >
                    <Avatar user={m} size={22} />
                    <Link
                      href={`/u/${m.userId}`}
                      className="min-w-0 flex-1 truncate text-[13px] hover:underline"
                    >
                      {m.displayName}
                    </Link>
                    <span className="chip">{m.role}</span>
                  </li>
                ))}
              </ul>
            </div>
          </Section>
        </div>
      </div>
    </>
  );
}
