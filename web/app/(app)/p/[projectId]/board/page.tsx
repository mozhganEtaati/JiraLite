"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { api } from "@/lib/api";
import { useList, useProjectRole } from "@/lib/hooks";
import type { Board, BoardColumnGroup, BoardItem } from "@/lib/types";
import { BoardCanvas } from "@/components/board";
import { Empty, ErrorNote, Loading, PageHead } from "@/components/ui";
import { NewIssueButton } from "@/components/new-issue";

function BoardView() {
  const { projectId } = useParams<{ projectId: string }>();
  const params = useSearchParams();
  const router = useRouter();
  const { canWrite, canAdmin } = useProjectRole(projectId);

  const boards = useList<BoardItem>(
    ["boards", projectId],
    `/api/projects/${projectId}/boards`,
  );

  const boardId = params.get("board") ?? boards.items[0]?.id;

  const board = useQuery({
    queryKey: ["board", boardId],
    enabled: Boolean(boardId),
    queryFn: () => api.get<Board>(`/api/boards/${boardId}`),
  });

  const issues = useQuery({
    queryKey: ["board-issues", boardId],
    enabled: Boolean(boardId),
    queryFn: () =>
      api.get<{ columns: BoardColumnGroup[] }>(`/api/boards/${boardId}/issues`),
  });

  if (boards.isLoading) return <Loading label="Loading boards" />;

  if (boards.items.length === 0) {
    return (
      <Empty
        title="This project has no board yet"
        hint="A board holds the columns your issues move through."
        action={
          canAdmin ? (
            <Link href={`/p/${projectId}/boards`} className="btn btn-primary btn-sm">
              Create a board
            </Link>
          ) : undefined
        }
      />
    );
  }

  return (
    <>
      <PageHead
        eyebrow="Board"
        title={board.data?.name ?? "Board"}
        actions={
          <>
            {boards.items.length > 1 && (
              <select
                className="field w-auto"
                value={boardId}
                onChange={(e) =>
                  router.replace(
                    `/p/${projectId}/board?board=${e.target.value}`,
                  )
                }
                aria-label="Choose board"
              >
                {boards.items.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name} · {b.type}
                  </option>
                ))}
              </select>
            )}
            {canAdmin && board.data && (
              <Link
                href={`/p/${projectId}/boards/${board.data.id}/columns`}
                className="btn btn-ghost"
              >
                Edit columns
              </Link>
            )}
            {canWrite && <NewIssueButton projectId={projectId} />}
          </>
        }
      />

      {board.error && <ErrorNote error={board.error} className="mb-3" />}
      {issues.error && <ErrorNote error={issues.error} className="mb-3" />}

      {board.data && issues.data ? (
        <BoardCanvas
          board={board.data}
          groups={issues.data.columns}
          canWrite={canWrite}
        />
      ) : (
        <Loading label="Loading the board" />
      )}
    </>
  );
}

export default function BoardPage() {
  return (
    <Suspense fallback={<Loading />}>
      <BoardView />
    </Suspense>
  );
}
