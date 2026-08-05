"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { ago, fullDate } from "@/lib/format";
import { useList, useWorkspace } from "@/lib/hooks";
import type { Invitation, InvitationStatusName, WorkspaceRoleName } from "@/lib/types";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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

/** Only a pending invitation can still be taken back. */
const REVOCABLE: InvitationStatusName[] = ["Pending"];

export default function InvitationsPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);
  const [inviting, setInviting] = useState(false);

  const invitations = useList<Invitation>(
    ["invitations", workspaceId],
    `/api/workspaces/${workspaceId}/invitations`,
  );

  const pending = invitations.items.filter((i) => i.status === "Pending").length;

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="Invitations"
        meta={
          pending
            ? `${pending} still waiting to be answered`
            : "Nobody is waiting on an answer."
        }
        actions={<Button onClick={() => setInviting(true)}>Invite someone</Button>}
      />

      <div className="card overflow-hidden">
        {invitations.isLoading ? (
          <Loading label="Loading invitations" />
        ) : invitations.error ? (
          <div className="p-4">
            <ErrorNote error={invitations.error} />
          </div>
        ) : invitations.items.length === 0 ? (
          <Empty
            title="No invitations sent"
            hint="Invite someone by e-mail and they will find the workspace waiting for them."
            action={
              <Button size="sm" onClick={() => setInviting(true)}>
                Invite someone
              </Button>
            }
          />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>E-mail</TableHead>
                <TableHead className="w-[110px]">Role</TableHead>
                <TableHead className="w-[130px]">Status</TableHead>
                <TableHead className="w-[170px]">Expires</TableHead>
                <TableHead className="w-[100px]" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {invitations.items.map((invite) => (
                <InvitationRow
                  key={invite.id}
                  invite={invite}
                  workspaceId={workspaceId}
                />
              ))}
            </TableBody>
          </Table>
        )}
      </div>

      <InviteDialog
        workspaceId={workspaceId}
        open={inviting}
        onClose={() => setInviting(false)}
      />
    </>
  );
}

function InvitationRow({
  invite,
  workspaceId,
}: {
  invite: Invitation;
  workspaceId: string;
}) {
  const qc = useQueryClient();

  const revoke = useMutation({
    mutationFn: () =>
      api.del(`/api/workspaces/${workspaceId}/invitations/${invite.id}`),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ["invitations", workspaceId] }),
  });

  const expired = new Date(invite.expiresAtUtc) < new Date();

  return (
    <TableRow>
      <TableCell className="font-medium">
        {invite.email}
        <ErrorNote error={revoke.error} className="mt-2" />
      </TableCell>
      <TableCell>
        <span className={invite.role === "Admin" ? "chip chip-ink" : "chip"}>
          {invite.role}
        </span>
      </TableCell>
      <TableCell>
        <Badge
          variant={invite.status === "Accepted" ? "default" : "secondary"}
        >
          {invite.status}
        </Badge>
      </TableCell>
      <TableCell className="t-meta">
        {invite.status === "Pending" && expired
          ? "expired"
          : `${fullDate(invite.expiresAtUtc)} · sent ${ago(invite.createdAtUtc)}`}
      </TableCell>
      <TableCell className="text-right">
        {REVOCABLE.includes(invite.status) && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => revoke.mutate()}
            disabled={revoke.isPending}
          >
            {revoke.isPending ? "Revoking…" : "Revoke"}
          </Button>
        )}
      </TableCell>
    </TableRow>
  );
}

function InviteDialog({
  workspaceId,
  open,
  onClose,
}: {
  workspaceId: string;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<WorkspaceRoleName>("Member");

  const invite = useMutation({
    mutationFn: () =>
      api.post(`/api/workspaces/${workspaceId}/invitations`, { email, role }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["invitations", workspaceId] });
      setEmail("");
      setRole("Member");
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
      <DialogContent className="sm:max-w-[430px]">
        <DialogHeader>
          <DialogTitle>Invite someone</DialogTitle>
          <DialogDescription>
            They get an e-mail with a link. The invitation expires if it is not
            answered.
          </DialogDescription>
        </DialogHeader>

        <form
          id="invite"
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            invite.mutate();
          }}
        >
          <div className="space-y-2">
            <Label htmlFor="inv-email">E-mail</Label>
            <Input
              id="inv-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              maxLength={256}
              placeholder="person@company.com"
              autoFocus
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="inv-role">Role</Label>
            <Select
              value={role}
              onValueChange={(v) => setRole(v as WorkspaceRoleName)}
            >
              <SelectTrigger id="inv-role" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Member">
                  Member — works in the projects they are added to
                </SelectItem>
                <SelectItem value="Admin">
                  Admin — runs the workspace and every project in it
                </SelectItem>
              </SelectContent>
            </Select>
          </div>

          <ErrorNote error={invite.error} />
        </form>

        <DialogFooter>
          <Button variant="outline" type="button" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="invite"
            disabled={invite.isPending || !email.trim()}
          >
            {invite.isPending ? "Sending…" : "Send invitation"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
