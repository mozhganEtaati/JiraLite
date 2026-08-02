"use client";

import { cx } from "@/lib/format";
import { ApiError } from "@/lib/api";
import { useEffect, useRef } from "react";

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
    <header className="mb-5 flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        {eyebrow && <div className="t-eyebrow mb-1.5">{eyebrow}</div>}
        <h1 className="t-title truncate">{title}</h1>
        {meta && <div className="t-meta mt-1.5">{meta}</div>}
      </div>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
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
      <div className="mb-2 flex items-center justify-between gap-3">
        <h2 className="t-eyebrow flex items-center gap-2">
          {title}
          {count !== undefined && (
            <span className="t-num text-[11px] text-[var(--color-ink-faint)]">
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
          rx="2"
          fill="none"
          stroke="var(--color-rule)"
          strokeWidth="1.5"
        />
        <rect
          x="7.5"
          y="7.5"
          width="27"
          height="27"
          rx="2"
          fill="none"
          stroke="var(--color-pink-soft)"
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
        "rounded-[3px] border border-[#e0c2c2] bg-[#fdf3f3] px-3 py-2 text-[13px] text-[var(--color-alarm)]",
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

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto p-4 pt-[10vh]"
      style={{ background: "rgba(22,22,26,.34)" }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="plate-in w-full"
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
