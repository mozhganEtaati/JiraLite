"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { fullDate } from "@/lib/format";
import { useList, useWorkspace, useWorkspaceRole } from "@/lib/hooks";
import type { TeamItem } from "@/lib/types";
import { Empty, ErrorNote, Loading, PageHead } from "@/components/kit";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";

export default function TeamsPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);
  const { isAdmin } = useWorkspaceRole(workspaceId);
  const [creating, setCreating] = useState(false);

  const teams = useList<TeamItem>(
    ["teams", workspaceId],
    `/api/workspaces/${workspaceId}/teams`,
  );

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="Teams"
        meta="A team is a group of people you can talk about as one."
        actions={isAdmin && <Button onClick={() => setCreating(true)}>New team</Button>}
      />

      <div className="card overflow-hidden">
        {teams.isLoading ? (
          <Loading label="Loading teams" />
        ) : teams.error ? (
          <div className="p-4">
            <ErrorNote error={teams.error} />
          </div>
        ) : teams.items.length === 0 ? (
          <Empty
            title="No teams yet"
            hint={
              isAdmin
                ? "Group people into a team to keep track of who works together."
                : "A workspace admin can create the first one."
            }
            action={
              isAdmin && (
                <Button size="sm" onClick={() => setCreating(true)}>
                  Create a team
                </Button>
              )
            }
          />
        ) : (
          <ul className="sheet">
            {teams.items.map((t) => (
              <li key={t.id}>
                <Link
                  href={`/w/${workspaceId}/teams/${t.id}`}
                  className="row-hover flex items-center justify-between gap-4 px-4 py-3"
                >
                  <span className="min-w-0">
                    <span className="block truncate font-medium">{t.name}</span>
                    <span className="block truncate text-[13px] text-[var(--color-ink-soft)]">
                      {t.description || "No description."}
                    </span>
                  </span>
                  <span className="t-meta shrink-0">
                    {fullDate(t.createdAtUtc)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>

      <CreateTeamDialog
        workspaceId={workspaceId}
        open={creating}
        onClose={() => setCreating(false)}
      />
    </>
  );
}

function CreateTeamDialog({
  workspaceId,
  open,
  onClose,
}: {
  workspaceId: string;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  const create = useMutation({
    mutationFn: () =>
      api.post(`/api/workspaces/${workspaceId}/teams`, {
        name,
        description: description || null,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teams", workspaceId] });
      setName("");
      setDescription("");
      onClose();
    },
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
    >
      <DialogContent className="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle>New team</DialogTitle>
          <DialogDescription>
            You can add people to it once it exists.
          </DialogDescription>
        </DialogHeader>

        <form
          id="new-team"
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <div className="space-y-2">
            <Label htmlFor="t-name">Name</Label>
            <Input
              id="t-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={100}
              autoFocus
              required
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="t-desc">Description</Label>
            <Textarea
              id="t-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={500}
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
            form="new-team"
            disabled={create.isPending || !name.trim()}
          >
            {create.isPending ? "Creating…" : "Create team"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
