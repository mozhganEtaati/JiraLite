"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { ApiError, api } from "@/lib/api";
import { Field, PasswordInput } from "@/components/kit";

function ResetForm() {
  const router = useRouter();
  const params = useSearchParams();
  const token = params.get("token") ?? "";

  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [mismatch, setMismatch] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setMismatch(null);

    // Checked here rather than server-side: the confirmation field exists to
    // catch a typo before it becomes a password nobody knows, and the API has
    // no business receiving a second copy of it.
    if (password !== confirm) {
      setMismatch("Both entries must match.");
      return;
    }

    setPending(true);
    try {
      await api.anon("/api/auth/reset-password", {
        token,
        newPassword: password,
      });
      router.replace("/login?reset=1");
    } catch (err) {
      setError(err);
      setPending(false);
    }
  }

  const fieldError = error instanceof ApiError ? error : null;
  const message =
    error instanceof ApiError
      ? (error.detail ?? error.title)
      : error instanceof Error
        ? error.message
        : error
          ? "That request could not be completed."
          : null;

  /*
   * There is no endpoint that reads a reset token, so a bad one is only found
   * out on submit. An empty one, though, means the person arrived without a
   * link at all — say so now instead of after they have chosen a password.
   */
  if (!token) {
    return (
      <>
        <h1 className="auth-title">Link incomplete</h1>
        <p className="auth-note" role="alert">
          This page needs the reset link from your e-mail. Open the link again,
          or request a new one.
        </p>
        <p className="auth-foot">
          <Link href="/forgot-password" className="auth-link auth-foot-link">
            Request a new link
          </Link>
        </p>
      </>
    );
  }

  return (
    <>
      <h1 className="auth-title">Choose a new password</h1>

      <form onSubmit={onSubmit} className="auth-form" noValidate>
        <Field
          label="New password"
          hint="At least 8 characters, with a letter and a digit."
          error={fieldError?.field("newPassword")}
        >
          <PasswordInput
            name="newPassword"
            placeholder="••••••••••"
            autoComplete="new-password"
            autoFocus
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={Boolean(fieldError?.field("newPassword")) || undefined}
          />
        </Field>

        <Field label="Confirm new password" error={mismatch ?? undefined}>
          <PasswordInput
            name="confirmPassword"
            placeholder="••••••••••"
            autoComplete="new-password"
            required
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            aria-invalid={Boolean(mismatch) || undefined}
          />
        </Field>

        {message && !fieldError?.field("newPassword") && (
          <p role="alert" className="auth-error">
            {message}
          </p>
        )}

        <button type="submit" className="auth-submit" disabled={pending}>
          {pending ? "Saving…" : "Set new password"}
        </button>
      </form>

      <p className="auth-note">
        Setting a new password signs you out everywhere else.
      </p>

      <p className="auth-foot">
        Link expired?
        <Link href="/forgot-password" className="auth-link auth-foot-link">
          Request a new one
        </Link>
      </p>
    </>
  );
}

export default function ResetPasswordPage() {
  return (
    <Suspense fallback={null}>
      <ResetForm />
    </Suspense>
  );
}
