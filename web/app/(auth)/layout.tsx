import { AuthPromo } from "@/components/auth-promo";
import "./auth.css";

export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="auth-scope auth-stage flex-1">
      <div className="auth-composition">
        <AuthPromo />
        <section className="auth-panel">
          <div className="auth-panel-inner">{children}</div>
        </section>
      </div>
    </div>
  );
}
