"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { api } from "@/lib/api";
import { useCursorList } from "@/lib/hooks";
import { ago, cx } from "@/lib/format";
import type { NotificationItem } from "@/lib/types";
import {
  Empty,
  ErrorNote,
  Loading,
  LoadMore,
  PageHead,
} from "@/components/kit";

const LABELS: Record<NotificationItem["type"], string> = {
  IssueAssigned: "Assigned to you",
  IssueStatusChanged: "Status changed",
  CommentAdded: "New comment",
};

export default function NotificationsPage() {
  const qc = useQueryClient();

  const list = useCursorList<NotificationItem>(
    ["notifications"],
    "/api/notifications",
    {},
    { limit: 30 },
  );

  const { data: unread } = useQuery({
    queryKey: ["unread-count"],
    queryFn: () =>
      api.get<{ unreadCount: number }>("/api/notifications/unread-count"),
  });

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["notifications"] });
    qc.invalidateQueries({ queryKey: ["unread-count"] });
  };

  const markRead = useMutation({
    mutationFn: (id: string) => api.patch(`/api/notifications/${id}/read`),
    onSuccess: refresh,
  });

  const markAll = useMutation({
    mutationFn: () => api.post("/api/notifications/read-all"),
    onSuccess: refresh,
  });

  return (
    <>
      <PageHead
        eyebrow="Notifications"
        title="What happened while you were away"
        meta={
          unread?.unreadCount
            ? `${unread.unreadCount} unread`
            : "Everything is read"
        }
        actions={
          Boolean(unread?.unreadCount) && (
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => markAll.mutate()}
              disabled={markAll.isPending}
            >
              {markAll.isPending ? "Marking…" : "Mark all read"}
            </button>
          )
        }
      />

      <ErrorNote error={markAll.error ?? markRead.error} className="mb-3" />

      <div className="card max-w-[840px] overflow-hidden">
        {list.isLoading ? (
          <Loading />
        ) : list.items.length === 0 ? (
          <Empty
            title="No notifications"
            hint="You are told when an issue is assigned to you, moves column, or gets a comment."
          />
        ) : (
          <ul className="sheet">
            {list.items.map((n) => (
              <li
                key={n.id}
                className={cx(
                  "flex items-center gap-3 px-3 py-2.5",
                  !n.isRead && "bg-[var(--color-pink-wash)]",
                )}
              >
                <span
                  aria-hidden
                  style={{
                    width: 6,
                    height: 6,
                    borderRadius: 99,
                    flexShrink: 0,
                    background: n.isRead
                      ? "var(--color-rule)"
                      : "var(--color-pink)",
                  }}
                />
                <span className="chip shrink-0">{LABELS[n.type] ?? n.type}</span>

                {n.entityType === "Issue" ? (
                  <Link
                    href={`/i/${n.entityId}`}
                    className="min-w-0 flex-1 truncate text-[13px] hover:underline"
                  >
                    {n.summary}
                  </Link>
                ) : (
                  <span className="min-w-0 flex-1 truncate text-[13px]">
                    {n.summary}
                  </span>
                )}

                <span className="t-meta shrink-0 text-[11px]">
                  {ago(n.createdAtUtc)}
                </span>

                {!n.isRead && (
                  <button
                    type="button"
                    className="btn btn-bare btn-sm shrink-0"
                    onClick={() => markRead.mutate(n.id)}
                  >
                    Mark read
                  </button>
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
    </>
  );
}
