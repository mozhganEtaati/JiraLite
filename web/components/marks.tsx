import { API_BASE } from "@/lib/api";
import { cx, daysUntil, initials, shortDate } from "@/lib/format";
import type {
  IssueTypeName,
  PriorityName,
  SprintStatusName,
  UserSummary,
} from "@/lib/types";

/**
 * Issue type is carried by shape, not colour — colour is spent on urgency.
 * Bug is the one type allowed the signal ink, because a bug is a signal.
 */
export function TypeMark({
  type,
  size = 12,
}: {
  type: IssueTypeName;
  size?: number;
}) {
  const blue = "var(--color-blue)";
  const pink = "var(--color-pink)";
  const common = { width: size, height: size, viewBox: "0 0 12 12" };
  const title = <title>{type}</title>;

  switch (type) {
    case "Epic":
      return (
        <svg {...common} aria-hidden="false" role="img">
          {title}
          <path d="M6 0.5 11.5 6 6 11.5 0.5 6Z" fill={blue} />
        </svg>
      );
    case "Story":
      return (
        <svg {...common} role="img">
          {title}
          <rect
            x="1"
            y="1"
            width="10"
            height="10"
            rx="1.5"
            fill="none"
            stroke={blue}
            strokeWidth="1.6"
          />
        </svg>
      );
    case "Task":
      return (
        <svg {...common} role="img">
          {title}
          <rect x="1" y="1" width="10" height="10" rx="1.5" fill={blue} />
          <path
            d="M3.6 6.2 5.2 7.8 8.4 4.4"
            fill="none"
            stroke="#fff"
            strokeWidth="1.5"
            strokeLinecap="square"
          />
        </svg>
      );
    case "Bug":
      return (
        <svg {...common} role="img">
          {title}
          <circle cx="6" cy="6" r="5" fill={pink} />
          <circle cx="6" cy="6" r="1.8" fill="#fff" />
        </svg>
      );
    case "Subtask":
      return (
        <svg {...common} role="img">
          {title}
          <path d="M2 2v6h4" fill="none" stroke={blue} strokeWidth="1.5" />
          <rect x="6" y="6" width="5" height="5" rx="1" fill={blue} />
        </svg>
      );
  }
}

/** Four steps of urgency. Only Critical earns the signal ink. */
export function PriorityMark({ priority }: { priority: PriorityName }) {
  const steps: Record<PriorityName, number> = {
    Low: 1,
    Medium: 2,
    High: 3,
    Critical: 4,
  };
  const filled = steps[priority];
  const color =
    priority === "Critical"
      ? "var(--color-pink)"
      : priority === "High"
        ? "var(--color-blue)"
        : "var(--color-blue-soft)";
  return (
    <span
      className="inline-flex items-end gap-[2px]"
      title={`${priority} priority`}
      aria-label={`${priority} priority`}
    >
      {[0, 1, 2, 3].map((i) => (
        <span
          key={i}
          style={{
            width: 2,
            height: 4 + i * 2,
            background: i < filled ? color : "var(--color-rule)",
          }}
        />
      ))}
    </span>
  );
}

/** The part number stamped on every piece of work. */
export function IssueKey({
  issueKey,
  className,
}: {
  issueKey: string;
  className?: string;
}) {
  return <span className={cx("key", className)}>{issueKey}</span>;
}

export function Avatar({
  user,
  size = 22,
}: {
  user?: Pick<UserSummary, "displayName" | "avatarUrl"> | null;
  size?: number;
}) {
  const src = user?.avatarUrl
    ? user.avatarUrl.startsWith("http")
      ? user.avatarUrl
      : `${API_BASE}${user.avatarUrl}`
    : null;

  if (src) {
    return (
      // eslint-disable-next-line @next/next/no-img-element
      <img
        src={src}
        alt={user?.displayName ?? ""}
        width={size}
        height={size}
        className="rounded-full object-cover"
        style={{ width: size, height: size }}
      />
    );
  }
  return (
    <span
      title={user?.displayName ?? "Unassigned"}
      className="inline-flex shrink-0 items-center justify-center rounded-full"
      style={{
        width: size,
        height: size,
        background: user ? "var(--color-blue-wash)" : "var(--color-paper-deep)",
        color: user ? "var(--color-blue)" : "var(--color-ink-faint)",
        fontFamily: "var(--font-mono)",
        fontSize: Math.max(9, size * 0.4),
        fontWeight: 500,
        letterSpacing: "-0.02em",
      }}
    >
      {user ? initials(user.displayName) : "–"}
    </span>
  );
}

/** A due date only turns pink once it is actually a problem. */
export function DueDate({ value }: { value?: string | null }) {
  if (!value) return null;
  const days = daysUntil(value);
  const late = days !== null && days < 0;
  const soon = days !== null && days >= 0 && days <= 2;
  return (
    <span
      className={cx("chip", (late || soon) && "chip-signal")}
      title={late ? `Overdue by ${Math.abs(days!)} days` : `Due ${shortDate(value)}`}
    >
      {late ? "Overdue" : shortDate(value)}
    </span>
  );
}

export function SprintStatusChip({ status }: { status: SprintStatusName }) {
  if (status === "Active")
    return <span className="chip chip-signal">Running</span>;
  if (status === "Completed") return <span className="chip">Completed</span>;
  return <span className="chip chip-ink">Planned</span>;
}

export function LabelChip({ name, color }: { name: string; color: string }) {
  return (
    <span className="chip" style={{ paddingInlineStart: 6 }}>
      <span
        aria-hidden
        style={{
          width: 7,
          height: 7,
          borderRadius: 1,
          background: color?.startsWith("#") ? color : `#${color}`,
        }}
      />
      {name}
    </span>
  );
}
