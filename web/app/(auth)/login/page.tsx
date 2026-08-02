"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { ApiError } from "@/lib/api";
import { useSession } from "@/lib/providers";
import { ErrorNote, Field } from "@/components/ui";
import { RegMark, Wordmark } from "@/components/wordmark";

function SignInForm() {
  const { signIn } = useSession();
  const router = useRouter();
  const params = useSearchParams();
  const next = params.get("next");

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [pending, setPending] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setPending(true);
    try {
      await signIn(email, password);
      router.replace(next || "/dashboard");
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

      <div className="t-eyebrow mb-2">Sign in</div>
      <h1 className="t-title mb-6">Welcome back</h1>

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

        <Field label="Password" error={fieldError?.field("password")}>
          <input
            className="field"
            type="password"
            name="password"
            autoComplete="current-password"
            required
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
          {pending ? "Signing in…" : "Sign in"}
        </button>
      </form>

      <p className="mt-6 border-t border-[var(--color-rule)] pt-4 text-[13px] text-[var(--color-ink-soft)]">
        No account yet?{" "}
        <Link
          href="/register"
          className="font-medium text-[var(--color-blue)] underline decoration-[var(--color-pink)] decoration-2 underline-offset-2"
        >
          Create one
        </Link>
      </p>
    </>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={null}>
      <SignInForm />
    </Suspense>
  );
}
