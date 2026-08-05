"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { ApiError } from "@/lib/api";
import { useSession } from "@/lib/providers";
import { Field, PasswordInput } from "@/components/kit";

function SignInForm() {
  const { signIn } = useSession();
  const router = useRouter();
  const params = useSearchParams();
  const next = params.get("next");

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [note, setNote] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setNote(null);
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
      <h1 className="auth-title">Log in</h1>

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

        <div>
          <Field label="Password" error={fieldError?.field("password")}>
            <PasswordInput
              name="password"
              placeholder="••••••••••"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-invalid={Boolean(fieldError?.field("password")) || undefined}
            />
          </Field>

          <button
            type="button"
            className="auth-hint"
            onClick={() =>
              setNote(
                "Password reset is not wired up yet — a workspace admin can set a new one for you.",
              )
            }
          >
            Forgot the password
          </button>
        </div>

        {message && !fieldError?.errors && (
          <p role="alert" className="auth-error">
            {message}
          </p>
        )}

        <button type="submit" className="auth-submit" disabled={pending}>
          {pending ? "Logging in…" : "Log in"}
        </button>
      </form>

      <p className="auth-divider">Or log in via:</p>

      <div className="auth-socials">
        {providers.map((p) => (
          <button
            key={p.name}
            type="button"
            className="auth-social"
            aria-label={`Log in with ${p.name}`}
            onClick={() =>
              setNote(
                `${p.name} sign-in is not connected yet — use your e-mail and password.`,
              )
            }
          >
            {p.mark}
          </button>
        ))}
      </div>

      {note && (
        <p className="auth-note" role="status">
          {note}
        </p>
      )}

      <p className="auth-foot">
        Don&rsquo;t have an account yet?
        <Link href="/register" className="auth-link auth-foot-link">
          Register now!
        </Link>
      </p>
    </>
  );
}

/**
 * The marks are here, but none of them are wired: the API exposes only
 * e-mail and password (`/api/auth/login`). Each button says so rather than
 * failing silently, and swaps to a real redirect the day OAuth lands.
 */
const providers = [
  {
    name: "Google",
    mark: (
      <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden>
        <path
          fill="#4285F4"
          d="M17.64 9.2c0-.64-.06-1.25-.16-1.84H9v3.48h4.84a4.14 4.14 0 0 1-1.8 2.72v2.26h2.92c1.7-1.57 2.68-3.88 2.68-6.62Z"
        />
        <path
          fill="#34A853"
          d="M9 18c2.43 0 4.47-.8 5.96-2.18l-2.92-2.26c-.8.54-1.84.86-3.04.86-2.34 0-4.32-1.58-5.03-3.7H.96v2.33A9 9 0 0 0 9 18Z"
        />
        <path
          fill="#FBBC05"
          d="M3.97 10.72a5.4 5.4 0 0 1 0-3.44V4.95H.96a9 9 0 0 0 0 8.1l3.01-2.33Z"
        />
        <path
          fill="#EA4335"
          d="M9 3.58c1.32 0 2.5.45 3.44 1.35l2.58-2.58C13.46.89 11.43 0 9 0A9 9 0 0 0 .96 4.95l3.01 2.33C4.68 5.16 6.66 3.58 9 3.58Z"
        />
      </svg>
    ),
  },
  {
    name: "GitHub",
    mark: (
      <svg width="18" height="18" viewBox="0 0 24 24" fill="#24292f" aria-hidden>
        <path d="M12 .5C5.37.5 0 5.87 0 12.5c0 5.3 3.44 9.8 8.21 11.39.6.11.82-.26.82-.58v-2.03c-3.34.73-4.04-1.61-4.04-1.61-.55-1.39-1.34-1.76-1.34-1.76-1.09-.75.08-.73.08-.73 1.2.08 1.84 1.24 1.84 1.24 1.07 1.84 2.81 1.31 3.5 1 .11-.78.42-1.31.76-1.61-2.67-.3-5.47-1.34-5.47-5.96 0-1.32.47-2.39 1.24-3.23-.12-.31-.54-1.53.12-3.18 0 0 1.01-.32 3.3 1.23a11.5 11.5 0 0 1 6 0c2.29-1.55 3.3-1.23 3.3-1.23.66 1.65.24 2.87.12 3.18.77.84 1.24 1.91 1.24 3.23 0 4.63-2.81 5.65-5.49 5.95.43.37.81 1.1.81 2.22v3.29c0 .32.21.7.82.58A12 12 0 0 0 24 12.5C24 5.87 18.63.5 12 .5Z" />
      </svg>
    ),
  },
  {
    name: "Microsoft",
    mark: (
      <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden>
        <rect width="7" height="7" fill="#F25022" />
        <rect x="9" width="7" height="7" fill="#7FBA00" />
        <rect y="9" width="7" height="7" fill="#00A4EF" />
        <rect x="9" y="9" width="7" height="7" fill="#FFB900" />
      </svg>
    ),
  },
  {
    name: "Facebook",
    mark: (
      <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden>
        <path
          fill="#1877F2"
          d="M24 12a12 12 0 1 0-13.88 11.85v-8.38H7.08V12h3.04V9.36c0-3 1.79-4.67 4.53-4.67 1.31 0 2.68.24 2.68.24v2.95h-1.51c-1.49 0-1.95.92-1.95 1.87V12h3.32l-.53 3.47h-2.79v8.38A12 12 0 0 0 24 12Z"
        />
      </svg>
    ),
  },
];

export default function LoginPage() {
  return (
    <Suspense fallback={null}>
      <SignInForm />
    </Suspense>
  );
}
