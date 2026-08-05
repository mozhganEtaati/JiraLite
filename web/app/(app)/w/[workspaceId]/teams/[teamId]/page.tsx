"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { api } from "@/lib/api";
import { fullDate } from "@/lib/format";
import { useList, useWorkspaceRole } from "@/lib/hooks";
import { useSession } from "@/lib/providers";
import type { Team, TeamMember, WorkspaceMember } from "@/lib/types";
import { Avatar } from "@/components/marks";
import { Empty, ErrorNote, Loading, PageHead } from "@/components/kit";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";

export default function TeamPage() {
  const { workspaceId, teamId } = useParams<{
    workspaceId: string;
    teamId: string;
  }>();
  const router = useRouter();
  const qc = useQueryClient();
  const { me } = useSession();
  const { isAdmin } = useWorkspaceRole(workspaceId);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const team = useQuery({
    queryKey: ["team", teamId],
    queryFn: () => api.get<Team>(`/api/teams/${teamId}`),
  });

  const members = team.data?.members ?? [];
  // Membership is managed by workspace admins and by the team's own leads.
  const iAmLead = members.some((m) => m.userId === me?.id && m.isLead);
  const canManageMembers = isAdmin || iAmLead;

  const remove = useMutation({
    mutationFn: (userId: string) =>
      api.del(`/api/teams/${teamId}/members/${userId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["team", teamId] }),
  });

  const setLead = useMutation({
    mutationFn: (vars: { userId: string; isLead: boolean }) =>
      api.patch(`/api/teams/${teamId}/members/${vars.userId}`, {
        isLead: vars.isLead,
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["team", teamId] }),
  });

  if (team.isLoading) return <Loading label="Loading team" />;
  if (team.error) return <ErrorNote error={team.error} />;

  return (
    <>
      <PageHead
        eyebrow={
          <Link href={`/w/${workspaceId}/teams`} className="hover:underline">
            ← Teams
          </Link>
        }
        title={team.data?.name ?? "Team"}
        meta={team.data?.description || "No description."}
        actions={
          <>
            {canManageMembers && (
              <Button onClick={() => setAdding(true)}>Add member</Button>
            )}
            {isAdmin && (
              <>
                <Button variant="outline" onClick={() => setEditing(true)}>
                  Rename
                </Button>
                <Button variant="ghost" onClick={() => setDeleting(true)}>
                  Delete
                </Button>
              </>
            )}
          </>
        }
      />

      <ErrorNote error={remove.error ?? setLead.error} className="mb-3" />

      <div className="card overflow-hidden">
        {members.length === 0 ? (
          <Empty
            title="Nobody on this team yet"
            hint={
              canManageMembers
                ? "Add people from this workspace."
                : "A lead or a workspace admin can add people."
            }
            action={
              canManageMembers && (
                <Button size="sm" onClick={() => setAdding(true)}>
                  Add someone
                </Button>
              )
            }
          />
        ) : (
          <ul className="sheet">
            {members.map((m) => (
              <li
                key={m.userId}
                className="flex flex-wrap items-center gap-3 px-4 py-3"
              >
                <Avatar
                  user={{ displayName: m.displayName, avatarUrl: m.avatarUrl }}
                  size={28}
                />
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">
                    {m.displayName}
                  </span>
                  <span className="t-meta">joined {fullDate(m.joinedAtUtc)}</span>
                </span>

                {canManageMembers ? (
                  <label className="flex items-center gap-2 text-[13px]">
                    <Switch
                      checked={m.isLead}
                      onCheckedChange={(isLead) =>
                        setLead.mutate({ userId: m.userId, isLead })
                      }
                      aria-label={`${m.displayName} is a lead`}
                    />
                    Lead
                  </label>
                ) : (
                  m.isLead && <Badge variant="secondary">Lead</Badge>
                )}

                {canManageMembers && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => remove.mutate(m.userId)}
                    disabled={remove.isPending}
                  >
                    Remove
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <AddMemberDialog
        workspaceId={workspaceId}
        teamId={teamId}
        already={members}
        open={adding}
        onClose={() => setAdding(false)}
      />

      {team.data && (
        /* keyed on the saved values, so the form resets when they change */
        <RenameTeamDialog
          key={`${team.data.name}|${team.data.description ?? ""}`}
          team={team.data}
          open={editing}
          onClose={() => setEditing(false)}
        />
      )}

      <DeleteTeamDialog
        teamId={teamId}
        name={team.data?.name ?? ""}
        open={deleting}
        onClose={() => setDeleting(false)}
        onDeleted={() => router.replace(`/w/${workspaceId}/teams`)}
      />
    </>
  );
}

function AddMemberDialog({
  workspaceId,
  teamId,
  already,
  open,
  onClose,
}: {
  workspaceId: string;
  teamId: string;
  already: TeamMember[];
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [userId, setUserId] = useState("");

  const people = useList<WorkspaceMember>(
    ["workspace-members", workspaceId],
    `/api/workspaces/${workspaceId}/members`,
    undefined,
    { enabled: open },
  );

  const candidates = useMemo(() => {
    const taken = new Set(already.map((m) => m.userId));
    return people.items.filter((p) => !taken.has(p.userId));
  }, [people.items, already]);

  const add = useMutation({
    mutationFn: () => api.post(`/api/teams/${teamId}/members`, { userId }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", teamId] });
      setUserId("");
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
      <DialogContent className="sm:max-w-[420px]">
        <DialogHeader>
          <DialogTitle>Add a member</DialogTitle>
          <DialogDescription>
            Only people already in this workspace can join a team.
          </DialogDescription>
        </DialogHeader>

        {candidates.length === 0 ? (
          <p className="text-[13px] text-[var(--color-ink-soft)]">
            Everyone in this workspace is already on the team.
          </p>
        ) : (
          <div className="space-y-2">
            <Label htmlFor="tm-person">Person</Label>
            <Select value={userId} onValueChange={setUserId}>
              <SelectTrigger id="tm-person" className="w-full">
                <SelectValue placeholder="Pick someone" />
              </SelectTrigger>
              <SelectContent>
                {candidates.map((p) => (
                  <SelectItem key={p.userId} value={p.userId}>
                    {p.displayName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

        <ErrorNote error={add.error} />

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={() => add.mutate()}
            disabled={!userId || add.isPending}
          >
            {add.isPending ? "Adding…" : "Add to team"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RenameTeamDialog({
  team,
  open,
  onClose,
}: {
  team: Team;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [name, setName] = useState(team.name);
  const [description, setDescription] = useState(team.description ?? "");

  const save = useMutation({
    mutationFn: () =>
      api.patch(`/api/teams/${team.id}`, {
        name,
        description: description || null,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["team", team.id] });
      qc.invalidateQueries({ queryKey: ["teams", team.workspaceId] });
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
          <DialogTitle>Rename team</DialogTitle>
        </DialogHeader>

        <form
          id="rename-team"
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          <div className="space-y-2">
            <Label htmlFor="rt-name">Name</Label>
            <Input
              id="rt-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={100}
              required
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="rt-desc">Description</Label>
            <Textarea
              id="rt-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={500}
              rows={3}
            />
          </div>
          <ErrorNote error={save.error} />
        </form>

        <DialogFooter>
          <Button variant="outline" type="button" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="rename-team"
            disabled={save.isPending || !name.trim()}
          >
            {save.isPending ? "Saving…" : "Save"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function DeleteTeamDialog({
  teamId,
  name,
  open,
  onClose,
  onDeleted,
}: {
  teamId: string;
  name: string;
  open: boolean;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const qc = useQueryClient();

  const del = useMutation({
    mutationFn: () => api.del(`/api/teams/${teamId}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["teams"] });
      onDeleted();
    },
  });

  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete the team “{name}”?</AlertDialogTitle>
          <AlertDialogDescription>
            The team disappears; the people in it keep their workspace access and
            everything they have worked on.
          </AlertDialogDescription>
        </AlertDialogHeader>

        <ErrorNote error={del.error} />

        <AlertDialogFooter>
          <AlertDialogCancel disabled={del.isPending}>Keep it</AlertDialogCancel>
          <AlertDialogAction
            onClick={(e) => {
              e.preventDefault();
              del.mutate();
            }}
            disabled={del.isPending}
          >
            {del.isPending ? "Deleting…" : "Delete team"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
