"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import { api } from "@/lib/api";
import { cx } from "@/lib/format";
import { useWorkspaceRole, useWorkspaces } from "@/lib/hooks";
import { useSession } from "@/lib/providers";
import { Avatar } from "@/components/marks";
import { RegMark, Wordmark } from "@/components/wordmark";

/* ── which workspace am I in ──────────────────────────────── */

const WorkspaceCtx = createContext<{
  workspaceId: string | null;
  setWorkspaceId: (id: string) => void;
}>({ workspaceId: null, setWorkspaceId: () => {} });

export function useCurrentWorkspace() {
  return useContext(WorkspaceCtx);
}

const LAST_WS = "jl.workspace";

export function WorkspaceScope({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const fromPath = pathname.match(/^\/w\/([0-9a-f-]{36})/i)?.[1] ?? null;
  const [stored, setStored] = useState<string | null>(null);

  useEffect(() => {
    setStored(localStorage.getItem(LAST_WS));
  }, []);

  const setWorkspaceId = useCallback((id: string) => {
    localStorage.setItem(LAST_WS, id);
    setStored(id);
  }, []);

  useEffect(() => {
    if (fromPath) setWorkspaceId(fromPath);
  }, [fromPath, setWorkspaceId]);

  const value = useMemo(
    () => ({ workspaceId: fromPath ?? stored, setWorkspaceId }),
    [fromPath, stored, setWorkspaceId],
  );

  return <WorkspaceCtx.Provider value={value}>{children}</WorkspaceCtx.Provider>;
}

/* ── rail ─────────────────────────────────────────────────── */

export function Rail() {
  const pathname = usePathname();
  const { me, signOut } = useSession();
  const { workspaceId, setWorkspaceId } = useCurrentWorkspace();
  const { items: workspaces } = useWorkspaces();
  const { isAdmin } = useWorkspaceRole(workspaceId ?? undefined);
  const [switcherOpen, setSwitcherOpen] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);

  const current = workspaces.find((w) => w.id === workspaceId) ?? workspaces[0];

  useEffect(() => {
    if (!workspaceId && workspaces[0]) setWorkspaceId(workspaces[0].id);
  }, [workspaceId, workspaces, setWorkspaceId]);

  const ws = current?.id;

  return (
    <nav
      className="flex w-[228px] shrink-0 flex-col border-r border-[var(--color-rule)] bg-[var(--color-paper-deep)]"
      aria-label="Main"
    >
      <div className="flex items-center gap-2.5 px-4 py-4">
        <RegMark size={18} />
        <Wordmark size={18} />
      </div>

      {/* workspace switcher */}
      <div className="relative px-3 pb-3">
        <button
          type="button"
          className="plate flex w-full items-center justify-between gap-2 px-2.5 py-2 text-left"
          onClick={() => setSwitcherOpen((v) => !v)}
          aria-expanded={switcherOpen}
        >
          <span className="min-w-0">
            <span className="t-eyebrow block text-[10px]">Workspace</span>
            <span className="block truncate text-[13px] font-medium">
              {current?.name ?? "None yet"}
            </span>
          </span>
          <svg width="9" height="6" viewBox="0 0 10 7" aria-hidden>
            <path
              d="M1 1.5 5 5.5 9 1.5"
              fill="none"
              stroke="var(--color-ink-soft)"
              strokeWidth="1.5"
            />
          </svg>
        </button>

        {switcherOpen && (
          <div
            className="absolute inset-x-3 z-30 mt-1 max-h-[320px] overflow-auto border border-[var(--color-ink)] bg-[var(--color-surface)]"
            style={{ borderRadius: 3, boxShadow: "4px 4px 0 var(--color-pink)" }}
          >
            <ul className="sheet">
              {workspaces.map((w) => (
                <li key={w.id}>
                  <Link
                    href={`/w/${w.id}`}
                    className="row-hover flex items-center justify-between gap-2 px-2.5 py-2"
                    onClick={() => {
                      setWorkspaceId(w.id);
                      setSwitcherOpen(false);
                    }}
                  >
                    <span className="truncate text-[13px]">{w.name}</span>
                    {w.role === "Admin" && (
                      <span className="chip chip-ink">Admin</span>
                    )}
                  </Link>
                </li>
              ))}
            </ul>
            <Link
              href="/orgs"
              className="row-hover block border-t border-[var(--color-rule)] px-2.5 py-2 text-[13px] font-medium text-[var(--color-blue)]"
              onClick={() => setSwitcherOpen(false)}
            >
              New workspace…
            </Link>
          </div>
        )}
      </div>

      <div className="flex-1 overflow-y-auto px-3 pb-3">
        <Group>
          <Item href="/dashboard" active={pathname === "/dashboard"}>
            My work
          </Item>
          <Item
            href="/notifications"
            active={pathname === "/notifications"}
            trailing={<UnreadBadge />}
          >
            Notifications
          </Item>
        </Group>

        {ws && (
          <Group label="This workspace">
            <Item
              href={`/w/${ws}/projects`}
              active={pathname.startsWith(`/w/${ws}/projects`) || pathname.startsWith("/p/")}
            >
              Projects
            </Item>
            <Item
              href={`/w/${ws}/teams`}
              active={pathname.startsWith(`/w/${ws}/teams`)}
            >
              Teams
            </Item>
            <Item
              href={`/w/${ws}/members`}
              active={pathname.startsWith(`/w/${ws}/members`)}
            >
              Members
            </Item>
            {isAdmin && (
              <Item
                href={`/w/${ws}/invitations`}
                active={pathname.startsWith(`/w/${ws}/invitations`)}
              >
                Invitations
              </Item>
            )}
            <Item
              href={`/w/${ws}/settings`}
              active={pathname.startsWith(`/w/${ws}/settings`)}
            >
              Settings
            </Item>
          </Group>
        )}

        {ws && isAdmin && (
          <Group label="Admin">
            <Item
              href={`/w/${ws}/admin`}
              active={pathname === `/w/${ws}/admin`}
            >
              Overview
            </Item>
            <Item
              href={`/w/${ws}/admin/users`}
              active={pathname === `/w/${ws}/admin/users`}
            >
              People
            </Item>
            <Item
              href={`/w/${ws}/admin/projects`}
              active={pathname === `/w/${ws}/admin/projects`}
            >
              Projects
            </Item>
            <Item
              href={`/w/${ws}/admin/roles`}
              active={pathname === `/w/${ws}/admin/roles`}
            >
              What roles can do
            </Item>
          </Group>
        )}

        <Group label="Organizations">
          <Item href="/orgs" active={pathname.startsWith("/orgs")}>
            All organizations
          </Item>
        </Group>
      </div>

      {/* user */}
      <div className="relative border-t border-[var(--color-rule)] p-3">
        <button
          type="button"
          className="row-hover flex w-full items-center gap-2 rounded-[3px] px-1.5 py-1.5 text-left"
          onClick={() => setMenuOpen((v) => !v)}
          aria-expanded={menuOpen}
        >
          <Avatar user={me ?? null} size={24} />
          <span className="min-w-0 flex-1">
            <span className="block truncate text-[13px] font-medium">
              {me?.displayName ?? "…"}
            </span>
            <span className="t-meta block truncate text-[11px]">
              {me?.email}
            </span>
          </span>
        </button>

        {menuOpen && (
          <div
            className="absolute inset-x-3 bottom-[calc(100%-6px)] z-30 border border-[var(--color-ink)] bg-[var(--color-surface)]"
            style={{ borderRadius: 3, boxShadow: "4px 4px 0 var(--color-pink)" }}
          >
            <ul className="sheet">
              {[
                ["/settings/profile", "Profile"],
                ["/settings/notifications", "Notification settings"],
                ["/settings/activity", "My activity"],
              ].map(([href, label]) => (
                <li key={href}>
                  <Link
                    href={href}
                    className="row-hover block px-2.5 py-2 text-[13px]"
                    onClick={() => setMenuOpen(false)}
                  >
                    {label}
                  </Link>
                </li>
              ))}
              <li>
                <button
                  type="button"
                  className="row-hover block w-full px-2.5 py-2 text-left text-[13px] text-[var(--color-alarm)]"
                  onClick={() => {
                    setMenuOpen(false);
                    void signOut();
                  }}
                >
                  Sign out
                </button>
              </li>
            </ul>
          </div>
        )}
      </div>
    </nav>
  );
}

function Group({
  label,
  children,
}: {
  label?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="mb-4">
      {label && (
        <div className="t-eyebrow mb-1.5 px-2 text-[10px]">{label}</div>
      )}
      <ul className="space-y-0.5">{children}</ul>
    </div>
  );
}

function Item({
  href,
  active,
  children,
  trailing,
}: {
  href: string;
  active?: boolean;
  children: React.ReactNode;
  trailing?: React.ReactNode;
}) {
  return (
    <li>
      <Link
        href={href}
        aria-current={active ? "page" : undefined}
        className={cx(
          "flex items-center justify-between gap-2 rounded-[3px] border-l-[3px] px-2 py-1.5 text-[13px] transition-colors",
          active
            ? "border-[var(--color-pink)] bg-[var(--color-surface)] font-medium text-[var(--color-blue)]"
            : "border-transparent text-[var(--color-ink-soft)] hover:bg-[color-mix(in_srgb,var(--color-surface)_60%,transparent)] hover:text-[var(--color-ink)]",
        )}
      >
        <span className="truncate">{children}</span>
        {trailing}
      </Link>
    </li>
  );
}

function UnreadBadge() {
  const { data } = useQuery({
    queryKey: ["unread-count"],
    queryFn: () => api.get<{ unreadCount: number }>("/api/notifications/unread-count"),
    refetchInterval: 60_000,
  });
  if (!data?.unreadCount) return null;
  return (
    <span
      className="t-num inline-flex h-[17px] min-w-[17px] items-center justify-center px-1 text-[10px] font-medium"
      style={{
        background: "var(--color-pink)",
        color: "#fff",
        borderRadius: 2,
      }}
    >
      {data.unreadCount > 99 ? "99+" : data.unreadCount}
    </span>
  );
}

/* ── project tab bar ──────────────────────────────────────── */

export function ProjectTabs({ projectId }: { projectId: string }) {
  const pathname = usePathname();
  const tabs = [
    ["", "Overview"],
    ["/board", "Board"],
    ["/backlog", "Backlog"],
    ["/sprints", "Sprints"],
    ["/issues", "Issues"],
    ["/calendar", "Calendar"],
    ["/settings", "Settings"],
  ] as const;

  return (
    <div className="-mx-6 mb-5 border-b border-[var(--color-rule)] px-6">
      <ul className="flex gap-1 overflow-x-auto">
        {tabs.map(([suffix, label]) => {
          const href = `/p/${projectId}${suffix}`;
          const active =
            suffix === ""
              ? pathname === href
              : pathname.startsWith(href);
          return (
            <li key={label}>
              <Link
                href={href}
                aria-current={active ? "page" : undefined}
                className={cx(
                  "-mb-px block border-b-2 px-2.5 py-2 text-[13px] whitespace-nowrap transition-colors",
                  active
                    ? "border-[var(--color-pink)] font-medium text-[var(--color-ink)]"
                    : "border-transparent text-[var(--color-ink-soft)] hover:text-[var(--color-ink)]",
                )}
              >
                {label}
              </Link>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/* ── gate ─────────────────────────────────────────────────── */

export function AuthGate({ children }: { children: React.ReactNode }) {
  const { status } = useSession();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (status === "signed-out") {
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    }
  }, [status, router, pathname]);

  if (status !== "signed-in") {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="text-center">
          <div className="press-bar mx-auto w-[120px] rounded-full" />
          <p className="t-meta mt-3">Loading your workspace…</p>
        </div>
      </div>
    );
  }
  return <>{children}</>;
}
