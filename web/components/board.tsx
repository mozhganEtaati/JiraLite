"use client";

import {
  DndContext,
  DragOverlay,
  PointerSensor,
  closestCorners,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo, useState } from "react";
import { api } from "@/lib/api";
import { cx } from "@/lib/format";
import type {
  Board,
  BoardCardIssue,
  BoardColumnGroup,
  Issue,
} from "@/lib/types";
import { Avatar, PriorityMark, TypeMark } from "@/components/marks";

type Lanes = Record<string, BoardCardIssue[]>;

function buildLanes(columns: Board["columns"], groups: BoardColumnGroup[]) {
  const lanes: Lanes = {};
  for (const c of columns) lanes[c.id] = [];
  for (const g of groups) lanes[g.columnId] = g.issues;
  return lanes;
}

export function BoardCanvas({
  board,
  groups,
  canWrite,
}: {
  board: Board;
  groups: BoardColumnGroup[];
  canWrite: boolean;
}) {
  const qc = useQueryClient();
  const [lanes, setLanes] = useState<Lanes>(() =>
    buildLanes(board.columns, groups),
  );
  const [dragging, setDragging] = useState<BoardCardIssue | null>(null);

  /*
   * A drag reorders the lanes locally so the card follows the pointer, then
   * the server answer arrives as fresh props and takes over again. Rebuilding
   * during render rather than in an effect means the board never paints one
   * frame of the stale arrangement.
   */
  const [builtFrom, setBuiltFrom] = useState({ columns: board.columns, groups });
  if (builtFrom.columns !== board.columns || builtFrom.groups !== groups) {
    setBuiltFrom({ columns: board.columns, groups });
    setLanes(buildLanes(board.columns, groups));
  }

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
  );

  const laneOf = (issueId: string) =>
    Object.keys(lanes).find((c) => lanes[c].some((i) => i.id === issueId));

  const move = useMutation({
    mutationFn: async (v: {
      issueId: string;
      columnId: string;
      afterIssueId: string | null;
      changedColumn: boolean;
    }) => {
      // Neither the board payload nor the rank endpoint carries a row version,
      // so read the issue first and hand its version back with the write.
      let issue = await api.get<Issue>(`/api/issues/${v.issueId}`);
      if (v.changedColumn) {
        const res = await api.patch<{ rowVersion: string }>(
          `/api/issues/${v.issueId}/move`,
          { boardColumnId: v.columnId, rowVersion: issue.rowVersion },
        );
        issue = { ...issue, rowVersion: res.rowVersion };
      }
      await api.patch(`/api/issues/${v.issueId}/rank`, {
        afterIssueId: v.afterIssueId,
        rowVersion: issue.rowVersion,
      });
    },
    onSettled: () => {
      qc.invalidateQueries({ queryKey: ["board-issues", board.id] });
      qc.invalidateQueries({ queryKey: ["backlog"] });
    },
  });

  function onDragStart(e: DragStartEvent) {
    const id = String(e.active.id);
    const lane = laneOf(id);
    setDragging(lanes[lane!]?.find((i) => i.id === id) ?? null);
  }

  function onDragOver(e: DragOverEvent) {
    const { active, over } = e;
    if (!over) return;
    const from = laneOf(String(active.id));
    const to = lanes[String(over.id)] ? String(over.id) : laneOf(String(over.id));
    if (!from || !to || from === to) return;

    setLanes((prev) => {
      const card = prev[from].find((i) => i.id === String(active.id));
      if (!card) return prev;
      const overIndex = prev[to].findIndex((i) => i.id === String(over.id));
      const insertAt = overIndex >= 0 ? overIndex : prev[to].length;
      return {
        ...prev,
        [from]: prev[from].filter((i) => i.id !== card.id),
        [to]: [
          ...prev[to].slice(0, insertAt),
          card,
          ...prev[to].slice(insertAt),
        ],
      };
    });
  }

  function onDragEnd(e: DragEndEvent) {
    const { active, over } = e;
    setDragging(null);
    if (!over) return;

    const issueId = String(active.id);
    const to = lanes[String(over.id)] ? String(over.id) : laneOf(String(over.id));
    if (!to) return;

    const oldIndex = lanes[to].findIndex((i) => i.id === issueId);
    const overIndex = lanes[to].findIndex((i) => i.id === String(over.id));
    const newIndex = overIndex >= 0 ? overIndex : lanes[to].length - 1;

    const ordered =
      oldIndex >= 0 && oldIndex !== newIndex
        ? arrayMove(lanes[to], oldIndex, newIndex)
        : lanes[to];
    setLanes((prev) => ({ ...prev, [to]: ordered }));

    const at = ordered.findIndex((i) => i.id === issueId);
    const original = groups.find((g) => g.issues.some((i) => i.id === issueId));

    move.mutate({
      issueId,
      columnId: to,
      afterIssueId: at > 0 ? ordered[at - 1].id : null,
      changedColumn: original?.columnId !== to,
    });
  }

  const total = useMemo(
    () => Object.values(lanes).reduce((n, l) => n + l.length, 0),
    [lanes],
  );

  return (
    <DndContext
      sensors={canWrite ? sensors : []}
      collisionDetection={closestCorners}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDragEnd={onDragEnd}
    >
      <div className="flex gap-3 overflow-x-auto pb-3">
        {board.columns
          .slice()
          .sort((a, b) => a.displayOrder - b.displayOrder)
          .map((c) => (
            <Lane
              key={c.id}
              id={c.id}
              name={c.name}
              isDone={c.isDoneColumn}
              issues={lanes[c.id] ?? []}
              draggable={canWrite}
            />
          ))}
      </div>

      <DragOverlay dropAnimation={null}>
        {dragging && <Card issue={dragging} overlay />}
      </DragOverlay>

      <p className="t-meta mt-1">
        {total} {total === 1 ? "issue" : "issues"} on this board
        {canWrite ? " · drag a card to change its column" : ""}
      </p>
    </DndContext>
  );
}

function Lane({
  id,
  name,
  isDone,
  issues,
  draggable,
}: {
  id: string;
  name: string;
  isDone: boolean;
  issues: BoardCardIssue[];
  draggable: boolean;
}) {
  const { setNodeRef, isOver } = useDroppable({ id });

  return (
    <section
      ref={setNodeRef}
      className={cx(
        "flex w-[286px] shrink-0 flex-col rounded-[3px] border p-2 transition-colors",
        isOver
          ? "border-[var(--color-pink)] bg-[var(--color-pink-wash)]"
          : "border-[var(--color-rule)] bg-[var(--color-paper-deep)]",
      )}
    >
      <header className="mb-2 flex items-center justify-between px-1">
        <h3 className="t-eyebrow flex items-center gap-1.5 text-[10px]">
          {name}
          {isDone && (
            <span
              aria-label="done column"
              style={{
                width: 6,
                height: 6,
                borderRadius: 99,
                background: "var(--color-blue)",
                display: "inline-block",
              }}
            />
          )}
        </h3>
        <span className="t-num text-[11px] text-[var(--color-ink-faint)]">
          {issues.length}
        </span>
      </header>

      <SortableContext
        items={issues.map((i) => i.id)}
        strategy={verticalListSortingStrategy}
      >
        <div className="flex min-h-[52px] flex-col gap-2">
          {issues.map((i) => (
            <SortableCard key={i.id} issue={i} draggable={draggable} />
          ))}
          {issues.length === 0 && (
            <p className="px-1 py-3 text-center text-[12px] text-[var(--color-ink-faint)]">
              Nothing here
            </p>
          )}
        </div>
      </SortableContext>
    </section>
  );
}

function SortableCard({
  issue,
  draggable,
}: {
  issue: BoardCardIssue;
  draggable: boolean;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: issue.id, disabled: !draggable });

  return (
    <div
      ref={setNodeRef}
      style={{ transform: CSS.Translate.toString(transform), transition }}
      className={cx(isDragging && "opacity-25")}
      {...attributes}
      {...listeners}
    >
      <Card issue={issue} />
    </div>
  );
}

function Card({
  issue,
  overlay,
}: {
  issue: BoardCardIssue;
  overlay?: boolean;
}) {
  return (
    <article
      className="plate p-2.5"
      data-slip={overlay ? "true" : undefined}
      style={overlay ? { rotate: "-1.2deg" } : undefined}
    >
      <p className="text-[13px] leading-snug">{issue.title}</p>
      <div className="mt-2 flex items-center gap-2">
        <TypeMark type={issue.type} size={11} />
        <Link
          href={`/i/${issue.id}`}
          className="key hover:underline"
          onClick={(e) => e.stopPropagation()}
        >
          {issue.key}
        </Link>
        <PriorityMark priority={issue.priority} />
        {/* A blocked card that looks like every other card is the problem
            restated, so the mark rides on the board itself. */}
        {issue.isBlocked && <span className="chip chip-signal">Blocked</span>}
        <span className="ml-auto">
          <Avatar user={issue.assignee} size={20} />
        </span>
      </div>
    </article>
  );
}
