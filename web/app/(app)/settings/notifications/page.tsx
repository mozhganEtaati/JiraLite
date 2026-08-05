"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { NotificationPrefs } from "@/lib/types";
import { ErrorNote, Loading, PageHead } from "@/components/kit";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";

export default function NotificationSettingsPage() {
  const qc = useQueryClient();

  const prefs = useQuery({
    queryKey: ["notification-prefs"],
    queryFn: () =>
      api.get<NotificationPrefs>("/api/users/me/notification-preferences"),
  });

  /**
   * The endpoint takes both flags at once, so a switch sends the pair with its
   * own value replaced — never a half-written preference.
   */
  const save = useMutation({
    mutationFn: (next: NotificationPrefs) =>
      api.patch<NotificationPrefs>(
        "/api/users/me/notification-preferences",
        next,
      ),
    onSuccess: (result) => {
      qc.setQueryData(["notification-prefs"], result);
      qc.invalidateQueries({ queryKey: ["unread-count"] });
    },
  });

  const current = prefs.data;

  return (
    <>
      <PageHead
        eyebrow="Account"
        title="Notifications"
        meta="Where JiraLite may reach you when something needs your attention."
      />

      {prefs.isLoading ? (
        <div className="card">
          <Loading label="Loading preferences" />
        </div>
      ) : prefs.error ? (
        <ErrorNote error={prefs.error} />
      ) : (
        current && (
          <div className="card divide-y divide-[var(--color-rule-soft)]">
            <Row
              id="in-app"
              title="In the app"
              hint="The bell in the sidebar and the notifications page."
              checked={current.inAppEnabled}
              disabled={save.isPending}
              onChange={(inAppEnabled) =>
                save.mutate({ ...current, inAppEnabled })
              }
            />
            <Row
              id="email"
              title="By e-mail"
              hint="A message to your sign-in address for the same events."
              checked={current.emailEnabled}
              disabled={save.isPending}
              onChange={(emailEnabled) =>
                save.mutate({ ...current, emailEnabled })
              }
            />
          </div>
        )
      )}

      <ErrorNote error={save.error} className="mt-3" />
    </>
  );
}

function Row({
  id,
  title,
  hint,
  checked,
  disabled,
  onChange,
}: {
  id: string;
  title: string;
  hint: string;
  checked: boolean;
  disabled: boolean;
  onChange: (next: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between gap-4 p-4">
      <div className="min-w-0">
        <Label htmlFor={id} className="text-[14px]">
          {title}
        </Label>
        <p className="mt-1 text-[13px] text-[var(--color-ink-soft)]">{hint}</p>
      </div>
      <Switch
        id={id}
        checked={checked}
        disabled={disabled}
        onCheckedChange={onChange}
      />
    </div>
  );
}
