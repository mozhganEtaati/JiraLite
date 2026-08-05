"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { fullDate } from "@/lib/format";
import { useWorkspace, useWorkspaceRole } from "@/lib/hooks";
import type { Workspace } from "@/lib/types";
import { ErrorNote, Loading, PageHead, Section } from "@/components/kit";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";

export default function WorkspaceSettingsPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);
  const { isAdmin } = useWorkspaceRole(workspaceId);

  if (workspace.isLoading) return <Loading label="Loading workspace" />;
  if (workspace.error) return <ErrorNote error={workspace.error} />;

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="Workspace settings"
        meta={
          workspace.data
            ? `Created ${fullDate(workspace.data.createdAtUtc)}${
                workspace.data.isArchived ? " · archived" : ""
              }`
            : undefined
        }
      />

      <div className="grid gap-6 lg:grid-cols-2">
        <Section title="Details">
          {workspace.data && (
            /*
             * Keyed on the saved values: the form starts from what the server
             * holds, and a change made elsewhere resets it by remounting
             * rather than by an effect that would fight whoever is typing.
             */
            <DetailsCard
              key={`${workspace.data.name}|${workspace.data.description ?? ""}`}
              workspace={workspace.data}
              canEdit={isAdmin}
            />
          )}
        </Section>

        <Section title="Leaving and archiving">
          <div className="space-y-3">
            <LeaveCard workspaceId={workspaceId} />
            {isAdmin && !workspace.data?.isArchived && (
              <ArchiveCard
                workspaceId={workspaceId}
                name={workspace.data?.name ?? ""}
              />
            )}
          </div>
        </Section>
      </div>
    </>
  );
}

function DetailsCard({
  workspace,
  canEdit,
}: {
  workspace: Workspace;
  canEdit: boolean;
}) {
  const qc = useQueryClient();
  const [name, setName] = useState(workspace.name);
  const [description, setDescription] = useState(workspace.description ?? "");

  const save = useMutation({
    mutationFn: () =>
      api.patch(`/api/workspaces/${workspace.id}`, {
        name,
        description: description || null,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspace", workspace.id] });
      qc.invalidateQueries({ queryKey: ["workspaces"] });
    },
  });

  const dirty =
    name !== workspace.name || description !== (workspace.description ?? "");

  return (
    <form
      className="card space-y-4 p-4"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      <div className="space-y-2">
        <Label htmlFor="ws-name">Name</Label>
        <Input
          id="ws-name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={200}
          disabled={!canEdit}
          required
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="ws-desc">Description</Label>
        <Textarea
          id="ws-desc"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          maxLength={1000}
          rows={3}
          disabled={!canEdit}
        />
      </div>

      <ErrorNote error={save.error} />

      {canEdit ? (
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={!dirty || save.isPending || !name.trim()}>
            {save.isPending ? "Saving…" : "Save changes"}
          </Button>
          {save.isSuccess && !dirty && (
            <span className="t-meta" role="status">
              Saved.
            </span>
          )}
        </div>
      ) : (
        <p className="text-[13px] text-[var(--color-ink-faint)]">
          Only a workspace admin can change these.
        </p>
      )}
    </form>
  );
}

function LeaveCard({ workspaceId }: { workspaceId: string }) {
  const router = useRouter();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);

  const leave = useMutation({
    mutationFn: () => api.post(`/api/workspaces/${workspaceId}/leave`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspaces"] });
      router.replace("/orgs");
    },
  });

  return (
    <div className="card p-4">
      <h3 className="font-medium">Leave this workspace</h3>
      <p className="mt-1 text-[13px] text-[var(--color-ink-soft)]">
        You lose access to its projects. If you are the only admin, the server
        will refuse — hand the role to someone else first.
      </p>

      <ErrorNote error={leave.error} className="mt-3" />

      <Button variant="outline" className="mt-3" onClick={() => setOpen(true)}>
        Leave workspace
      </Button>

      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Leave this workspace?</AlertDialogTitle>
            <AlertDialogDescription>
              You will need a new invitation to come back.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={leave.isPending}>Stay</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault();
                leave.mutate();
              }}
              disabled={leave.isPending}
            >
              {leave.isPending ? "Leaving…" : "Leave"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

function ArchiveCard({
  workspaceId,
  name,
}: {
  workspaceId: string;
  name: string;
}) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);

  const archive = useMutation({
    mutationFn: () => api.post(`/api/workspaces/${workspaceId}/archive`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspace", workspaceId] });
      qc.invalidateQueries({ queryKey: ["workspaces"] });
      setOpen(false);
    },
  });

  return (
    <div className="card p-4">
      <h3 className="font-medium">Archive this workspace</h3>
      <p className="mt-1 text-[13px] text-[var(--color-ink-soft)]">
        It becomes read-only for everyone. Nothing is deleted.
      </p>

      <ErrorNote error={archive.error} className="mt-3" />

      <Button variant="outline" className="mt-3" onClick={() => setOpen(true)}>
        Archive workspace
      </Button>

      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive “{name}”?</AlertDialogTitle>
            <AlertDialogDescription>
              Everyone keeps their access, but nobody can change anything in it
              until it is brought back.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={archive.isPending}>
              Keep it open
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault();
                archive.mutate();
              }}
              disabled={archive.isPending}
            >
              {archive.isPending ? "Archiving…" : "Archive"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
