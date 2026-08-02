import { RegMark } from "@/components/wordmark";

/**
 * The signed-out screen is a press sheet: one side flooded with the structure
 * ink and screened back with a halftone, the other side bare paper. The strip
 * along the bottom is the density wedge a print shop puts on every sheet to
 * check ink coverage — here it is the actual tint scale the app is built from.
 */
export function PressSheet() {
  return (
    <aside
      className="relative hidden flex-col justify-between overflow-hidden lg:flex"
      style={{ background: "var(--color-blue)" }}
    >
      <Halftone />

      <div className="relative flex items-center gap-3 px-10 pt-9">
        <RegMark size={20} light />
        <span
          className="t-display inline-flex items-baseline select-none"
          style={{ fontSize: 20, lineHeight: 1 }}
        >
          <span style={{ color: "#fff" }}>Jira</span>
          <span
            style={{
              color: "var(--color-pink)",
              display: "inline-block",
              transform: "translate(1px, 2px)",
            }}
          >
            Lite
          </span>
        </span>
      </div>

      <div className="relative flex flex-1 flex-col justify-center px-10 py-8">
        <h1
          className="t-display"
          style={{
            fontSize: "clamp(2.6rem, 4.4vw, 3.9rem)",
            fontWeight: 700,
            color: "#fff",
            maxWidth: "13ch",
          }}
        >
          Four moving parts.{" "}
          <span style={{ color: "var(--color-pink)" }}>
            Nothing to configure.
          </span>
        </h1>

        <p
          className="mt-6 max-w-[44ch] text-[15px] leading-relaxed"
          style={{ color: "rgba(255,255,255,.72)" }}
        >
          Projects, boards, sprints, issues. That is the whole model — no
          workflow builder to set up before your team can start working.
        </p>

        <Ladder />
      </div>

      <DensityWedge />
    </aside>
  );
}

/**
 * The one place numbering is honest: this really is an order. You cannot
 * make a project before a workspace, or a board before a project, and that
 * containment is the thing new users get wrong first.
 */
function Ladder() {
  const steps = ["Organization", "Workspace", "Project", "Board"];
  return (
    <ol className="mt-10 flex max-w-[500px] items-baseline gap-6 border-t border-white/20 pt-4">
      {steps.map((label, i) => (
        <li key={label} className="flex items-baseline gap-2.5">
          <span
            className="t-num text-[11px] tracking-[0.12em]"
            style={{ color: "var(--color-pink)" }}
          >
            {String(i + 1).padStart(2, "0")}
          </span>
          <span
            className="text-[13px] font-medium"
            style={{ color: "rgba(255,255,255,.88)" }}
          >
            {label}
          </span>
        </li>
      ))}
    </ol>
  );
}

/** Screened halftone — the second plate laid over the flood. */
function Halftone() {
  return (
    <div
      aria-hidden
      className="pointer-events-none absolute inset-0"
      style={{
        backgroundImage:
          "radial-gradient(var(--color-pink) 1.15px, transparent 1.25px)",
        backgroundSize: "7px 7px",
        opacity: 0.22,
        maskImage:
          "radial-gradient(120% 90% at 82% 8%, #000 0%, transparent 62%)",
        WebkitMaskImage:
          "radial-gradient(120% 90% at 82% 8%, #000 0%, transparent 62%)",
      }}
    />
  );
}

/** 100 · 80 · 60 · 40 · 20 per ink, full bleed along the trim edge. */
function DensityWedge() {
  const steps = [1, 0.8, 0.6, 0.4, 0.2];
  return (
    <div aria-hidden className="flex h-[26px] w-full">
      {steps.map((s) => (
        <span
          key={`b${s}`}
          className="flex-1"
          style={{ background: `rgba(255,255,255,${0.1 + (1 - s) * 0.14})` }}
        />
      ))}
      {steps.map((s) => (
        <span
          key={`p${s}`}
          className="flex-1"
          style={{ background: `color-mix(in srgb, var(--color-pink) ${s * 100}%, transparent)` }}
        />
      ))}
    </div>
  );
}
