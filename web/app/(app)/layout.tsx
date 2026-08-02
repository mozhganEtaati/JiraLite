"use client";

import { AuthGate, Rail, WorkspaceScope } from "@/components/shell";

export default function AppLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <AuthGate>
      <WorkspaceScope>
        <div className="flex min-h-screen">
          <Rail />
          <main className="min-w-0 flex-1 px-6 py-6">
            <div className="mx-auto max-w-[1320px]">{children}</div>
          </main>
        </div>
      </WorkspaceScope>
    </AuthGate>
  );
}
