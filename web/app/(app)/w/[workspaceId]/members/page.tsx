"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { fullDate } from "@/lib/format";
import { useList, useWorkspace, useWorkspaceRole } from "@/lib/hooks";
import { useSession } from "@/lib/providers";
import type { WorkspaceMember, WorkspaceRoleName } from "@/lib/types";
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export default function WorkspaceMembersPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);
  const { isAdmin } = useWorkspaceRole(workspaceId);
  const { me } = useSession();
  const [removing, setRemoving] = useState<WorkspaceMember | null>(null);

  const members = useList<WorkspaceMember>(
    ["workspace-members", workspaceId],
    `/api/workspaces/${workspaceId}/members`,
  );

  const admins = members.items.filter((m) => m.role === "Admin").length;

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="Members"
        meta={`${members.items.length} ${
          members.items.length === 1 ? "person" : "people"
        } · ${admins} admin${admins === 1 ? "" : "s"}`}
        actions={
          isAdmin && (
            <Button asChild>
              <Link href={`/w/${workspaceId}/invitations`}>Invite someone</Link>
            </Button>
          )
        }
      />

      <div className="card overflow-hidden">
        {members.isLoading ? (
          <Loading label="Loading members" />
        ) : members.error ? (
          <div className="p-4">
            <ErrorNote error={members.error} />
          </div>
        ) : members.items.length === 0 ? (
          <Empty title="Nobody here" hint="Invite someone to get started." />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Person</TableHead>
                <TableHead className="w-[180px]">Role</TableHead>
                <TableHead className="w-[140px]">Joined</TableHead>
                {isAdmin && <TableHead className="w-[90px]" />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {members.items.map((m) => (
                <MemberRow
                  key={m.userId}
                  member={m}
                  workspaceId={workspaceId}
                  isAdmin={isAdmin}
                  isMe={m.userId === me?.id}
                  isLastAdmin={m.role === "Admin" && admins === 1}
                  onRemove={() => setRemoving(m)}
                />
              ))}
            </TableBody>
          </Table>
        )}
      </div>

      <RemoveMemberDialog
        workspaceId={workspaceId}
        member={removing}
        onClose={() => setRemoving(null)}
      />
    </>
  );
}

function MemberRow({
  member,
  workspaceId,
  isAdmin,
  isMe,
  isLastAdmin,
  onRemove,
}: {
  member: WorkspaceMember;
  workspaceId: string;
  isAdmin: boolean;
  isMe: boolean;
  isLastAdmin: boolean;
  onRemove: () => void;
}) {
  const qc = useQueryClient();

  const changeRole = useMutation({
    mutationFn: (role: WorkspaceRoleName) =>
      api.patch(`/api/workspaces/${workspaceId}/members/${member.userId}`, {
        role,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspace-members", workspaceId] });
      qc.invalidateQueries({ queryKey: ["workspace-role", workspaceId] });
    },
  });

  return (
    <TableRow>
      <TableCell>
        <div className="flex items-center gap-2.5">
          <Avatar
            user={{
              displayName: member.displayName,
              avatarUrl: member.avatarUrl,
            }}
            size={26}
          />
          <span className="font-medium">{member.displayName}</span>
          {isMe && <Badge variant="secondary">You</Badge>}
        </div>
        <ErrorNote error={changeRole.error} className="mt-2" />
      </TableCell>

      <TableCell>
        {/*
          The server refuses to demote the only admin; saying so here saves the
          round trip, and the disabled control explains why it cannot change.
        */}
        {isAdmin && !isLastAdmin ? (
          <Select
            value={member.role}
            onValueChange={(v) => changeRole.mutate(v as WorkspaceRoleName)}
            disabled={changeRole.isPending}
          >
            <SelectTrigger className="w-[150px]" aria-label="Role">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Admin">Admin</SelectItem>
              <SelectItem value="Member">Member</SelectItem>
            </SelectContent>
          </Select>
        ) : (
          <span className={member.role === "Admin" ? "chip chip-ink" : "chip"}>
            {member.role}
            {isLastAdmin && " · the only one"}
          </span>
        )}
      </TableCell>

      <TableCell className="t-meta">{fullDate(member.joinedAtUtc)}</TableCell>

      {isAdmin && (
        <TableCell className="text-right">
          {!isLastAdmin && (
            <Button variant="ghost" size="sm" onClick={onRemove}>
              Remove
            </Button>
          )}
        </TableCell>
      )}
    </TableRow>
  );
}

function RemoveMemberDialog({
  workspaceId,
  member,
  onClose,
}: {
  workspaceId: string;
  member: WorkspaceMember | null;
  onClose: () => void;
}) {
  const qc = useQueryClient();

  const remove = useMutation({
    mutationFn: () =>
      api.del(`/api/workspaces/${workspaceId}/members/${member?.userId}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspace-members", workspaceId] });
      onClose();
    },
  });

  return (
    <AlertDialog
      open={Boolean(member)}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            Remove {member?.displayName} from this workspace?
          </AlertDialogTitle>
          <AlertDialogDescription>
            They lose access to every project in it. Issues they created and
            comments they wrote stay where they are.
          </AlertDialogDescription>
        </AlertDialogHeader>

        <ErrorNote error={remove.error} />

        <AlertDialogFooter>
          <AlertDialogCancel disabled={remove.isPending}>
            Keep them
          </AlertDialogCancel>
          <AlertDialogAction
            onClick={(e) => {
              e.preventDefault();
              remove.mutate();
            }}
            disabled={remove.isPending}
          >
            {remove.isPending ? "Removing…" : "Remove"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
