"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import { useSession } from "@/lib/providers";
import { ErrorNote, Field } from "@/components/ui";
import { RegMark, Wordmark } from "@/components/wordmark";

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

  return (
    <>
      <div className="mb-8 flex items-center gap-2.5 lg:hidden">
        <RegMark size={18} />
        <Wordmark size={19} />
      </div>

      <div className="t-eyebrow mb-2">Create account</div>
      <h1 className="t-title mb-6">Start tracking work</h1>

      <form onSubmit={onSubmit} className="space-y-3.5" noValidate>
        <Field label="Email" error={fieldError?.field("email")}>
          <input
            className="field"
            type="email"
            name="email"
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
          <input
            className="field"
            type="password"
            name="password"
            autoComplete="new-password"
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={Boolean(fieldError?.field("password")) || undefined}
          />
        </Field>

        {error && !fieldError?.errors && <ErrorNote error={error} />}

        <button
          type="submit"
          className="btn btn-primary w-full"
          disabled={pending}
        >
          {pending ? "Creating account…" : "Create account"}
        </button>
      </form>

      <p className="mt-6 border-t border-[var(--color-rule)] pt-4 text-[13px] text-[var(--color-ink-soft)]">
        Already have an account?{" "}
        <Link
          href="/login"
          className="font-medium text-[var(--color-blue)] underline decoration-[var(--color-pink)] decoration-2 underline-offset-2"
        >
          Sign in
        </Link>
      </p>
    </>
  );
}
