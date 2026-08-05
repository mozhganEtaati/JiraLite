"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { useWorkspace } from "@/lib/hooks";
import type { AdminOverview } from "@/lib/types";
import { ErrorNote, Loading, PageHead } from "@/components/kit";

export default function AdminOverviewPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);

  const overview = useQuery({
    queryKey: ["admin-overview", workspaceId],
    queryFn: () =>
      api.get<AdminOverview>(`/api/workspaces/${workspaceId}/admin/overview`),
  });

  const o = overview.data;

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="Admin overview"
        meta="What this workspace holds right now."
      />

      {overview.isLoading ? (
        <div className="card">
          <Loading label="Counting" />
        </div>
      ) : overview.error ? (
        <ErrorNote error={overview.error} />
      ) : (
        o && (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Stat
              label="People"
              value={o.memberCount}
              href={`/w/${workspaceId}/members`}
            />
            <Stat
              label="Teams"
              value={o.teamCount}
              href={`/w/${workspaceId}/teams`}
            />
            <Stat
              label="Projects"
              value={o.projectCount}
              note={`${o.activeProjectCount} active · ${o.archivedProjectCount} archived`}
              href={`/w/${workspaceId}/admin/projects`}
            />
            <Stat
              label="Invitations waiting"
              value={o.pendingInvitationCount}
              /* the one number that means someone is blocked on you */
              signal={o.pendingInvitationCount > 0}
              href={`/w/${workspaceId}/invitations`}
            />
          </div>
        )
      )}
    </>
  );
}

function Stat({
  label,
  value,
  note,
  href,
  signal,
}: {
  label: string;
  value: number;
  note?: string;
  href: string;
  signal?: boolean;
}) {
  return (
    <Link href={href} className="plate flex flex-col gap-1 px-4 py-3.5">
      <span className="text-[12px] font-semibold text-[var(--color-ink-soft)]">
        {label}
      </span>
      <span
        className="t-num text-[34px] leading-none"
        style={{ color: signal ? "var(--color-pink)" : "var(--color-ink)" }}
      >
        {value}
      </span>
      {/* the breakdown is data about the number above it, so it stays in mono */}
      <span className="t-meta mt-auto pt-1 text-[11px]">{note ?? " "}</span>
    </Link>
  );
}
