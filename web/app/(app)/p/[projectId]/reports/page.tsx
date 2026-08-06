"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { useList, useSprintReport } from "@/lib/hooks";
import { ago, cx, fullDate } from "@/lib/format";
import type { BoardItem, HealthState, SprintItem, SprintReport } from "@/lib/types";
import { IssueKey, SprintStatusChip } from "@/components/marks";
import {
  AssigneeLoad,
  SprintProgress,
  StatusStrip,
} from "@/components/charts";
import { Empty, ErrorNote, Loading, PageHead, Section } from "@/components/kit";

/**
 * The team-lead read of a sprint (spec/24-reports.md). Every figure on this page
 * comes from one endpoint, including the health verdict — the rules live on the
 * server and arrive here already reasoned, so this page never restates them.
 */
export default function ReportsPage() {
  const { projectId } = useParams<{ projectId: string }>();

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

  // Running first, then what is planned, then history newest-first — the same
  // ordering the sprints list uses, because the sprint you want a report on is
  // almost always the one you are in.
  const ordered = sprints.items.slice().sort((a, b) => {
    const rank = { Active: 0, Planned: 1, Completed: 2 } as const;
    if (rank[a.status] !== rank[b.status]) return rank[a.status] - rank[b.status];
    return a.status === "Completed"
      ? b.plannedStartDateUtc.localeCompare(a.plannedStartDateUtc)
      : a.plannedStartDateUtc.localeCompare(b.plannedStartDateUtc);
  });

  const [sprintId, setSprintId] = useState<string>("");
  const sprint = ordered.find((s) => s.id === sprintId) ?? ordered[0];
  const report = useSprintReport(sprint?.id);

  if (boards.isLoading) return <Loading label="Loading reports" />;

  if (scrumBoards.length === 0) {
    return (
      <>
        <PageHead eyebrow="Reports" title="Sprint report" />
        <div className="card">
          <Empty
            title="Sprint reports need a Scrum board"
            hint="Kanban boards run continuously and have no sprints to report on."
            action={
              <Link
                href={`/p/${projectId}/boards`}
                className="btn btn-primary btn-sm"
              >
                Add a Scrum board
              </Link>
            }
          />
        </div>
      </>
    );
  }

  if (!sprints.isLoading && ordered.length === 0) {
    return (
      <>
        <PageHead eyebrow="Reports" title="Sprint report" />
        <div className="card">
          <Empty
            title="No sprints to report on"
            hint="Create a sprint, fill it from the backlog, then start it — the report fills itself in."
            action={
              <Link
                href={`/p/${projectId}/sprints`}
                className="btn btn-primary btn-sm"
              >
                Go to sprints
              </Link>
            }
          />
        </div>
      </>
    );
  }

  return (
    <>
      <PageHead
        eyebrow="Reports"
        title={sprint?.name ?? "Sprint report"}
        meta={
          sprint
            ? `${fullDate(sprint.plannedStartDateUtc)} → ${fullDate(sprint.plannedEndDateUtc)}`
            : undefined
        }
        actions={
          <>
            {scrumBoards.length > 1 && (
              <select
                className="field w-auto"
                value={board?.id}
                onChange={(e) => {
                  setBoardId(e.target.value);
                  setSprintId("");
                }}
                aria-label="Choose board"
              >
                {scrumBoards.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            )}
            {ordered.length > 1 && (
              <select
                className="field w-auto"
                value={sprint?.id}
                onChange={(e) => setSprintId(e.target.value)}
                aria-label="Choose sprint"
              >
                {ordered.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                    {s.status === "Active" ? " (running)" : ""}
                  </option>
                ))}
              </select>
            )}
          </>
        }
      />

      {report.error ? (
        <ErrorNote error={report.error} />
      ) : !report.data ? (
        <div className="card">
          <Loading label="Reading the sprint" />
        </div>
      ) : (
        <Report projectId={projectId} report={report.data} />
      )}
    </>
  );
}

function Report({
  projectId,
  report,
}: {
  projectId: string;
  report: SprintReport;
}) {
  const { sprint, pace, progress, byStatus, byAssignee, risks, health } = report;

  return (
    <div className="space-y-6">
      <section className="card overflow-hidden">
        {/*
          The verdict line and the goal are two different kinds of sentence, so
          they get two lines. Run together they read as one list, and the goal
          arrives looking like a third thing wrong with the sprint.
        */}
        <div className="border-b border-[var(--color-rule)] px-4 py-3">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
            <SprintStatusChip status={sprint.status} />
            <HealthVerdict health={health} />
          </div>
          {sprint.goal && (
            <p className="mt-2 text-[13px] text-[var(--color-ink-soft)]">
              <span className="t-eyebrow mr-2 text-[10px]">Goal</span>
              {sprint.goal}
            </p>
          )}
        </div>

        {/*
          A completed sprint is emptied of everything unfinished when it closes
          (spec/08-sprints.md BR-05), so its figures below describe what it
          finished and nothing else. Saying so is the difference between a
          report and a 100% that means nothing.
        */}
        {sprint.status === "Completed" && (
          <p className="border-b border-[var(--color-rule-soft)] bg-[var(--color-haze)] px-4 py-2.5 text-[12px] text-[var(--color-ink-soft)]">
            These figures cover what this sprint finished.{" "}
            {sprint.carriedForwardIssueCount === null
              ? "The work carried out of it on completion was not recorded."
              : sprint.carriedForwardIssueCount === 0
                ? "Nothing was carried out of it."
                : `${sprint.carriedForwardIssueCount} unfinished ${sprint.carriedForwardIssueCount === 1 ? "issue was" : "issues were"} carried out of it on completion.`}
          </p>
        )}

        <div className="grid lg:grid-cols-[minmax(0,1.55fr)_minmax(280px,1fr)]">
          <div className="p-4">
            <h2 className="t-eyebrow mb-3.5">How far it has got</h2>
            <SprintProgress progress={progress} pace={pace} />
          </div>
          <div className="border-t border-[var(--color-rule)] p-4 lg:border-t-0 lg:border-l">
            <h2 className="t-eyebrow mb-3.5">Where the work sits</h2>
            <StatusStrip
              byStatus={byStatus}
              empty="Nothing is in this sprint yet. Pull work into it from the backlog."
            />
          </div>
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.55fr)_minmax(280px,1fr)]">
        <div className="space-y-6">
          <Section title="Blocked" count={risks.blocked.length}>
            <div className="card overflow-hidden">
              {risks.blocked.length === 0 ? (
                <Empty
                  title="Nothing is blocked"
                  hint="Blocking an issue from its page puts it here, with its reason and how long it has been stuck."
                />
              ) : (
                <ul className="sheet">
                  {risks.blocked.map((b) => (
                    <li key={b.id}>
                      <Link
                        href={`/i/${b.id}`}
                        className="row-hover flex items-start gap-3 px-3 py-2.5"
                      >
                        <span className="pt-0.5">
                          <IssueKey issueKey={b.key} />
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="block truncate">{b.title}</span>
                          {b.blockedReason && (
                            <span className="mt-0.5 block truncate text-[12px] text-[var(--color-ink-soft)]">
                              {b.blockedReason}
                            </span>
                          )}
                        </span>
                        <span
                          className="chip chip-signal shrink-0"
                          title={
                            b.blockedSinceUtc
                              ? `Blocked ${ago(b.blockedSinceUtc)}`
                              : undefined
                          }
                        >
                          {b.blockedDays === 0
                            ? "Today"
                            : `${b.blockedDays}d`}
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </Section>

          <Section title="Who is carrying it" count={byAssignee.length}>
            <div className="card p-4">
              <AssigneeLoad byAssignee={byAssignee} />
            </div>
          </Section>
        </div>

        <Section title="Worth a look">
          <div className="card overflow-hidden">
            <ul className="sheet">
              <RiskRow
                label="Past their due date"
                count={risks.overdueCount}
                signal
                href={`/p/${projectId}/issues`}
              />
              <RiskRow
                label="Due after the sprint ends"
                count={risks.dueAfterSprintEndCount}
                signal
              />
              <RiskRow label="Nobody assigned" count={risks.unassignedCount} />
              <RiskRow label="No estimate" count={risks.unestimatedCount} />
            </ul>
          </div>
        </Section>
      </div>
    </div>
  );
}

/**
 * The verdict and, beside it, everything that produced it. A state on its own
 * is a number nobody can argue with or act on; the reasons are the report.
 */
function HealthVerdict({ health }: { health: SprintReport["health"] }) {
  if (health.state === null) {
    return (
      <span className="text-[13px] text-[var(--color-ink-soft)]">
        Not started — nothing to judge yet.
      </span>
    );
  }

  const label: Record<HealthState, string> = {
    OnTrack: "On track",
    AtRisk: "At risk",
    OffTrack: "Off track",
  };

  return (
    <span className="flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-1">
      <span
        className={cx(
          "chip",
          health.state === "OnTrack" ? "chip-ink" : "chip-signal",
        )}
      >
        {label[health.state]}
      </span>
      {/* Separated, because two reasons set side by side read as one long
          sentence and the reader has to find the seam themselves. */}
      {health.reasons.map((r, i) => (
        <span key={r.code} className="flex items-baseline gap-2.5">
          {i > 0 && (
            <span aria-hidden className="text-[var(--color-ink-faint)]">
              ·
            </span>
          )}
          <span className="text-[12px] text-[var(--color-ink-soft)]">
            {r.detail}
          </span>
        </span>
      ))}
    </span>
  );
}

function RiskRow({
  label,
  count,
  signal,
  href,
}: {
  label: string;
  count: number;
  signal?: boolean;
  href?: string;
}) {
  const lit = Boolean(signal) && count > 0;
  const body = (
    <>
      <span className="min-w-0 flex-1 truncate text-[13px]">{label}</span>
      <span
        className="t-num text-[13px]"
        style={lit ? { color: "var(--color-over)" } : undefined}
      >
        {count}
      </span>
    </>
  );

  return (
    <li>
      {href && count > 0 ? (
        <Link
          href={href}
          className="row-hover flex items-center gap-2 px-3 py-2.5"
        >
          {body}
        </Link>
      ) : (
        <span className="flex items-center gap-2 px-3 py-2.5">{body}</span>
      )}
    </li>
  );
}
