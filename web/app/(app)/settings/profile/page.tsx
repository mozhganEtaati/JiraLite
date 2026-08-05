"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { api, API_BASE } from "@/lib/api";
import { fullDate } from "@/lib/format";
import { useSession } from "@/lib/providers";
import type { Me } from "@/lib/types";
import { Avatar } from "@/components/marks";
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

const AVATAR_TYPES = ["image/png", "image/jpeg", "image/webp"];
const AVATAR_MAX_BYTES = 5 * 1024 * 1024;

export default function ProfilePage() {
  const { me } = useSession();

  return (
    <>
      <PageHead
        eyebrow="Account"
        title="Profile"
        meta={me ? `Signed in as ${me.email}` : undefined}
      />

      {!me ? (
        <div className="card">
          <Loading label="Loading your profile" />
        </div>
      ) : (
        <div className="space-y-6">
          <Section title="Who you are">
            {/* keyed on the saved name so an edit elsewhere resets the field */}
            <NameCard key={me.displayName} me={me} />
          </Section>

          <Section title="Picture">
            <AvatarCard me={me} />
          </Section>

          <Section title="Closing the account">
            <DeactivateCard />
          </Section>
        </div>
      )}
    </>
  );
}

function NameCard({ me }: { me: Me }) {
  const qc = useQueryClient();
  const [displayName, setDisplayName] = useState(me.displayName);

  const save = useMutation({
    mutationFn: () => api.patch("/api/users/me", { displayName }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["me"] }),
  });

  const dirty = displayName !== me.displayName;

  return (
    <form
      className="card space-y-4 p-4"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      <div className="space-y-2">
        <Label htmlFor="display-name">Display name</Label>
        <Input
          id="display-name"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          maxLength={100}
          required
        />
        <p className="text-[12px] text-[var(--color-ink-faint)]">
          This is the name on your comments and on every issue assigned to you.
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="email">E-mail</Label>
        <Input id="email" value={me.email} disabled readOnly />
        <p className="text-[12px] text-[var(--color-ink-faint)]">
          Your e-mail is how you sign in and cannot be changed here. Member
          since {fullDate(me.createdAtUtc)}.
        </p>
      </div>

      <ErrorNote error={save.error} />

      <div className="flex items-center gap-3">
        <Button
          type="submit"
          disabled={!dirty || save.isPending || !displayName.trim()}
        >
          {save.isPending ? "Saving…" : "Save changes"}
        </Button>
        {save.isSuccess && !dirty && (
          <span className="t-meta" role="status">
            Saved.
          </span>
        )}
      </div>
    </form>
  );
}

function AvatarCard({ me }: { me: Me }) {
  const qc = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  const upload = useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append("file", file);
      return api.put("/api/users/me/avatar", body);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["me"] }),
  });

  const remove = useMutation({
    mutationFn: () => api.del("/api/users/me/avatar"),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["me"] }),
  });

  /**
   * The API rejects the wrong type or size with a 415/413; checking here first
   * means the answer arrives before the upload instead of after it.
   */
  function onPick(file: File | undefined) {
    if (!file) return;
    setLocalError(null);
    if (!AVATAR_TYPES.includes(file.type)) {
      setLocalError("Pick a PNG, JPEG or WebP image.");
      return;
    }
    if (file.size > AVATAR_MAX_BYTES) {
      setLocalError("That image is over 5 MB. Pick a smaller one.");
      return;
    }
    upload.mutate(file);
  }

  return (
    <div className="card p-4">
      <div className="flex flex-wrap items-center gap-4">
        <Avatar user={me} size={64} />

        <div className="min-w-0 flex-1">
          <p className="text-[13px] text-[var(--color-ink-soft)]">
            PNG, JPEG or WebP, up to 5 MB.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <Button
              variant="outline"
              onClick={() => inputRef.current?.click()}
              disabled={upload.isPending}
            >
              {upload.isPending ? "Uploading…" : "Upload a picture"}
            </Button>
            {me.avatarUrl && (
              <Button
                variant="ghost"
                onClick={() => remove.mutate()}
                disabled={remove.isPending}
              >
                {remove.isPending ? "Removing…" : "Remove"}
              </Button>
            )}
          </div>
        </div>
      </div>

      <input
        ref={inputRef}
        type="file"
        accept={AVATAR_TYPES.join(",")}
        className="hidden"
        onChange={(e) => {
          onPick(e.target.files?.[0]);
          e.target.value = "";
        }}
      />

      {localError && (
        <p role="alert" className="mt-3 text-[13px] text-[var(--color-alarm)]">
          {localError}
        </p>
      )}
      <ErrorNote error={upload.error ?? remove.error} className="mt-3" />

      {me.avatarUrl && (
        <p className="t-meta mt-3 truncate">
          {API_BASE}
          {me.avatarUrl}
        </p>
      )}
    </div>
  );
}

function DeactivateCard() {
  const { signOut } = useSession();
  const [open, setOpen] = useState(false);

  const deactivate = useMutation({
    mutationFn: () => api.post("/api/users/me/deactivate"),
    onSuccess: () => signOut(),
  });

  return (
    <div className="card p-4">
      <h3 className="font-medium">Deactivate your account</h3>
      <p className="mt-1 text-[13px] text-[var(--color-ink-soft)]">
        You are signed out and can no longer sign in. Your issues, comments and
        history stay where they are so your team&rsquo;s record is not full of
        holes.
      </p>

      <ErrorNote error={deactivate.error} className="mt-3" />

      <Button variant="outline" className="mt-3" onClick={() => setOpen(true)}>
        Deactivate account
      </Button>

      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Deactivate your account?</AlertDialogTitle>
            <AlertDialogDescription>
              This signs you out immediately. An administrator has to bring the
              account back — you cannot do it yourself.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deactivate.isPending}>
              Keep my account
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault();
                deactivate.mutate();
              }}
              disabled={deactivate.isPending}
            >
              {deactivate.isPending ? "Deactivating…" : "Deactivate"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
