"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useList, useWorkspace, useWorkspaceRole } from "@/lib/hooks";
import type { ProjectListItem } from "@/lib/types";
import { Empty, ErrorNote, Loading, PageHead } from "@/components/kit";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";

export default function WorkspaceProjectsPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);
  const { isAdmin } = useWorkspaceRole(workspaceId);
  const [showArchived, setShowArchived] = useState(false);
  const [creating, setCreating] = useState(false);

  const projects = useList<ProjectListItem>(
    ["workspace-projects", workspaceId],
    `/api/workspaces/${workspaceId}/projects`,
  );

  // The endpoint returns archived projects too; they are noise until asked for.
  const visible = showArchived
    ? projects.items
    : projects.items.filter((p) => !p.isArchived);
  const archivedCount = projects.items.filter((p) => p.isArchived).length;

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="Projects"
        meta="Every project has its own board, backlog and members."
        actions={
          isAdmin && (
            <Button onClick={() => setCreating(true)}>New project</Button>
          )
        }
      />

      {archivedCount > 0 && (
        <div className="mb-3 flex items-center gap-2.5">
          <Switch
            id="show-archived"
            checked={showArchived}
            onCheckedChange={setShowArchived}
          />
          <Label
            htmlFor="show-archived"
            className="text-[13px] font-normal text-[var(--color-ink-soft)]"
          >
            Show {archivedCount} archived
          </Label>
        </div>
      )}

      {projects.isLoading ? (
        <div className="card">
          <Loading label="Loading projects" />
        </div>
      ) : projects.error ? (
        <ErrorNote error={projects.error} />
      ) : visible.length === 0 ? (
        <div className="card">
          <Empty
            title="No projects here yet"
            hint={
              isAdmin
                ? "Create one to get a board, a backlog and somewhere to file issues."
                : "A workspace admin can create the first one."
            }
            action={
              isAdmin && (
                <Button size="sm" onClick={() => setCreating(true)}>
                  Create a project
                </Button>
              )
            }
          />
        </div>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {visible.map((p) => (
            <ProjectCard
              key={p.id}
              project={p}
              workspaceId={workspaceId}
              canManage={isAdmin}
            />
          ))}
        </ul>
      )}

      <CreateProjectDialog
        workspaceId={workspaceId}
        open={creating}
        onClose={() => setCreating(false)}
      />
    </>
  );
}

function ProjectCard({
  project,
  workspaceId,
  canManage,
}: {
  project: ProjectListItem;
  workspaceId: string;
  canManage: boolean;
}) {
  const qc = useQueryClient();

  const setArchived = useMutation({
    mutationFn: (archived: boolean) =>
      api.post(
        `/api/projects/${project.id}/${archived ? "archive" : "unarchive"}`,
      ),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ["workspace-projects", workspaceId] }),
  });

  return (
    <li className="plate flex flex-col p-3.5">
      <div className="flex items-start justify-between gap-2">
        <span className="key">{project.key}</span>
        <div className="flex items-center gap-1.5">
          {project.isArchived && <Badge variant="secondary">Archived</Badge>}
          {canManage && (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Actions for ${project.name}`}
                >
                  <DotsMark />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem asChild>
                  <Link href={`/p/${project.id}/settings`}>Settings</Link>
                </DropdownMenuItem>
                <DropdownMenuItem
                  onSelect={() => setArchived.mutate(!project.isArchived)}
                >
                  {project.isArchived ? "Unarchive" : "Archive"}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          )}
        </div>
      </div>

      <Link href={`/p/${project.id}`} className="mt-2 block">
        <h3 className="t-title truncate text-[15px]">{project.name}</h3>
        <p className="mt-1 line-clamp-2 min-h-[2.4em] text-[13px] text-[var(--color-ink-soft)]">
          {project.description || "No description."}
        </p>
      </Link>

      <div className="mt-3 flex items-center gap-2 border-t border-[var(--color-rule-soft)] pt-3">
        <Button variant="outline" size="sm" asChild>
          <Link href={`/p/${project.id}/board`}>Board</Link>
        </Button>
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/p/${project.id}/backlog`}>Backlog</Link>
        </Button>
      </div>

      <ErrorNote error={setArchived.error} className="mt-2" />
    </li>
  );
}

function CreateProjectDialog({
  workspaceId,
  open,
  onClose,
}: {
  workspaceId: string;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [keyTouched, setKeyTouched] = useState(false);

  const create = useMutation({
    mutationFn: () =>
      api.post(`/api/workspaces/${workspaceId}/projects`, {
        key,
        name,
        description: description || null,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspace-projects", workspaceId] });
      reset();
      onClose();
    },
  });

  function reset() {
    setKey("");
    setName("");
    setDescription("");
    setKeyTouched(false);
  }

  /**
   * The key is what every issue is named after (WEB-14), so it is derived from
   * the name until someone types one — then it is theirs and we stop guessing.
   */
  function onNameChange(value: string) {
    setName(value);
    if (!keyTouched) {
      setKey(
        value
          .replace(/[^A-Za-z0-9 ]/g, "")
          .trim()
          .split(/\s+/)
          .map((w) => w[0] ?? "")
          .join("")
          .toUpperCase()
          .slice(0, 10),
      );
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
    >
      <DialogContent className="sm:max-w-[460px]">
        <DialogHeader>
          <DialogTitle>New project</DialogTitle>
          <DialogDescription>
            The key prefixes every issue in it and cannot be changed later.
          </DialogDescription>
        </DialogHeader>

        <form
          id="new-project"
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <div className="space-y-2">
            <Label htmlFor="p-name">Name</Label>
            <Input
              id="p-name"
              value={name}
              onChange={(e) => onNameChange(e.target.value)}
              maxLength={200}
              autoFocus
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="p-key">Key</Label>
            <Input
              id="p-key"
              value={key}
              onChange={(e) => {
                setKeyTouched(true);
                setKey(e.target.value.toUpperCase());
              }}
              pattern="[A-Za-z][A-Za-z0-9]*"
              minLength={2}
              maxLength={10}
              required
              className="w-32 font-mono tracking-wide"
            />
            <p className="text-[12px] text-[var(--color-ink-faint)]">
              2–10 letters or digits, starting with a letter.
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="p-desc">Description</Label>
            <Textarea
              id="p-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={1000}
              rows={3}
            />
          </div>

          <ErrorNote error={create.error} />
        </form>

        <DialogFooter>
          <Button variant="outline" type="button" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="new-project"
            disabled={create.isPending || !name.trim() || key.length < 2}
          >
            {create.isPending ? "Creating…" : "Create project"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function DotsMark() {
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden>
      {[3, 7, 11].map((cy) => (
        <circle key={cy} cx="7" cy={cy} r="1.4" fill="currentColor" />
      ))}
    </svg>
  );
}
