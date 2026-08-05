"use client";

import Link from "next/link";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import { Field } from "@/components/kit";

/**
 * The API answers 202 whether or not the address is registered, so that it
 * cannot be used to find out which addresses have accounts. This screen keeps
 * that promise: on success it says the same sentence either way, and never
 * "no account with that email" — a UI that reported the difference would hand
 * back exactly the answer the endpoint withholds.
 */
export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [pending, setPending] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setPending(true);
    try {
      await api.anon("/api/auth/forgot-password", { email });
      setSent(true);
    } catch (err) {
      setError(err);
    } finally {
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

  if (sent) {
    return (
      <>
        <h1 className="auth-title">Check your inbox</h1>

        <p className="auth-note" role="status">
          If {email.trim() || "that address"} is registered, a password reset
          link is on its way. The link works once and expires in an hour.
        </p>

        <p className="auth-note">
          Nothing arrived? Check the spam folder, then{" "}
          <button
            type="button"
            className="auth-link"
            onClick={() => setSent(false)}
          >
            try another address
          </button>
          .
        </p>

        <p className="auth-foot">
          <Link href="/login" className="auth-link auth-foot-link">
            Back to log in
          </Link>
        </p>
      </>
    );
  }

  return (
    <>
      <h1 className="auth-title">Reset your password</h1>

      <p className="auth-note">
        Enter the e-mail on your account and we&rsquo;ll send you a link to set
        a new password.
      </p>

      <form onSubmit={onSubmit} className="auth-form" noValidate>
        <Field label="E-mail" error={fieldError?.field("email")}>
          <input
            className="field"
            type="email"
            name="email"
            placeholder="example.company@mail.com"
            autoComplete="email"
            autoFocus
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={Boolean(fieldError?.field("email")) || undefined}
          />
        </Field>

        {message && !fieldError?.errors && (
          <p role="alert" className="auth-error">
            {message}
          </p>
        )}

        <button type="submit" className="auth-submit" disabled={pending}>
          {pending ? "Sending…" : "Send reset link"}
        </button>
      </form>

      <p className="auth-foot">
        Remembered it?
        <Link href="/login" className="auth-link auth-foot-link">
          Log in
        </Link>
      </p>
    </>
  );
}
