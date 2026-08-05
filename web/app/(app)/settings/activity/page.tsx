"use client";

import Link from "next/link";
import { ago, fullDate } from "@/lib/format";
import { useCursorList } from "@/lib/hooks";
import type { ActivityEntry } from "@/lib/types";
import {
  Empty,
  ErrorNote,
  LoadMore,
  Loading,
  PageHead,
} from "@/components/kit";

/** Only issues have a screen of their own to link back to. */
function hrefFor(entry: ActivityEntry) {
  return entry.entityType === "Issue" ? `/i/${entry.entityId}` : null;
}

export default function MyActivityPage() {
  const activity = useCursorList<ActivityEntry>(
    ["my-activity"],
    "/api/users/me/activity",
  );

  return (
    <>
      <PageHead
        eyebrow="Account"
        title="My activity"
        meta="Everything you have changed, newest first."
      />

      <div className="card overflow-hidden">
        {activity.isLoading ? (
          <Loading label="Loading your activity" />
        ) : activity.error ? (
          <div className="p-4">
            <ErrorNote error={activity.error} />
          </div>
        ) : activity.items.length === 0 ? (
          <Empty
            title="Nothing recorded yet"
            hint="Create or change something and it shows up here."
          />
        ) : (
          <>
            {/* a real sequence, so it gets the connected node treatment */}
            <ul className="timeline py-2">
              {activity.items.map((entry) => {
                const href = hrefFor(entry);
                const body = (
                  <>
                    <span className="min-w-0 flex-1">
                      <span className="block">{entry.summary}</span>
                      <span className="t-meta">
                        {entry.entityType} · {entry.action}
                      </span>
                    </span>
                    <time
                      className="t-meta shrink-0"
                      dateTime={entry.occurredAtUtc}
                      title={fullDate(entry.occurredAtUtc)}
                    >
                      {ago(entry.occurredAtUtc)}
                    </time>
                  </>
                );

                return (
                  <li key={entry.id} className="ml-4">
                    {href ? (
                      <Link
                        href={href}
                        className="row-hover flex items-start gap-4 rounded-[var(--radius-md)] px-3 py-2.5"
                      >
                        {body}
                      </Link>
                    ) : (
                      <div className="flex items-start gap-4 px-3 py-2.5">
                        {body}
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>

            <LoadMore
              hasNext={Boolean(activity.hasNextPage)}
              loading={activity.isFetchingNextPage}
              onClick={() => activity.fetchNextPage()}
            />
          </>
        )}
      </div>
    </>
  );
}
