"use client";

/**
 * The dashboard's figures, drawn by hand rather than by a chart library.
 *
 * Every chart here is a few divs and a hairline, so they inherit the page's
 * palette, its dark mode and its reduced-motion rule for free — and the bundle
 * gains nothing. Colour follows the house rule (app/globals.css): blue is the
 * series and gets deeper the further work has travelled; coral is signal only,
 * spent on overdue and on Critical, and on nothing else.
 */

import { format } from "date-fns";
import { PriorityMark } from "@/components/marks";
import { cx, fromUtc } from "@/lib/format";
import type { MyStats, PriorityName, SprintReport } from "@/lib/types";

/* ── the stat row ─────────────────────────────────────────── */

function Stat({
  label,
  value,
  hint,
  signal,
}: {
  label: string;
  value: number;
  hint?: string;
  signal?: boolean;
}) {
  const lit = Boolean(signal) && value > 0;
  return (
    <div className="px-4 py-3.5">
      <div
        className="figure"
        style={{ color: lit ? "var(--color-over)" : "var(--color-ink)" }}
      >
        {value}
      </div>
      <div className="mt-1.5 text-[12px] font-medium">{label}</div>
      {hint && (
        <div className="mt-0.5 text-[11px] text-[var(--color-ink-faint)]">
          {hint}
        </div>
      )}
    </div>
  );
}

export function StatRow({ totals }: { totals: MyStats["totals"] }) {
  return (
    <div className="stat-row border-b border-[var(--color-rule)]">
      <Stat label="Open" value={totals.open} hint="assigned to you" />
      <Stat label="Overdue" value={totals.overdue} hint="past its due date" signal />
      <Stat label="Due in 7 days" value={totals.dueSoon} hint="still open" />
      <Stat label="Finished" value={totals.done} hint={`of ${totals.assigned}`} />
    </div>
  );
}

/* ── the streak ───────────────────────────────────────────────
   What you did to Issues, a column a day. Created sits at the
   bottom of each column because that is where a piece of work
   starts; comments ride on top.
   ─────────────────────────────────────────────────────────── */

const SERIES = [
  { key: "created", label: "Created", color: "var(--color-blue-deep)" },
  { key: "moved", label: "Moved", color: "var(--color-blue)" },
  { key: "commented", label: "Commented", color: "var(--color-blue-soft)" },
] as const;

/** A floor, not a fixed height — the plot takes whatever the pane beside it makes available. */
const MIN_PLOT_HEIGHT = 148;

/** Bottom of each column upwards, so work starts at the baseline and comments ride on top. */
const STACK = [...SERIES].reverse();

/**
 * The whole run stands up inside this, however many columns there are — a
 * per-column delay would make a month take three times as long as a fortnight.
 */
const STAGGER_MS = 300;

export function ActivityStreak({ activity }: { activity: MyStats["activity"] }) {
  const days = activity.map((d) => {
    const date = fromUtc(d.date);
    const total = d.created + d.moved + d.commented;
    const weekend = date.getDay() === 0 || date.getDay() === 6;
    return { ...d, date, total, weekend };
  });

  const ceiling = Math.max(...days.map((d) => d.total), 0);
  const grandTotal = days.reduce((sum, d) => sum + d.total, 0);
  /** The window always ends on today, so the last column is the one you are in. */
  const todayIndex = days.length - 1;

  if (grandTotal === 0) {
    return (
      <div
        className="flex flex-1 items-center justify-center px-4 text-center text-[13px] text-[var(--color-ink-soft)]"
        style={{ minHeight: MIN_PLOT_HEIGHT }}
      >
        Nothing logged in this window. Create, move, or comment on an issue and
        it lands here.
      </div>
    );
  }

  const busiest = days.reduce((a, b) => (b.total > a.total ? b : a));
  /** A fortnight of columns is wide enough to letter every one; a month is not. */
  const dense = days.length <= 14;

  return (
    <div className="flex flex-1 flex-col">
      {/* One rule at the height of the busiest day, labelled with its own
          number. Cheaper than a gridded axis, and the only figure the columns
          actually need to be read against. */}
      <div className="relative flex flex-1 flex-col pt-[15px]">
        <span className="t-meta absolute top-0 left-0">{ceiling}</span>
        <span className="t-meta absolute top-0 right-0">
          {format(days[0].date, "d MMM")} – {format(days[todayIndex].date, "d MMM")}
        </span>
        <div
          className="ceiling flex flex-1 items-end gap-[3px]"
          style={{ minHeight: MIN_PLOT_HEIGHT }}
          role="img"
          aria-label={`Your issue activity over the last ${days.length} days: ${grandTotal} actions in total, busiest on ${format(busiest.date, "d MMMM")} with ${ceiling}.`}
        >
          {days.map((d, i) => (
            <div
              key={d.date.toISOString()}
              className="stand flex h-full flex-1 flex-col justify-end"
              style={{ animationDelay: `${(i / days.length) * STAGGER_MS}ms` }}
              title={`${format(d.date, "EEE d MMM")} — ${d.created} created, ${d.moved} moved, ${d.commented} comments`}
            >
              {STACK.map((s) => {
                const value = d[s.key];
                if (value === 0) return null;
                return (
                  <div
                    key={s.key}
                    style={{
                      height: `${(value / ceiling) * 100}%`,
                      background: s.color,
                      minHeight: 2,
                    }}
                  />
                );
              })}
            </div>
          ))}
        </div>
      </div>

      {/*
        The baseline, then the axis. A fortnight gets a letter a day; a month
        does not — thirty initials is a wall, so it falls back to a dated tick
        every week, counted back from today. Today is named by weight rather
        than by a glyph: beside a 10px letter, a mark reads as a smudge.
      */}
      <div className="mt-[3px] border-t border-[var(--color-rule)]" />
      <div className="flex gap-[3px] pt-[6px]" style={{ height: 17 }}>
        {days.map((d, i) => {
          const isToday = i === todayIndex;
          const stepsBack = todayIndex - i;
          // Nothing gets a label so close to the left edge that it hangs off it.
          const labelled = dense || isToday || (stepsBack % 7 === 0 && i > 2);
          if (!labelled) return <span key={d.date.toISOString()} className="flex-1" />;

          return (
            <span key={d.date.toISOString()} className="relative flex-1">
              <span
                className={cx(
                  "t-meta absolute left-1/2 -translate-x-1/2 text-[10px] leading-none whitespace-nowrap",
                  isToday && "font-semibold",
                )}
                style={{
                  color: isToday
                    ? "var(--color-blue-deep)"
                    : d.weekend && dense
                      ? "var(--color-ink-faint)"
                      : "var(--color-ink-soft)",
                }}
              >
                {dense ? format(d.date, "EEEEE") : format(d.date, "d MMM")}
              </span>
            </span>
          );
        })}
      </div>

      <div className="mt-3.5 flex flex-wrap gap-x-4 gap-y-1.5">
        {SERIES.map((s) => {
          const total = days.reduce((sum, d) => sum + d[s.key], 0);
          return (
            <span
              key={s.key}
              className="flex items-center gap-1.5 text-[12px] text-[var(--color-ink-soft)]"
            >
              <span
                aria-hidden
                style={{ width: 8, height: 8, background: s.color }}
              />
              {s.label}
              <span className="t-num text-[12px] text-[var(--color-ink)]">
                {total}
              </span>
            </span>
          );
        })}
      </div>
    </div>
  );
}

/* ── where the work sits ──────────────────────────────────────
   The board, flattened into a line. Segments run left to right
   in board order, so the strip fills towards Done the way the
   columns do.
   ─────────────────────────────────────────────────────────── */

/**
 * Three steps, deepening left to right. Deliberately not starting at
 * --color-blue-wash: against the track it sits on, the first segment would be
 * invisible in dark mode — a status with six issues in it, showing as nothing.
 */
const RAMP = [
  "var(--color-blue-soft)",
  "var(--color-blue)",
  "var(--color-blue-deep)",
];

function statusColor(index: number, count: number) {
  if (count <= 1) return RAMP[RAMP.length - 1];
  return RAMP[Math.round((index / (count - 1)) * (RAMP.length - 1))];
}

export function StatusStrip({
  byStatus,
  empty = "Issues appear here the moment someone puts your name on one.",
}: {
  byStatus: MyStats["byStatus"];
  /** The strip is shared by a first-person dashboard and a whole-team sprint
      report, and "your name" is only true on one of them. */
  empty?: string;
}) {
  const total = byStatus.reduce((sum, b) => sum + b.count, 0);
  if (total === 0) {
    return <p className="py-6 text-[13px] text-[var(--color-ink-soft)]">{empty}</p>;
  }

  // Done columns already sort last, so the ramp deepens towards finished work
  // without the segment colour having to know what "done" means.
  const segments = byStatus.map((bucket, i) => ({
    ...bucket,
    color: statusColor(i, byStatus.length),
  }));

  return (
    <div>
      <div
        className="flex gap-px overflow-hidden rounded-full"
        style={{ height: 10, background: "var(--color-rule-soft)" }}
        role="img"
        aria-label={`Your ${total} issues by status: ${segments.map((s) => `${s.count} ${s.name}`).join(", ")}.`}
      >
        {segments.map((s) => (
          <div
            key={s.name}
            style={{ flex: s.count, background: s.color }}
            title={`${s.name} — ${s.count}`}
          />
        ))}
      </div>

      <ul className="mt-3 space-y-1.5">
        {segments.map((s) => (
          <li key={s.name} className="flex items-center gap-2 text-[13px]">
            <span
              aria-hidden
              className="shrink-0 rounded-[2px]"
              style={{ width: 8, height: 8, background: s.color }}
            />
            <span className="min-w-0 flex-1 truncate">{s.name}</span>
            {s.isDone && <span className="chip">Done</span>}
            <span className="t-num text-[13px]">{s.count}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/* ── pressure ─────────────────────────────────────────────────
   The same four steps the Issue rows are marked with, so the
   glyph beside each bar is the one already learned elsewhere.
   ─────────────────────────────────────────────────────────── */

const PRIORITY_COLOR: Record<PriorityName, string> = {
  Critical: "var(--color-pink)",
  High: "var(--color-blue)",
  Medium: "var(--color-blue-soft)",
  Low: "var(--color-blue-soft)",
};

export function PriorityBars({
  byPriority,
}: {
  byPriority: MyStats["byPriority"];
}) {
  const ceiling = Math.max(...byPriority.map((p) => p.count), 1);
  const total = byPriority.reduce((sum, p) => sum + p.count, 0);

  if (total === 0) return null;

  return (
    <ul className="space-y-2.5">
      {byPriority.map((p) => (
        <li key={p.priority} className="flex items-center gap-2.5">
          <PriorityMark priority={p.priority} />
          <span className="w-[52px] shrink-0 text-[12px] text-[var(--color-ink-soft)]">
            {p.priority}
          </span>
          <span
            className="min-w-0 flex-1 rounded-full"
            style={{ height: 6, background: "var(--color-rule-soft)" }}
          >
            <span
              className="block rounded-full"
              style={{
                width: `${(p.count / ceiling) * 100}%`,
                height: "100%",
                background: PRIORITY_COLOR[p.priority],
              }}
            />
          </span>
          <span className="t-num w-5 shrink-0 text-right text-[12px]">
            {p.count}
          </span>
        </li>
      ))}
    </ul>
  );
}

/* ── sprint progress ──────────────────────────────────────────
   One bar, one mark. The bar is how much of the sprint is done;
   the mark is how much of its calendar has gone. The whole "are
   we going to make it?" question is the gap between the two, so
   nothing else is drawn on it.
   ─────────────────────────────────────────────────────────── */

export function SprintProgress({
  progress,
  pace,
}: {
  progress: SprintReport["progress"];
  pace: SprintReport["pace"];
}) {
  const done = progress.donePercentByIssues;
  const expected = pace?.expectedPercent ?? null;
  // Coral is signal everywhere else on the page, so it is spent here only when
  // the work has actually fallen behind the calendar — never on the pace mark
  // itself, which is a neutral fact about the date.
  const behind = expected !== null && done < expected;

  return (
    <div>
      <div className="flex items-baseline justify-between gap-3">
        <span className="figure">{done}%</span>
        <span className="t-meta">
          {progress.issues.done} of {progress.issues.total} issues
        </span>
      </div>

      <div
        className="relative mt-2.5 overflow-hidden rounded-full"
        style={{ height: 10, background: "var(--color-rule-soft)" }}
        role="img"
        aria-label={
          expected === null
            ? `${done}% of this sprint's issues are done.`
            : `${done}% of this sprint's issues are done, with ${expected}% of the sprint elapsed.`
        }
      >
        <div
          className="h-full rounded-full"
          style={{
            width: `${done}%`,
            background: behind ? "var(--color-pink)" : "var(--color-blue)",
          }}
        />
        {/* The pace line rides over the fill rather than beside it: the reading
            that matters is which side of it the fill ends on. */}
        {expected !== null && (
          <span
            aria-hidden
            className="absolute top-0 bottom-0"
            style={{
              left: `${expected}%`,
              width: 2,
              background: "var(--color-ink)",
            }}
            title={`${expected}% of the sprint elapsed`}
          />
        )}
      </div>

      {pace && (
        <div className="t-meta mt-2 flex flex-wrap gap-x-4">
          <span>
            Day {pace.elapsedDays} of {pace.totalDays}
          </span>
          <span>
            {pace.remainingDays === 0
              ? "Last day"
              : `${pace.remainingDays} days left`}
          </span>
          <span>{pace.expectedPercent}% elapsed</span>
        </div>
      )}

      {progress.points.total > 0 && (
        <div className="t-meta mt-1 flex flex-wrap gap-x-4">
          <span>
            {progress.points.done} of {progress.points.total} points (
            {progress.donePercentByPoints}%)
          </span>
          {progress.points.unestimatedIssues > 0 && (
            <span>
              {progress.points.unestimatedIssues} unestimated
            </span>
          )}
        </div>
      )}
    </div>
  );
}

/* ── who is carrying it ───────────────────────────────────────
   A row a person, each row split done-then-open so the finished
   part of everyone's load lines up on the left and the eye can
   run down it. Bars are scaled against the busiest person, not
   against each row's own total, or a light load would look the
   same as a heavy one.
   ─────────────────────────────────────────────────────────── */

export function AssigneeLoad({
  byAssignee,
}: {
  byAssignee: SprintReport["byAssignee"];
}) {
  const ceiling = Math.max(...byAssignee.map((a) => a.total), 1);

  if (byAssignee.length === 0) {
    return (
      <p className="py-6 text-[13px] text-[var(--color-ink-soft)]">
        Nothing is in this sprint yet.
      </p>
    );
  }

  return (
    <ul className="space-y-2.5">
      {byAssignee.map((a) => (
        <li
          key={a.user?.id ?? "unassigned"}
          className="flex items-center gap-2.5"
        >
          <span
            className={cx(
              "w-[110px] shrink-0 truncate text-[12px]",
              a.user
                ? "text-[var(--color-ink)]"
                : "text-[var(--color-ink-faint)] italic",
            )}
            title={a.user?.displayName ?? "Unassigned"}
          >
            {a.user?.displayName ?? "Unassigned"}
          </span>

          <span
            className="flex min-w-0 flex-1 gap-px overflow-hidden rounded-full"
            style={{ height: 8, background: "var(--color-rule-soft)" }}
            title={`${a.done} done, ${a.open} open${a.blocked > 0 ? `, ${a.blocked} blocked` : ""}`}
          >
            <span
              style={{
                width: `${(a.done / ceiling) * 100}%`,
                background: "var(--color-blue-deep)",
              }}
            />
            <span
              style={{
                width: `${(a.open / ceiling) * 100}%`,
                background: "var(--color-blue-soft)",
              }}
            />
          </span>

          {a.blocked > 0 && (
            <span
              className="t-num shrink-0 text-[11px]"
              style={{ color: "var(--color-over)" }}
              title={`${a.blocked} blocked`}
            >
              {a.blocked} blocked
            </span>
          )}
          <span className="t-num w-14 shrink-0 text-right text-[12px]">
            {a.done}/{a.total}
          </span>
        </li>
      ))}
    </ul>
  );
}
