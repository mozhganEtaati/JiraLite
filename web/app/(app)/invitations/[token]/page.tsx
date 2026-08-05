"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useSession } from "@/lib/providers";
import { ErrorNote, PageHead } from "@/components/kit";
import { Button } from "@/components/ui/button";

type Accepted = { workspaceId: string; role: string };

/**
 * The link from an invitation e-mail lands here. It sits inside the signed-in
 * group on purpose: the API ties an invitation to the account answering it, so
 * the gate sends people to log in first and back here afterwards.
 *
 * There is no endpoint to read an invitation by token, so this screen does not
 * pretend to know who sent it — it offers the two answers and reports what
 * happened.
 */
export default function InvitationPage() {
  const { token } = useParams<{ token: string }>();
  const router = useRouter();
  const qc = useQueryClient();
  const { me } = useSession();

  const accept = useMutation({
    mutationFn: () => api.post<Accepted>(`/api/invitations/${token}/accept`),
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: ["workspaces"] });
      router.replace(`/w/${result.workspaceId}/projects`);
    },
  });

  const decline = useMutation({
    mutationFn: () => api.post(`/api/invitations/${token}/decline`),
  });

  const busy = accept.isPending || decline.isPending;

  return (
    <div className="mx-auto max-w-[520px] py-10">
      <PageHead
        eyebrow="Invitation"
        title="You have been invited to a workspace"
        meta={
          me
            ? `Answering as ${me.email}. If the invitation was sent to another address, sign in with that one first.`
            : undefined
        }
      />

      {decline.isSuccess ? (
        <div className="card p-5">
          <p className="font-medium">Invitation declined.</p>
          <p className="mt-1 text-[13px] text-[var(--color-ink-soft)]">
            Nothing was shared with you. Whoever invited you can send another.
          </p>
          <Button className="mt-4" variant="outline" onClick={() => router.push("/dashboard")}>
            Back to my work
          </Button>
        </div>
      ) : (
        <div className="card p-5">
          <p className="text-[13px] text-[var(--color-ink-soft)]">
            Accepting adds you to the workspace and everything shared with its
            members. Declining leaves things as they are.
          </p>

          <ErrorNote
            error={accept.error ?? decline.error}
            className="mt-4"
          />

          <div className="mt-5 flex gap-2">
            <Button onClick={() => accept.mutate()} disabled={busy}>
              {accept.isPending ? "Joining…" : "Accept invitation"}
            </Button>
            <Button
              variant="outline"
              onClick={() => decline.mutate()}
              disabled={busy}
            >
              {decline.isPending ? "Declining…" : "Decline"}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
