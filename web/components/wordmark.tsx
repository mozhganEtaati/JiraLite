/**
 * The mark is the node: a hollow ring with a hairline running out of it, the
 * same device the rail uses for the screen you are on and the page head uses
 * to name what a screen sits inside. The logo is simply its first appearance.
 */
export function NodeMark({
  size = 18,
  tone = "dark",
}: {
  size?: number;
  /** "light" for the navy rail, "dark" for a white surface */
  tone?: "light" | "dark";
}) {
  // The light tone rides two different grounds — the navy rail and the blue
  // promo card — so it is built from white rather than a blue that would sink
  // into the card.
  const ring = tone === "light" ? "#ffffff" : "var(--color-blue)";
  const line =
    tone === "light" ? "rgba(255,255,255,.5)" : "var(--color-rule)";

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 18 18"
      fill="none"
      aria-hidden
    >
      <circle cx="6.5" cy="9" r="4" stroke={ring} strokeWidth="1.75" />
      <path d="M12 9h5.5" stroke={line} strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

/**
 * The lockup: mark and wordmark, locked to each other.
 *
 * Every screen that shows the brand uses this — the rail and both halves of the
 * signed-out promo — so the signed-in and signed-out sides of the product can
 * never drift into two different logos again.
 */
export function Logo({
  size = 18,
  tone = "dark",
}: {
  size?: number;
  tone?: "light" | "dark";
}) {
  return (
    <span
      className="inline-flex items-center"
      style={{ gap: size * 0.44 }}
      aria-label="JiraLite"
      role="img"
    >
      <NodeMark size={size} tone={tone} />
      <Wordmark size={size} tone={tone} />
    </span>
  );
}

/**
 * Two weights of one face rather than two colours: the product is one thing,
 * and "Lite" is the qualifier, so it is set lighter instead of louder.
 */
export function Wordmark({
  size = 20,
  tone = "dark",
}: {
  size?: number;
  tone?: "light" | "dark";
}) {
  return (
    <span
      className="inline-flex items-baseline select-none"
      style={{
        fontFamily: "var(--font-display)",
        fontSize: size,
        lineHeight: 1,
        letterSpacing: "-0.03em",
      }}
      aria-label="JiraLite"
    >
      <span
        style={{
          fontWeight: 700,
          color: tone === "light" ? "#ffffff" : "var(--color-ink)",
        }}
      >
        Jira
      </span>
      <span
        style={{
          fontWeight: 400,
          color: tone === "light" ? "rgba(255,255,255,.72)" : "var(--color-blue)",
        }}
      >
        Lite
      </span>
    </span>
  );
}
