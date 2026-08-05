"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import { useSession } from "@/lib/providers";
import { Field, PasswordInput } from "@/components/kit";

export default function RegisterPage() {
  const { signIn } = useSession();
  const router = useRouter();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [pending, setPending] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setPending(true);
    try {
      await api.anon("/api/auth/register", { email, password });
      // Register returns the new user, not tokens — sign in with the same
      // credentials so the person lands inside instead of at another form.
      await signIn(email, password);
      router.replace("/dashboard");
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

  return (
    <>
      <h1 className="auth-title">Register</h1>

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

        <Field
          label="Password"
          hint="At least 8 characters."
          error={fieldError?.field("password")}
        >
          <PasswordInput
            name="password"
            placeholder="••••••••••"
            autoComplete="new-password"
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={Boolean(fieldError?.field("password")) || undefined}
          />
        </Field>

        {message && !fieldError?.errors && (
          <p role="alert" className="auth-error">
            {message}
          </p>
        )}

        <button type="submit" className="auth-submit" disabled={pending}>
          {pending ? "Creating account…" : "Create account"}
        </button>
      </form>

      <p className="auth-foot">
        Already have an account?{" "}
        <Link href="/login" className="auth-link">
          Log in
        </Link>
      </p>
    </>
  );
}
