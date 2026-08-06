"use client";

import { cx } from "@/lib/format";
import { ApiError } from "@/lib/api";
import { useEffect, useRef, useState } from "react";

/* ── page furniture ───────────────────────────────────────── */

export function PageHead({
  eyebrow,
  title,
  meta,
  actions,
}: {
  eyebrow?: React.ReactNode;
  title: React.ReactNode;
  meta?: React.ReactNode;
  actions?: React.ReactNode;
}) {
  return (
    <header className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        {/* the node-and-hairline says what this screen sits inside */}
        {eyebrow && <div className="path mb-2">{eyebrow}</div>}
        <h1 className="t-display truncate">{title}</h1>
        {meta && (
          <p className="mt-2 max-w-[70ch] text-[13px] text-[var(--color-ink-soft)]">
            {meta}
          </p>
        )}
      </div>
      {actions && (
        <div className="flex flex-wrap items-center gap-2">{actions}</div>
      )}
    </header>
  );
}

export function Section({
  title,
  count,
  actions,
  children,
  className,
}: {
  title: string;
  count?: number;
  actions?: React.ReactNode;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <section className={className}>
      <div className="mb-2.5 flex items-center justify-between gap-3">
        <h2 className="t-eyebrow flex items-baseline gap-2">
          {title}
          {/* the count is data, so it is set in the data face */}
          {count !== undefined && (
            <span className="t-num text-[12px] text-[var(--color-ink-faint)]">
              {count}
            </span>
          )}
        </h2>
        {actions}
      </div>
      {children}
    </section>
  );
}

/* ── state ────────────────────────────────────────────────── */

export function Loading({ label = "Loading" }: { label?: string }) {
  return (
    <div className="px-4 py-10" role="status" aria-live="polite">
      <div className="press-bar mx-auto max-w-[120px] rounded-full" />
      <p className="t-meta mt-3 text-center">{label}…</p>
    </div>
  );
}

/** An empty screen is an invitation to act, so it always names the next move. */
export function Empty({
  title,
  hint,
  action,
}: {
  title: string;
  hint?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col items-center gap-2 px-6 py-12 text-center">
      <svg width="34" height="34" viewBox="0 0 34 34" aria-hidden>
        <rect
          x="3.5"
          y="3.5"
          width="27"
          height="27"
          rx="4"
          fill="none"
          stroke="var(--color-rule)"
          strokeWidth="1.5"
        />
        <rect
          x="7.5"
          y="7.5"
          width="27"
          height="27"
          rx="4"
          fill="none"
          stroke="var(--color-blue-soft)"
          strokeWidth="1.5"
        />
      </svg>
      <p className="mt-1 font-medium">{title}</p>
      {hint && (
        <p className="max-w-[42ch] text-[13px] text-[var(--color-ink-soft)]">
          {hint}
        </p>
      )}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}

/** Errors state what happened and what to do — never apologise, never vague. */
export function ErrorNote({
  error,
  className,
}: {
  error: unknown;
  className?: string;
}) {
  if (!error) return null;
  const message =
    error instanceof ApiError
      ? (error.detail ?? error.title)
      : error instanceof Error
        ? error.message
        : "That request could not be completed.";
  return (
    <p
      role="alert"
      className={cx(
        "rounded-[var(--radius-md)] border border-[var(--color-error-rule)] bg-[var(--color-error-bg)] px-3 py-2 text-[13px] text-[var(--color-alarm)]",
        className,
      )}
    >
      {message}
    </p>
  );
}

/* ── forms ────────────────────────────────────────────────── */

export function Field({
  label,
  hint,
  error,
  children,
  className,
}: {
  label: string;
  hint?: string;
  error?: string;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <label className={cx("block", className)}>
      <span className="label">{label}</span>
      {children}
      {error ? (
        <span className="mt-1 block text-[12px] text-[var(--color-alarm)]">
          {error}
        </span>
      ) : hint ? (
        <span className="mt-1 block text-[12px] text-[var(--color-ink-faint)]">
          {hint}
        </span>
      ) : null}
    </label>
  );
}

/**
 * A password well with the reveal control inside it. Typing a password you
 * cannot see is the most common way people lock themselves out, so the eye
 * is always there — never a checkbox parked under the field.
 */
export function PasswordInput({
  className,
  ...props
}: React.InputHTMLAttributes<HTMLInputElement>) {
  const [shown, setShown] = useState(false);
  return (
    <span className="field-inset block">
      <input
        {...props}
        type={shown ? "text" : "password"}
        className={cx("field", className)}
      />
      <button
        type="button"
        className="field-eye"
        onClick={() => setShown((v) => !v)}
        aria-label={shown ? "Hide password" : "Show password"}
        aria-pressed={shown}
      >
        <EyeMark off={shown} />
      </button>
    </span>
  );
}

function EyeMark({ off }: { off: boolean }) {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.4"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d="M1.2 8S3.6 3.6 8 3.6 14.8 8 14.8 8 12.4 12.4 8 12.4 1.2 8 1.2 8Z" />
      <circle cx="8" cy="8" r="1.9" />
      {off && <path d="M2.6 13.4 13.4 2.6" stroke="var(--color-pink)" />}
    </svg>
  );
}

/* ── dialog ───────────────────────────────────────────────── */

export function Modal({
  open,
  onClose,
  title,
  children,
  width = 480,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: React.ReactNode;
  width?: number;
}) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    ref.current?.querySelector<HTMLElement>(
      "input, textarea, select, button",
    )?.focus();
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!open) return null;

  /*
   * The panel sets text-start explicitly. A dialog is fixed-positioned but is
   * still a DOM descendant of whatever opened it, so one invoked from inside a
   * right-aligned cell inherits that alignment and sets its own labels and
   * hints ragged-left.
   */
  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto p-4 pt-[10vh]"
      style={{ background: "var(--color-scrim)" }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="plate-in w-full text-start"
        style={{
          maxWidth: width,
          background: "var(--color-surface)",
          border: "1px solid var(--color-ink)",
          borderRadius: 3,
          boxShadow: "6px 6px 0 var(--color-pink)",
        }}
      >
        <div className="flex items-center justify-between border-b border-[var(--color-rule)] px-4 py-3">
          <h2 className="t-title text-[16px]">{title}</h2>
          <button
            type="button"
            className="btn btn-bare btn-sm"
            onClick={onClose}
            aria-label="Close"
          >
            Esc
          </button>
        </div>
        <div className="p-4">{children}</div>
      </div>
    </div>
  );
}

/* ── misc ─────────────────────────────────────────────────── */

export function LoadMore({
  hasNext,
  loading,
  onClick,
}: {
  hasNext: boolean;
  loading: boolean;
  onClick: () => void;
}) {
  if (!hasNext) return null;
  return (
    <div className="border-t border-[var(--color-rule-soft)] p-2 text-center">
      <button
        type="button"
        className="btn btn-bare btn-sm"
        onClick={onClick}
        disabled={loading}
      >
        {loading ? "Loading…" : "Load more"}
      </button>
    </div>
  );
}

export function Confirm({
  open,
  title,
  body,
  confirmLabel,
  onConfirm,
  onCancel,
  pending,
}: {
  open: boolean;
  title: string;
  body: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  pending?: boolean;
}) {
  return (
    <Modal open={open} onClose={onCancel} title={title} width={400}>
      <p className="text-[13px] text-[var(--color-ink-soft)]">{body}</p>
      <div className="mt-4 flex justify-end gap-2">
        <button type="button" className="btn btn-ghost" onClick={onCancel}>
          Keep it
        </button>
        <button
          type="button"
          className="btn btn-danger"
          onClick={onConfirm}
          disabled={pending}
        >
          {pending ? "Working…" : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}
