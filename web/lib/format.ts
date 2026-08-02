import {
  differenceInCalendarDays,
  format,
  formatDistanceToNowStrict,
  parseISO,
} from "date-fns";

export function fromUtc(value: string) {
  return parseISO(value.endsWith("Z") || value.length <= 10 ? value : `${value}Z`);
}

export function shortDate(value?: string | null) {
  if (!value) return "—";
  return format(fromUtc(value), "d MMM");
}

export function fullDate(value?: string | null) {
  if (!value) return "—";
  return format(fromUtc(value), "d MMM yyyy");
}

export function ago(value?: string | null) {
  if (!value) return "—";
  return formatDistanceToNowStrict(fromUtc(value), { addSuffix: true });
}

/** Negative = overdue. Used to decide when a due date earns the signal ink. */
export function daysUntil(value?: string | null) {
  if (!value) return null;
  return differenceInCalendarDays(fromUtc(value), new Date());
}

export function initials(name?: string | null) {
  if (!name) return "?";
  const parts = name.trim().split(/\s+/).slice(0, 2);
  return parts.map((p) => p[0]?.toUpperCase() ?? "").join("") || "?";
}

export function fileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}
