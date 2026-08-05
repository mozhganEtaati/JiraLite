# Password Reset — Implementation Plan

**Date:** 2026-08-05
**Spec home:** [spec/01-authentication.md](../../spec/01-authentication.md) §16 — currently listed as a Future Improvement ("Password reset via emailed one-time link"). This plan promotes it to a shipped feature and moves it into the numbered requirements.

## 1. What exists today

- `src/Api/Features/Auth/` holds `Register.cs`, `Login.cs`, `Refresh.cs`, `Logout.cs` — one static class per use case, `Request`/`Response`/`Validator`/`Handler`/`MapEndpoint` nested inside (spec/20 §3).
- `RefreshToken` is the existing "hashed credential with expiry + revocation" entity; `PersonalAccessToken` is the same shape with a unique index on `TokenHash`. Both are the template for the new table.
- Email already works end to end: `IEmailSender`/`SmtpEmailSender`, dispatched off the request thread via `backgroundJobClient.Enqueue<SendEmailJob>(...)`. `CreateInvitation.cs` is the closest precedent — it mints a random token, persists it, and enqueues the email.
- `/api/auth/*` is already behind the tight per-IP `RateLimitingSetup.AuthPolicyName` limiter (10/min by default).
- The web login page (`web/app/(auth)/login/page.tsx:79-89`) has a **"Forgot the password"** button that only prints _"Password reset is not wired up yet — a workspace admin can set a new one for you."_ That placeholder is what this feature retires.

## 2. Design decisions

| Decision | Choice | Why |
|---|---|---|
| Token storage | New `PasswordResetToken` table, SHA-256 hash only | Same rule as `RefreshToken` (NFR-02) — a database read alone must not yield a usable token. |
| Token lifetime | Configurable, default **60 minutes** | Much shorter than an invitation (7 days); a reset link is a live credential to an existing account. Config per spec/20 §8, never hardcoded. |
| Single use | `UsedAtUtc` stamped on success | A link that keeps working after use is a standing back door into the account. |
| One live token per user | Requesting a new link marks any outstanding one used | Mirrors `CreateInvitation`'s BR-06 handling of a re-invite. |
| Enumeration | `POST /forgot-password` **always** returns 202, unknown/deactivated email included | Extends NFR-03 (login says nothing about whether the address exists) to this endpoint; otherwise it becomes the enumeration oracle login isn't. |
| Session invalidation | Completing a reset revokes **every** active `RefreshToken` for that user | The usual reason someone resets is that they think somebody else has their password. Leaving live sessions running defeats the reset. |
| Deactivated users | Neither request nor complete a reset (silently, at request time) | BR-08 — a deactivated user cannot authenticate; a reset must not be a way back in. |
| Token generation | Reuse the 32-byte URL-safe pattern from `JwtTokenService.CreateRefreshToken` | Same entropy budget as a refresh token; safe to put in a URL. |

## 3. Backend changes

### 3.1 Domain + persistence
- `src/Api/Common/Domain/PasswordResetToken.cs` — `Id`, `UserId`, `TokenHash`, `ExpiresAtUtc`, `CreatedAtUtc`, `UsedAtUtc?`, computed `IsActive`.
- `Common/Infrastructure/Persistence/Configurations/PasswordResetTokenConfiguration.cs` — table `PasswordResetToken`, `TokenHash` max 128 + **unique index** (lookup path), index on `(UserId, UsedAtUtc)`, FK → `User` `ON DELETE CASCADE` (owned by the User aggregate, same as `RefreshToken`).
- `JiraLiteDbContext` — add `DbSet<PasswordResetToken>`.
- EF migration `AddPasswordResetToken`.

### 3.2 Configuration
- `Features/Auth/PasswordResetOptions.cs` — `TokenLifetimeMinutes` (default 60) and `ResetUrlTemplate` (e.g. `https://app.example.com/reset-password?token={token}`). Blank template falls back to emitting the bare token in the mail body, exactly as the invitation email does today.
- Bound in `Program.cs` and defaulted in `appsettings.json`.

### 3.3 Endpoints

| Method | Route | Auth | Result |
|---|---|---|---|
| POST | `/api/auth/forgot-password` | Anonymous, auth rate-limit | `202 Accepted`, always |
| POST | `/api/auth/reset-password` | Anonymous, auth rate-limit | `204 No Content` |

**`ForgotPassword.cs`** — validate email shape; look the user up case-insensitively; if found **and** active: mark outstanding tokens used, insert a new one, enqueue `SendEmailJob` with the link. Return 202 on every path, including a miss.

**`ResetPassword.cs`** — validate `token` non-empty and `newPassword` against the same rules as `Register` (min 8, one letter, one digit); hash the presented token and look it up; reject with `400` + problem type `invalid-password-reset-token` when missing, used, or expired; reject the same way if the owner is deactivated; otherwise set `PasswordHash`, bump `UpdatedAtUtc`, stamp `UsedAtUtc`, revoke all active refresh tokens, `204`.

Both mapped in `Program.cs` beside the other four auth endpoints.

## 4. Web changes

- **`web/app/(auth)/forgot-password/page.tsx`** — email field → `POST /api/auth/forgot-password` → a "check your inbox" panel that says the same thing whether or not the address is registered (the API's anti-enumeration promise is worthless if the UI leaks it).
- **`web/app/(auth)/reset-password/page.tsx`** — reads `?token=` (in `<Suspense>`, as `login/page.tsx` does with `useSearchParams`), new password + confirm, → `POST /api/auth/reset-password` → redirect to `/login`. Field errors read off `ApiError.field(...)` like the other two auth forms.
- **`web/app/(auth)/login/page.tsx`** — replace the placeholder `<button>` with a `Link` to `/forgot-password`, keeping the `auth-hint` class so it looks identical.
- No new CSS: `auth-title`, `auth-form`, `auth-submit`, `auth-error`, `auth-note`, `auth-foot` already cover these two screens.

## 5. Tests

`tests/JiraLite.Api.IntegrationTests/Auth/PasswordResetTests.cs`:

1. Requesting a reset for an unknown email returns the same 202 as a known one, and writes no row.
2. A requested reset persists the token **hashed** (raw value not recoverable from the row).
3. Completing a reset lets the new password log in and the old one fail.
4. The token is single-use — replaying it is rejected.
5. An expired token is rejected (row backdated directly in the DB).
6. Requesting a second link invalidates the first.
7. Completing a reset kills existing sessions — a refresh token minted before the reset stops working.
8. A deactivated user's reset request produces no token.

Plus `DatabaseResetHelper.TablesInDeleteOrder` gains `"PasswordResetToken"` before `"User"`.

## 6. Spec updates

- `spec/01-authentication.md` — add FR-06/FR-07, BR-09..BR-12, the two endpoints, request/response examples, validation rules, error scenarios and acceptance criteria; drop the now-shipped bullet from §16.
- `spec/18-database.md` §3 — add the `PasswordResetToken` table, and list it in the `→ User` cascade line in §9.

## 7. Order of work

1. Domain entity + EF configuration + `DbSet` + migration
2. `PasswordResetOptions` + `appsettings.json` + `Program.cs` wiring
3. `ForgotPassword.cs`, `ResetPassword.cs`, endpoint mapping
4. Integration tests + `DatabaseResetHelper`
5. Web: two pages + login link
6. Spec docs
7. `dotnet build` + `dotnet test` + `npm run lint`/`tsc`
