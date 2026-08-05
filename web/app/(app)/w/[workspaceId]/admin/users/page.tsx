"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { fullDate } from "@/lib/format";
import { useCursorList, useWorkspace } from "@/lib/hooks";
import type { AdminUser } from "@/lib/types";
import { Avatar } from "@/components/marks";
import {
  Empty,
  ErrorNote,
  LoadMore,
  Loading,
  PageHead,
} from "@/components/kit";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export default function AdminUsersPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);

  const users = useCursorList<AdminUser>(
    ["admin-users", workspaceId],
    `/api/workspaces/${workspaceId}/admin/users`,
  );

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="People"
        meta="Everyone in the workspace, and what they can reach inside it."
      />

      <div className="card overflow-hidden">
        {users.isLoading ? (
          <Loading label="Loading people" />
        ) : users.error ? (
          <div className="p-4">
            <ErrorNote error={users.error} />
          </div>
        ) : users.items.length === 0 ? (
          <Empty title="Nobody here yet" />
        ) : (
          <>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Person</TableHead>
                  <TableHead className="w-[110px]">Workspace</TableHead>
                  <TableHead>Projects</TableHead>
                  <TableHead className="w-[130px]">Joined</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {users.items.map((u) => (
                  <TableRow key={u.userId}>
                    <TableCell>
                      <div className="flex items-center gap-2.5">
                        <Avatar
                          user={{
                            displayName: u.displayName,
                            avatarUrl: u.avatarUrl,
                          }}
                          size={26}
                        />
                        <span className="min-w-0">
                          <span className="block truncate font-medium">
                            {u.displayName}
                          </span>
                          <span className="t-meta block truncate">
                            {u.email}
                          </span>
                        </span>
                        {!u.isActive && (
                          <Badge variant="secondary">Deactivated</Badge>
                        )}
                      </div>
                    </TableCell>

                    <TableCell>
                      <span
                        className={
                          u.workspaceRole === "Admin" ? "chip chip-ink" : "chip"
                        }
                      >
                        {u.workspaceRole}
                      </span>
                    </TableCell>

                    <TableCell>
                      {u.projectRoles.length === 0 ? (
                        <span className="text-[13px] text-[var(--color-ink-faint)]">
                          None
                        </span>
                      ) : (
                        <div className="flex flex-wrap gap-1.5">
                          {u.projectRoles.map((r) => (
                            <Link
                              key={r.projectId}
                              href={`/p/${r.projectId}/settings/members`}
                              className="chip hover:border-[var(--color-blue-soft)]"
                              title={`${r.role} on ${r.projectKey}`}
                            >
                              <span className="font-mono">{r.projectKey}</span>
                              <span className="text-[var(--color-ink-faint)]">
                                {r.role}
                              </span>
                            </Link>
                          ))}
                        </div>
                      )}
                    </TableCell>

                    <TableCell className="t-meta">
                      {fullDate(u.joinedAtUtc)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>

            <LoadMore
              hasNext={Boolean(users.hasNextPage)}
              loading={users.isFetchingNextPage}
              onClick={() => users.fetchNextPage()}
            />
          </>
        )}
      </div>
    </>
  );
}
