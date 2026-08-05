"use client";

import Link from "next/link";
import { useState } from "react";
import { useCursorList } from "@/lib/hooks";
import { useSession } from "@/lib/providers";
import { ago, cx } from "@/lib/format";
import type { FeedEntry, MyProject, MyTask } from "@/lib/types";
import {
  Avatar,
  DueDate,
  IssueKey,
  PriorityMark,
  TypeMark,
} from "@/components/marks";
import { Empty, ErrorNote, Loading, LoadMore, PageHead, Section } from "@/components/kit";

export default function DashboardPage() {
  const { me } = useSession();
  const [includeDone, setIncludeDone] = useState(false);

  const tasks = useCursorList<MyTask>(
    ["my-tasks"],
    "/api/dashboard/my-tasks",
    { includeDone },
    { limit: 25 },
  );
  const projects = useCursorList<MyProject>(
    ["my-projects"],
    "/api/dashboard/my-projects",
    {},
    { limit: 50 },
  );
  const activity = useCursorList<FeedEntry>(
    ["recent-activity"],
    "/api/dashboard/recent-activity",
    {},
    { limit: 15 },
  );

  return (
    <>
      <PageHead
        eyebrow="My work"
        title={`Hello, ${me?.displayName?.split(" ")[0] ?? "there"}`}
        actions={
          <label className="flex cursor-pointer items-center gap-2 text-[13px] text-[var(--color-ink-soft)]">
            <input
              type="checkbox"
              checked={includeDone}
              onChange={(e) => setIncludeDone(e.target.checked)}
              className="accent-[var(--color-blue)]"
            />
            Show finished
          </label>
        }
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.55fr)_minmax(280px,1fr)]">
        <Section title="Assigned to me" count={tasks.items.length}>
          <div className="card overflow-hidden">
            {tasks.isLoading ? (
              <Loading label="Collecting your issues" />
            ) : tasks.error ? (
              <div className="p-3">
                <ErrorNote error={tasks.error} />
              </div>
            ) : tasks.items.length === 0 ? (
              <Empty
                title="Nothing is assigned to you"
                hint="Issues appear here the moment someone puts your name on one."
              />
            ) : (
              <ul className="sheet">
                {tasks.items.map((t) => (
                  <li key={t.id}>
                    <Link
                      href={`/i/${t.id}`}
                      className="row-hover flex items-center gap-3 px-3 py-2.5"
                    >
                      <TypeMark type={t.type} />
                      <IssueKey issueKey={t.key} />
                      <span className="min-w-0 flex-1 truncate">{t.title}</span>
                      <span className="chip hidden sm:inline-flex">
                        {t.columnName}
                      </span>
                      <PriorityMark priority={t.priority} />
                      <DueDate value={t.dueDateUtc} />
                    </Link>
                  </li>
                ))}
              </ul>
            )}
            <LoadMore
              hasNext={Boolean(tasks.hasNextPage)}
              loading={tasks.isFetchingNextPage}
              onClick={() => tasks.fetchNextPage()}
            />
          </div>
        </Section>

        <div className="space-y-6">
          <Section title="My projects" count={projects.items.length}>
            <div className="card overflow-hidden">
              {projects.items.length === 0 && !projects.isLoading ? (
                <Empty
                  title="No projects yet"
                  hint="Create a workspace, then a project inside it."
                  action={
                    <Link href="/orgs" className="btn btn-primary btn-sm">
                      Set up a workspace
                    </Link>
                  }
                />
              ) : (
                <ul className="sheet">
                  {projects.items.map((p) => (
                    <li key={p.id}>
                      <Link
                        href={`/p/${p.id}`}
                        className="row-hover flex items-center gap-2.5 px-3 py-2.5"
                      >
                        <IssueKey issueKey={p.key} />
                        <span className="min-w-0 flex-1 truncate">{p.name}</span>
                        <span className="chip">{p.role}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
              <LoadMore
                hasNext={Boolean(projects.hasNextPage)}
                loading={projects.isFetchingNextPage}
                onClick={() => projects.fetchNextPage()}
              />
            </div>
          </Section>

          <Section title="Recent activity">
            <div className="card overflow-hidden">
              {activity.items.length === 0 && !activity.isLoading ? (
                <Empty
                  title="Nothing has happened yet"
                  hint="Changes your teams make show up here."
                />
              ) : (
                <ul className="sheet max-h-[430px] overflow-y-auto">
                  {activity.items.map((a) => (
                    <li
                      key={a.id}
                      className="flex items-center gap-2 px-3 py-1.5"
                    >
                      <Avatar user={a.actor} size={18} />
                      <span className="min-w-0 flex-1 truncate text-[13px]">
                        <span className="font-medium">
                          {a.actor.displayName.split(" ")[0]}
                        </span>{" "}
                        <span className="text-[var(--color-ink-soft)]">
                          {a.summary}
                        </span>
                      </span>
                      <span
                        className="t-meta shrink-0 text-[11px]"
                        title={a.occurredAtUtc}
                      >
                        {ago(a.occurredAtUtc).replace(" ago", "")}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
              <LoadMore
                hasNext={Boolean(activity.hasNextPage)}
                loading={activity.isFetchingNextPage}
                onClick={() => activity.fetchNextPage()}
              />
            </div>
          </Section>
        </div>
      </div>
    </>
  );
}
