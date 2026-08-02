"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useMemo, useState } from "react";
import { useList } from "@/lib/hooks";
import { cx, fromUtc, fullDate } from "@/lib/format";
import type { DueDateEntry, TimelineSprint } from "@/lib/types";
import { IssueKey, SprintStatusChip, TypeMark } from "@/components/marks";
import { Empty, Loading, PageHead, Section } from "@/components/ui";

const DAY_NAMES = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

export default function CalendarPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const [offset, setOffset] = useState(0);

  const anchor = useMemo(() => {
    const d = new Date();
    d.setDate(1);
    d.setMonth(d.getMonth() + offset);
    d.setHours(0, 0, 0, 0);
    return d;
  }, [offset]);

  const from = new Date(anchor);
  const to = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 0);
  const key = (d: Date) => d.toISOString().slice(0, 10);

  const due = useList<DueDateEntry>(
    ["calendar-due", projectId],
    `/api/projects/${projectId}/calendar/due-dates`,
    { from: key(from), to: key(to) },
  );

  const timeline = useList<TimelineSprint>(
    ["calendar-sprints", projectId],
    `/api/projects/${projectId}/calendar/sprint-timeline`,
  );

  const byDay = useMemo(() => {
    const map: Record<string, DueDateEntry[]> = {};
    for (const d of due.items) {
      const k = d.dueDateUtc.slice(0, 10);
      (map[k] ??= []).push(d);
    }
    return map;
  }, [due.items]);

  // Monday-first grid, padded to whole weeks.
  const cells: (Date | null)[] = [];
  const lead = (from.getDay() + 6) % 7;
  for (let i = 0; i < lead; i++) cells.push(null);
  for (let d = 1; d <= to.getDate(); d++)
    cells.push(new Date(anchor.getFullYear(), anchor.getMonth(), d));
  while (cells.length % 7 !== 0) cells.push(null);

  const today = key(new Date());

  return (
    <>
      <PageHead
        eyebrow="Calendar"
        title={anchor.toLocaleDateString("en", {
          month: "long",
          year: "numeric",
        })}
        meta="Due dates for this project, and where the sprints sit."
        actions={
          <>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => setOffset((o) => o - 1)}
            >
              ← Previous
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => setOffset(0)}
              disabled={offset === 0}
            >
              This month
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => setOffset((o) => o + 1)}
            >
              Next →
            </button>
          </>
        }
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,2.2fr)_minmax(240px,1fr)]">
        <div className="card overflow-hidden">
          <div className="grid grid-cols-7 border-b border-[var(--color-rule)]">
            {DAY_NAMES.map((d) => (
              <div key={d} className="t-eyebrow px-2 py-1.5 text-[10px]">
                {d}
              </div>
            ))}
          </div>

          {due.isLoading ? (
            <Loading />
          ) : (
            <div className="grid grid-cols-7">
              {cells.map((d, i) => {
                const k = d ? key(d) : "";
                const items = k ? (byDay[k] ?? []) : [];
                return (
                  <div
                    key={i}
                    className={cx(
                      "min-h-[92px] border-r border-b border-[var(--color-rule-soft)] p-1.5",
                      !d && "bg-[var(--color-paper-deep)]",
                      k === today && "bg-[var(--color-blue-wash)]",
                    )}
                  >
                    {d && (
                      <div className="t-num mb-1 text-[11px] text-[var(--color-ink-faint)]">
                        {d.getDate()}
                      </div>
                    )}
                    <ul className="space-y-1">
                      {items.map((it) => (
                        <li key={it.id}>
                          <Link
                            href={`/i/${it.id}`}
                            className="plate flex items-center gap-1 px-1 py-0.5"
                            title={it.title}
                          >
                            <TypeMark type={it.type} size={9} />
                            <span className="t-num truncate text-[10px] text-[var(--color-blue)]">
                              {it.key}
                            </span>
                          </Link>
                        </li>
                      ))}
                    </ul>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        <div className="space-y-6">
          <Section title="Due this month" count={due.items.length}>
            <div className="card overflow-hidden">
              {due.items.length === 0 ? (
                <Empty
                  title="Nothing is due this month"
                  hint="Set a due date on an issue to see it here."
                />
              ) : (
                <ul className="sheet">
                  {due.items
                    .slice()
                    .sort((a, b) =>
                      a.dueDateUtc.localeCompare(b.dueDateUtc),
                    )
                    .map((d) => (
                      <li key={d.id}>
                        <Link
                          href={`/i/${d.id}`}
                          className="row-hover flex items-center gap-2 px-3 py-2"
                        >
                          <TypeMark type={d.type} />
                          <IssueKey issueKey={d.key} />
                          <span className="min-w-0 flex-1 truncate text-[13px]">
                            {d.title}
                          </span>
                          <span className="t-meta text-[11px]">
                            {fromUtc(d.dueDateUtc).getDate()}
                          </span>
                        </Link>
                      </li>
                    ))}
                </ul>
              )}
            </div>
          </Section>

          <Section title="Sprint timeline" count={timeline.items.length}>
            <div className="card overflow-hidden">
              {timeline.items.length === 0 ? (
                <p className="px-3 py-4 text-[13px] text-[var(--color-ink-faint)]">
                  No sprints planned.
                </p>
              ) : (
                <ul className="sheet">
                  {timeline.items.map((s) => (
                    <li key={s.id} className="px-3 py-2.5">
                      <div className="mb-1 flex items-center justify-between gap-2">
                        <span className="truncate text-[13px] font-medium">
                          {s.name}
                        </span>
                        <SprintStatusChip status={s.status} />
                      </div>
                      <p className="t-meta text-[11px]">
                        {fullDate(s.plannedStartDateUtc)} →{" "}
                        {fullDate(s.plannedEndDateUtc)}
                      </p>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </Section>
        </div>
      </div>
    </>
  );
}
