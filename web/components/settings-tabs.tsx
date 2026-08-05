"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cx } from "@/lib/format";

/** Small pill bar for the sub-pages inside a settings area. */
export function SettingsTabs({
  tabs,
}: {
  tabs: [href: string, label: string][];
}) {
  const pathname = usePathname();
  return (
    <nav className="mb-5 flex flex-wrap gap-2">
      {tabs.map(([href, label]) => (
        <Link
          key={href}
          href={href}
          aria-current={pathname === href ? "page" : undefined}
          className={cx("chip h-8 px-3", pathname === href && "chip-ink")}
        >
          {label}
        </Link>
      ))}
    </nav>
  );
}

export function projectSettingsTabs(
  projectId: string,
): [string, string][] {
  return [
    [`/p/${projectId}/settings`, "Details"],
    [`/p/${projectId}/settings/members`, "Members"],
    [`/p/${projectId}/settings/labels`, "Labels"],
    [`/p/${projectId}/boards`, "Boards"],
  ];
}

/** The account pages are only reachable from the user menu, so they carry their own bar. */
export function accountSettingsTabs(): [string, string][] {
  return [
    ["/settings/profile", "Profile"],
    ["/settings/notifications", "Notifications"],
    ["/settings/activity", "My activity"],
  ];
}

export function workspaceSettingsTabs(
  workspaceId: string,
  isAdmin: boolean,
): [string, string][] {
  const tabs: [string, string][] = [
    [`/w/${workspaceId}/settings`, "Details"],
    [`/w/${workspaceId}/members`, "Members"],
    [`/w/${workspaceId}/teams`, "Teams"],
  ];
  if (isAdmin) tabs.push([`/w/${workspaceId}/invitations`, "Invitations"]);
  return tabs;
}
