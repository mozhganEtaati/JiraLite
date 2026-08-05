import Image from "next/image";
import { Logo } from "@/components/wordmark";

/**
 * The signed-out screens' brand half, in two forms.
 *
 * Wide: a tall card that slides under the form panel's edge, the scene standing
 * in front of it, and the dark base the whole composition rests on — absolutely
 * placed, so it only appears once there is width for the overlap.
 *
 * Narrow: the same artwork cropped to the drawing itself and stacked above the
 * form, because the overlap has nowhere to go. Neither image is marked
 * `priority`: whichever one is hidden has no layout box, so the browser never
 * fetches it, and the visible one is in the viewport from the first frame.
 */
export function AuthPromo() {
  return (
    <>
      <div className="auth-promo-compact lg:hidden">
        <Image
          src="/auth-team.webp"
          alt=""
          width={700}
          height={700}
          className="auth-promo-compact-img"
        />
        <div className="auth-promo-compact-brand">
          <Logo size={17} />
        </div>
      </div>

      <div className="auth-aside hidden lg:block" aria-hidden>
        <div className="auth-promo-card">
          <div className="auth-promo-brand">
            <Logo size={17} tone="light" />
          </div>

          <h2 className="auth-promo-title">Project Management Service</h2>

          <p className="auth-promo-sub">
            Everything you need for convenient team work
          </p>
        </div>

        <div className="auth-scene">
          <Image
            src="/auth-team.webp"
            alt=""
            width={700}
            height={700}
            className="auth-scene-img"
          />
        </div>

        <div className="auth-base" />
      </div>
    </>
  );
}
