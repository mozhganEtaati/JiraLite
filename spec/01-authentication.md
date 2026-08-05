# 01 — Authentication

## 1. Overview

Authentication establishes and maintains a User's identity across API requests. JiraLite uses JWT access tokens for request authentication and rotating refresh tokens for session renewal. This document covers Register, Login, Logout, Refresh Token, and Password Reset only. Workspace/Organization membership and role assignment are separate concerns — see [03-workspaces.md](03-workspaces.md) and [16-rbac.md](16-rbac.md).

## 2. Business Goal

Allow any user to create an account and securely authenticate, without requiring a workspace, project, or role to exist first. A registered User is a platform-level identity that later joins one or more Workspaces.

## 3. User Stories

| ID | Story |
|---|---|
| US-01 | As a new user, I can register with an email and password so I can access the platform. |
| US-02 | As a registered user, I can log in with my email and password to receive an access token. |
| US-03 | As a logged-in user, my session stays active without re-entering my password, via silent token refresh. |
| US-04 | As a logged-in user, I can log out so my refresh token can no longer be used. |
| US-05 | As a user who has forgotten my password, I can request a reset link by email so I can regain access without contacting an admin. |
| US-06 | As a user holding a reset link, I can set a new password, and any session opened with the old one stops working. |

## 4. Functional Requirements

- FR-01: A user can register with a unique email and a password.
- FR-02: A user can log in with email + password and receive an access token and a refresh token.
- FR-03: A user can exchange a valid, unrevoked refresh token for a new access token and a new refresh token (rotation).
- FR-04: A user can log out, which revokes the presented refresh token.
- FR-05: Access tokens are short-lived JWTs; refresh tokens are long-lived and stored server-side (hashed) for revocation checks.
- FR-06: A user can request a password reset for an email address, which sends a single-use, time-limited link to that address.
- FR-07: A user can present a valid reset token together with a new password to replace their credential.

## 5. Non-Functional Requirements

- NFR-01: Passwords are never stored or logged in plaintext; stored as a salted hash using an industry-standard algorithm (e.g., BCrypt/Argon2 — selected in [20-coding-guidelines.md](20-coding-guidelines.md)).
- NFR-02: Refresh tokens are stored server-side as a hash, not in plaintext, so a database read alone cannot yield a usable token.
- NFR-03: Login responses do not reveal whether the failure was due to an unknown email or a wrong password (mitigates user enumeration).
- NFR-04: All authentication endpoints are rate-limited (limit values defined in [19-api-guidelines.md](19-api-guidelines.md)).
- NFR-05: Password reset tokens are stored server-side as a hash, never in plaintext — the same rule as refresh tokens (NFR-02).
- NFR-06: The reset request endpoint responds identically whether or not the email is registered, so it cannot be used to enumerate accounts (the counterpart of NFR-03 for the unauthenticated reset path).

## 6. Business Rules

- BR-01: Email is the unique identifier for login; uniqueness is case-insensitive.
- BR-02: Registration creates a platform-level `User` account only — it does not create or join any Organization/Workspace. Workspace membership happens via invitation ([03-workspaces.md](03-workspaces.md)) or first-workspace creation.
- BR-03: Refresh tokens are single-use. Presenting a refresh token issues a new access token and a new refresh token, and immediately revokes the presented one (rotation).
- BR-04: If a **revoked** refresh token is presented again, this is treated as a possible token theft: the entire token family for that user is revoked, forcing re-login.
- BR-05: Refresh tokens and access tokens are transported in the JSON request/response body, not cookies — JiraLite is an API-only backend with no assumed cookie-handling client.
- BR-06: JWT access tokens carry identity claims only (`sub` = UserId, `email`). They do not embed roles or permissions, because a User's role can differ per Workspace/Project. Authorization is evaluated per-request against `WorkspaceMember`/`ProjectMember` records — see [16-rbac.md](16-rbac.md).
- BR-07: Logging out revokes only the specific refresh token presented; other active sessions (other devices) remain valid. "Logout everywhere" is not in V1 (see §16).
- BR-08: A deactivated User (`IsActive = false`) cannot log in and cannot refresh an existing session; any of their outstanding refresh tokens are treated as revoked.
- BR-09: Password reset tokens are short-lived — far shorter than an invitation ([03-workspaces.md](03-workspaces.md) BR-07), because the link is a live credential to an account that already exists. The lifetime is configuration, defaulting to 60 minutes.
- BR-10: A user has at most one outstanding reset token. Requesting a new link immediately invalidates any previous unused one, so a mailbox breach exposes one live link rather than every link ever requested.
- BR-11: A reset token is single-use. Redeeming it marks it used and revokes **all** of that user's active refresh tokens — the usual reason to reset is believing someone else has the password, and leaving their sessions alive would defeat the reset.
- BR-12: A deactivated User can neither request nor complete a password reset; the request endpoint still answers identically (NFR-06), it simply issues no token.

## 7. Database Entities

Full canonical schema (types, indexes, constraints) is consolidated in [18-database.md](18-database.md). This section defines the fields owned by this feature.

### User (auth-relevant fields)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| Email | string(256) | No | Unique, case-insensitive |
| PasswordHash | string | No | Never returned in any response |
| IsActive | bool | No | Default `true`; `false` = deactivated, cannot authenticate |
| CreatedAtUtc | datetime2 | No | |
| UpdatedAtUtc | datetime2 | No | |

Profile fields (name, avatar, etc.) belong to `UserProfile` — see [02-users.md](02-users.md).

### RefreshToken

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| UserId | Guid (FK → User) | No | |
| TokenHash | string | No | SHA-256 hash of the raw token value |
| ExpiresAtUtc | datetime2 | No | |
| CreatedAtUtc | datetime2 | No | |
| RevokedAtUtc | datetime2 | Yes | Null = active |
| ReplacedByTokenId | Guid (FK → RefreshToken) | Yes | Set on rotation, links to the successor token |

### PasswordResetToken

| Column | Type | Nullable | Notes |
|---|---|---|---|
| Id | Guid (PK) | No | |
| UserId | Guid (FK → User) | No | |
| TokenHash | string | No | SHA-256 hash of the raw token value; unique |
| ExpiresAtUtc | datetime2 | No | |
| CreatedAtUtc | datetime2 | No | |
| UsedAtUtc | datetime2 | Yes | Null = redeemable; set on redemption *and* on supersession (BR-10) |

## 8. Relationships

- `User (1) → RefreshToken (N)` — a User may have multiple active refresh tokens (one per device/session).
- `User (1) → PasswordResetToken (N)` — historical rows are retained; at most one is unused at a time (BR-10).
- `RefreshToken.ReplacedByTokenId → RefreshToken.Id` — self-referencing, tracks rotation chains for reuse detection (BR-04).

## 9. API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/register` | Anonymous | Create a new User account |
| POST | `/api/auth/login` | Anonymous | Authenticate and receive tokens |
| POST | `/api/auth/refresh` | Anonymous (requires valid refresh token in body) | Rotate tokens |
| POST | `/api/auth/logout` | Authenticated | Revoke the current refresh token |
| POST | `/api/auth/forgot-password` | Anonymous | Request a reset link for an email address |
| POST | `/api/auth/reset-password` | Anonymous (the reset token is the credential) | Redeem a reset token for a new password |

Routing/naming conventions follow [19-api-guidelines.md](19-api-guidelines.md).

## 10. Request Examples

**Register**
```http
POST /api/auth/register
Content-Type: application/json

{
  "email": "jane.doe@example.com",
  "password": "Str0ngP@ssword!"
}
```

**Login**
```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "jane.doe@example.com",
  "password": "Str0ngP@ssword!"
}
```

**Refresh**
```http
POST /api/auth/refresh
Content-Type: application/json

{
  "refreshToken": "8f14e45f-ceea-4d..."
}
```

**Logout**
```http
POST /api/auth/logout
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "refreshToken": "8f14e45f-ceea-4d..."
}
```

**Forgot Password**
```http
POST /api/auth/forgot-password
Content-Type: application/json

{
  "email": "jane.doe@example.com"
}
```

**Reset Password**
```http
POST /api/auth/reset-password
Content-Type: application/json

{
  "token": "9f2c1b...",
  "newPassword": "N3wStr0ngP@ssword!"
}
```

## 11. Response Examples

**Register — 201 Created**
```json
{
  "id": "3c1a1e2e-6b1a-4e9a-9c3e-1a2b3c4d5e6f",
  "email": "jane.doe@example.com",
  "createdAtUtc": "2026-07-31T10:00:00Z"
}
```

**Login / Refresh — 200 OK**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "accessTokenExpiresAtUtc": "2026-07-31T10:15:00Z",
  "refreshToken": "9a2b7c1d-4e5f-4a6b-8c9d-0e1f2a3b4c5d",
  "refreshTokenExpiresAtUtc": "2026-08-14T10:00:00Z"
}
```

**Logout — 204 No Content**
(empty body)

**Forgot Password — 202 Accepted**
```json
{
  "message": "If that email is registered, a password reset link is on its way."
}
```
Returned unchanged for an unknown or deactivated address (NFR-06, BR-12).

**Reset Password — 204 No Content**
(empty body)

## 12. Validation Rules

| Field | Rule |
|---|---|
| email | Required, valid email format, max 256 chars |
| password (register) | Required, min 8 chars, at least one letter and one digit |
| refreshToken | Required, well-formed GUID string |
| token (reset-password) | Required |
| newPassword (reset-password) | Same rules as `password` on register — a reset must not set a password registration would refuse |

## 13. Error Scenarios

| Scenario | Status | Notes |
|---|---|---|
| Email already registered | 409 Conflict | |
| Invalid email/password format on register | 400 Bad Request | Field-level Problem Details |
| Login with unknown email or wrong password | 401 Unauthorized | Generic "invalid credentials" message (NFR-03) |
| Login for deactivated user | 401 Unauthorized | Same generic message; does not reveal deactivation |
| Refresh with expired token | 401 Unauthorized | Client must re-authenticate |
| Refresh with already-revoked token | 401 Unauthorized | Triggers full token-family revocation (BR-04) |
| Refresh with unknown/malformed token | 401 Unauthorized | |
| Logout with a refresh token not owned by the authenticated user | 403 Forbidden | |
| Logout without a valid access token | 401 Unauthorized | |
| Forgot-password for an unknown or deactivated email | 202 Accepted | Deliberately indistinguishable from success (NFR-06) |
| Forgot-password with a malformed email | 400 Bad Request | Shape validation only — says nothing about registration |
| Reset with an unknown, used, or expired token | 400 Bad Request | One generic message for all three; a specific one would reveal which tokens exist |
| Reset for a deactivated user | 400 Bad Request | Same generic message |
| Reset with a new password failing the password rules | 400 Bad Request | Field-level Problem Details; the token is left unspent |

Global error response shape follows [19-api-guidelines.md](19-api-guidelines.md) (RFC 7807 Problem Details).

## 14. Authorization Rules

| Endpoint | Requirement |
|---|---|
| Register | None (public) |
| Login | None (public) |
| Refresh | None (public); refresh token itself is the credential |
| Logout | Valid access token; the refresh token in the body must belong to the same UserId as the access token's `sub` claim |
| Forgot Password | None (public) |
| Reset Password | None (public); the emailed reset token itself is the credential |

No role (Admin/Project Admin/Developer/Viewer) is relevant to this document — see [16-rbac.md](16-rbac.md) for role scope.

## 15. Acceptance Criteria

- Given a unique email and valid password, when a user registers, then a User record is created and no tokens are issued (register does not auto-login).
- Given valid credentials, when a user logs in, then an access token and refresh token are returned and a `RefreshToken` record is persisted (hashed).
- Given an unrevoked, unexpired refresh token, when refreshed, then a new access/refresh token pair is returned and the old refresh token is marked revoked with `ReplacedByTokenId` set.
- Given a refresh token that was already revoked, when presented again, then the request is rejected and all of that user's active refresh tokens are revoked.
- Given a valid access token and its matching refresh token, when logout is called, then the refresh token is revoked and a subsequent refresh attempt with it fails.
- Given a deactivated user's credentials, when login is attempted, then it fails with the same generic message as invalid credentials.
- Given an unregistered email, when a password reset is requested, then the response is byte-for-byte identical to the registered case and no token is persisted.
- Given a registered, active email, when a password reset is requested, then a token is persisted hashed and an email carrying the raw value is dispatched.
- Given a valid reset token, when it is redeemed with a new password, then the new password logs in and the old one no longer does.
- Given a reset token that was already redeemed, when it is presented again, then the request is rejected and the password set by the first redemption still stands.
- Given an expired reset token, when it is presented, then the request is rejected and the password is unchanged.
- Given an outstanding reset token, when a second reset is requested, then the first token no longer works and the second one does.
- Given a session opened before a reset, when the reset completes, then that session's refresh token no longer works.
- Given a deactivated user, when a password reset is requested for their email, then the response is unchanged and no token is issued.

## 16. Future Improvements

- Email verification flow before first login.
- "Logout everywhere" as an explicit user action (revoking all refresh tokens already happens as a side effect of a password reset, BR-11).
- Account lockout / exponential backoff after repeated failed login attempts.
- Multi-factor authentication.
- Device/IP metadata on refresh tokens for session management UI.
- OAuth/social login providers.
