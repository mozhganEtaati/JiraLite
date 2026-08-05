"use client";

import { accountSettingsTabs, SettingsTabs } from "@/components/settings-tabs";

export default function AccountSettingsLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="mx-auto max-w-[760px]">
      <SettingsTabs tabs={accountSettingsTabs()} />
      {children}
    </div>
  );
}
