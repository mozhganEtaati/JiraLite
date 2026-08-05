"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { fullDate } from "@/lib/format";
import { useCursorList, useWorkspace } from "@/lib/hooks";
import type { AdminProject } from "@/lib/types";
import {
  Empty,
  ErrorNote,
  LoadMore,
  Loading,
  PageHead,
} from "@/components/kit";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export default function AdminProjectsPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);

  const projects = useCursorList<AdminProject>(
    ["admin-projects", workspaceId],
    `/api/workspaces/${workspaceId}/admin/projects`,
  );

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="Projects"
        meta="Every project in the workspace, with how much is in it."
        actions={
          <Button variant="outline" asChild>
            <Link href={`/w/${workspaceId}/projects`}>Project list</Link>
          </Button>
        }
      />

      <div className="card overflow-hidden">
        {projects.isLoading ? (
          <Loading label="Loading projects" />
        ) : projects.error ? (
          <div className="p-4">
            <ErrorNote error={projects.error} />
          </div>
        ) : projects.items.length === 0 ? (
          <Empty title="No projects in this workspace" />
        ) : (
          <>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[100px]">Key</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead className="w-[110px] text-right">Members</TableHead>
                  <TableHead className="w-[110px] text-right">Issues</TableHead>
                  <TableHead className="w-[130px]">Created</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {projects.items.map((p) => (
                  <TableRow key={p.projectId}>
                    <TableCell>
                      <span className="key">{p.key}</span>
                    </TableCell>
                    <TableCell>
                      <Link
                        href={`/p/${p.projectId}`}
                        className="font-medium hover:underline"
                      >
                        {p.name}
                      </Link>
                      {p.isArchived && (
                        <Badge variant="secondary" className="ml-2">
                          Archived
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className="t-num text-right">
                      {p.memberCount}
                    </TableCell>
                    <TableCell className="t-num text-right">
                      {p.issueCount}
                    </TableCell>
                    <TableCell className="t-meta">
                      {fullDate(p.createdAtUtc)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>

            <LoadMore
              hasNext={Boolean(projects.hasNextPage)}
              loading={projects.isFetchingNextPage}
              onClick={() => projects.fetchNextPage()}
            />
          </>
        )}
      </div>
    </>
  );
}
