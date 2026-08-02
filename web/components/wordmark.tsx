/**
 * The wordmark is the two plates, printed out of register: "Jira" on the
 * structure plate, "Lite" slipped down-right on the signal plate.
 */
export function Wordmark({ size = 20 }: { size?: number }) {
  return (
    <span
      className="t-display inline-flex items-baseline select-none"
      style={{ fontSize: size, lineHeight: 1 }}
      aria-label="JiraLite"
    >
      <span style={{ color: "var(--color-blue)" }}>Jira</span>
      <span
        style={{
          color: "var(--color-pink)",
          transform: `translate(${size * 0.05}px, ${size * 0.09}px)`,
          display: "inline-block",
        }}
      >
        Lite
      </span>
    </span>
  );
}

/** Printer's registration target — the app's anchor glyph. */
export function RegMark({
  size = 18,
  light = false,
}: {
  size?: number;
  light?: boolean;
}) {
  return (
    <svg width={size} height={size} viewBox="0 0 18 18" aria-hidden>
      <circle
        cx="9"
        cy="9"
        r="5.25"
        fill="none"
        stroke={light ? "#fff" : "var(--color-blue)"}
        strokeWidth="1"
      />
      <path
        d="M9 0v18M0 9h18"
        stroke="var(--color-pink)"
        strokeWidth="1"
        opacity="0.9"
      />
    </svg>
  );
}
