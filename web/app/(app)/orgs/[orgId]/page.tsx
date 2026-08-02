"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useWorkspaces } from "@/lib/hooks";
import { fullDate } from "@/lib/format";
import { useSession } from "@/lib/providers";
import type { Organization } from "@/lib/types";
import {
  Empty,
  ErrorNote,
  Field,
  Loading,
  Modal,
  PageHead,
  Section,
} from "@/components/ui";

export default function OrgPage() {
  const { orgId } = useParams<{ orgId: string }>();
  const qc = useQueryClient();
  const { me } = useSession();
  const [creating, setCreating] = useState(false);

  const { data: org, isLoading } = useQuery({
    queryKey: ["organization", orgId],
    queryFn: () => api.get<Organization>(`/api/organizations/${orgId}`),
  });

  const workspaces = useWorkspaces();
  const mine = workspaces.items.filter((w) => w.organizationId === orgId);
  const isOwner = org?.ownerUserId === me?.id;

  const [name, setName] = useState("");
  useEffect(() => {
    if (org) setName(org.name);
  }, [org]);

  const rename = useMutation({
    mutationFn: () => api.patch(`/api/organizations/${orgId}`, { name }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["organization", orgId] }),
  });

  if (isLoading || !org) return <Loading label="Loading organization" />;

  return (
    <>
      <PageHead
        eyebrow="Organization"
        title={org.name}
        meta={`Created ${fullDate(org.createdAtUtc)}${isOwner ? " · you own it" : ""}`}
        actions={
          isOwner && (
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setCreating(true)}
            >
              New workspace
            </button>
          )
        }
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.4fr)_minmax(260px,1fr)]">
        <Section title="Workspaces" count={mine.length}>
          <div className="card overflow-hidden">
            {mine.length === 0 ? (
              <Empty
                title="No workspaces here yet"
                hint="A workspace is where projects, teams and members live."
                action={
                  isOwner ? (
                    <button
                      type="button"
                      className="btn btn-primary btn-sm"
                      onClick={() => setCreating(true)}
                    >
                      Create a workspace
                    </button>
                  ) : undefined
                }
              />
            ) : (
              <ul className="sheet">
                {mine.map((w) => (
                  <li key={w.id}>
                    <Link
                      href={`/w/${w.id}`}
                      className="row-hover flex items-center gap-3 px-3 py-2.5"
                    >
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">
                          {w.name}
                        </span>
                        {w.description && (
                          <span className="block truncate text-[12px] text-[var(--color-ink-soft)]">
                            {w.description}
                          </span>
                        )}
                      </span>
                      {w.isArchived && <span className="chip">Archived</span>}
                      <span
                        className={w.role === "Admin" ? "chip chip-ink" : "chip"}
                      >
                        {w.role}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </Section>

        <Section title="Details">
          <form
            className="card space-y-3.5 p-4"
            onSubmit={(e) => {
              e.preventDefault();
              rename.mutate();
            }}
          >
            <Field label="Name">
              <input
                className="field"
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={!isOwner}
              />
            </Field>
            <ErrorNote error={rename.error} />
            {isOwner ? (
              <button
                type="submit"
                className="btn btn-primary"
                disabled={rename.isPending}
              >
                {rename.isPending ? "Saving…" : "Save changes"}
              </button>
            ) : (
              <p className="text-[13px] text-[var(--color-ink-soft)]">
                Only the owner can rename this organization.
              </p>
            )}
          </form>
        </Section>
      </div>

      <CreateWorkspaceDialog
        open={creating}
        onClose={() => setCreating(false)}
        orgId={orgId}
      />
    </>
  );
}

function CreateWorkspaceDialog({
  open,
  onClose,
  orgId,
}: {
  open: boolean;
  onClose: () => void;
  orgId: string;
}) {
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  const create = useMutation({
    mutationFn: () =>
      api.post(`/api/organizations/${orgId}/workspaces`, {
        name,
        description: description || undefined,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workspaces"] });
      setName("");
      setDescription("");
      onClose();
    },
  });

  return (
    <Modal open={open} onClose={onClose} title="New workspace">
      <form
        className="space-y-3.5"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <Field label="Name">
          <input
            className="field"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Product"
            required
          />
        </Field>
        <Field label="Description">
          <textarea
            className="field"
            rows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </Field>
        <ErrorNote error={create.error} />
        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={create.isPending || !name.trim()}
          >
            {create.isPending ? "Creating…" : "Create workspace"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
