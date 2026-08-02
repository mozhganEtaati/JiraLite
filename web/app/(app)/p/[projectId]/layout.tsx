"use client";

import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import { useEffect } from "react";
import { api } from "@/lib/api";
import { useCurrentWorkspace, ProjectTabs } from "@/components/shell";
import { Loading } from "@/components/ui";
import type { Project } from "@/lib/types";

export default function ProjectLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const { projectId } = useParams<{ projectId: string }>();
  const { setWorkspaceId } = useCurrentWorkspace();

  const { data: project, isLoading } = useQuery({
    queryKey: ["project", projectId],
    queryFn: () => api.get<Project>(`/api/projects/${projectId}`),
  });

  // Opening a project deep link should also move the rail into its workspace.
  useEffect(() => {
    if (project?.workspaceId) setWorkspaceId(project.workspaceId);
  }, [project?.workspaceId, setWorkspaceId]);

  if (isLoading) return <Loading label="Opening project" />;

  return (
    <>
      <ProjectTabs projectId={projectId} />
      {children}
    </>
  );
}
