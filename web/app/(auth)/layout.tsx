import { PressSheet } from "@/components/press-sheet";

export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[minmax(0,1.12fr)_minmax(430px,0.88fr)]">
      <PressSheet />
      <main
        className="relative flex items-center justify-center px-6 py-14"
        style={{
          // imposition guides, the faint grid a sheet is laid out against
          backgroundImage:
            "radial-gradient(var(--color-rule) 0.7px, transparent 0.8px)",
          backgroundSize: "22px 22px",
        }}
      >
        <div
          className="w-full max-w-[380px] p-8"
          style={{
            background: "var(--color-surface)",
            border: "1px solid var(--color-ink)",
            borderRadius: 3,
            boxShadow: "7px 7px 0 var(--color-pink)",
          }}
        >
          {children}
        </div>
      </main>
    </div>
  );
}
