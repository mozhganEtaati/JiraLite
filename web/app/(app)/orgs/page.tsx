"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { useList, useWorkspaces } from "@/lib/hooks";
import { fullDate } from "@/lib/format";
import type { OrganizationItem } from "@/lib/types";
import {
  Empty,
  ErrorNote,
  Field,
  Loading,
  Modal,
  PageHead,
  Section,
} from "@/components/kit";

export default function OrgsPage() {
  const [creating, setCreating] = useState(false);
  const orgs = useList<OrganizationItem>(["organizations"], "/api/organizations");
  const workspaces = useWorkspaces();

  return (
    <>
      <PageHead
        eyebrow="Organizations"
        title="Organizations and workspaces"
        meta="An organization owns workspaces. A workspace owns projects."
        actions={
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => setCreating(true)}
          >
            New organization
          </button>
        }
      />

      <div className="grid gap-6 lg:grid-cols-2">
        <Section title="Organizations" count={orgs.items.length}>
          <div className="card overflow-hidden">
            {orgs.isLoading ? (
              <Loading />
            ) : orgs.items.length === 0 ? (
              <Empty
                title="No organizations yet"
                hint="Create one to hold your first workspace."
                action={
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    onClick={() => setCreating(true)}
                  >
                    Create an organization
                  </button>
                }
              />
            ) : (
              <ul className="sheet">
                {orgs.items.map((o) => (
                  <li key={o.id}>
                    <Link
                      href={`/orgs/${o.id}`}
                      className="row-hover flex items-center justify-between gap-3 px-3 py-2.5"
                    >
                      <span className="min-w-0 truncate font-medium">
                        {o.name}
                      </span>
                      <span className="t-meta">
                        {fullDate(o.createdAtUtc)}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </Section>

        <Section title="Workspaces you belong to" count={workspaces.items.length}>
          <div className="card overflow-hidden">
            {workspaces.items.length === 0 ? (
              <Empty
                title="No workspaces yet"
                hint="Open an organization and create one."
              />
            ) : (
              <ul className="sheet">
                {workspaces.items.map((w) => (
                  <li key={w.id}>
                    <Link
                      href={`/w/${w.id}`}
                      className="row-hover flex items-center gap-3 px-3 py-2.5"
                    >
                      <span className="min-w-0 flex-1 truncate font-medium">
                        {w.name}
                      </span>
                      {w.isArchived && <span className="chip">Archived</span>}
                      <span
                        className={
                          w.role === "Admin" ? "chip chip-ink" : "chip"
                        }
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
      </div>

      <CreateOrgDialog open={creating} onClose={() => setCreating(false)} />
    </>
  );
}

function CreateOrgDialog({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [name, setName] = useState("");

  const create = useMutation({
    mutationFn: () => api.post("/api/organizations", { name }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["organizations"] });
      setName("");
      onClose();
    },
  });

  return (
    <Modal open={open} onClose={onClose} title="New organization" width={420}>
      <form
        className="space-y-3.5"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <Field label="Name" hint="Usually your company or team name.">
          <input
            className="field"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
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
            {create.isPending ? "Creating…" : "Create organization"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
