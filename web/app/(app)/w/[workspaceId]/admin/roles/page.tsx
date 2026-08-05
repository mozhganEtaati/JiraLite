"use client";

import { useParams } from "next/navigation";
import { useList, useWorkspace } from "@/lib/hooks";
import type { AdminRole } from "@/lib/types";
import { ErrorNote, Loading, PageHead, Section } from "@/components/kit";

/**
 * The catalogue is served by the API rather than written here, so what this
 * screen promises and what the authorization handlers actually enforce cannot
 * drift apart.
 */
export default function AdminRolesPage() {
  const { workspaceId } = useParams<{ workspaceId: string }>();
  const workspace = useWorkspace(workspaceId);

  const roles = useList<AdminRole>(
    ["admin-roles", workspaceId],
    `/api/workspaces/${workspaceId}/admin/roles`,
  );

  const scopes = Array.from(new Set(roles.items.map((r) => r.scope)));

  return (
    <>
      <PageHead
        eyebrow={workspace.data?.name ?? "Workspace"}
        title="What roles can do"
        meta="Roles are checked on every request — nothing here is cached in your session."
      />

      {roles.isLoading ? (
        <div className="card">
          <Loading label="Loading roles" />
        </div>
      ) : roles.error ? (
        <ErrorNote error={roles.error} />
      ) : (
        <div className="space-y-6">
          {scopes.map((scope) => (
            <Section key={scope} title={`${scope} roles`}>
              <div className="card overflow-hidden">
                <ul className="sheet">
                  {roles.items
                    .filter((r) => r.scope === scope)
                    .map((r) => (
                      <li
                        key={`${r.scope}-${r.role}`}
                        className="flex flex-wrap items-baseline gap-x-4 gap-y-1 px-4 py-3"
                      >
                        <span className="w-[130px] shrink-0 font-medium">
                          {r.role}
                        </span>
                        <span className="min-w-0 flex-1 text-[13px] text-[var(--color-ink-soft)]">
                          {r.description}
                        </span>
                      </li>
                    ))}
                </ul>
              </div>
            </Section>
          ))}
        </div>
      )}
    </>
  );
}
