# Spec: Passwordless Identity Provider (IDP)

Status: Draft v2 · Reverse-engineered from `btms.identity.core`, reworked per stakeholder decisions; WebAuthn switched to built-in .NET 10 Identity passkeys.
Target: .NET 10 (LTS), PostgreSQL, single tenant.

---

## 1. Goals and non-goals

### Goals
- A standards-compliant **OpenID Connect / OAuth 2.0 provider** that authenticates users with **WebAuthn passkeys**, with passwords retained as a fallback.
- The IDP **owns identity, roles, claims and permissions** and emits them in tokens.
- **Asymmetric signing (ES256)** with a published JWKS and key rotation. Relying parties never hold signing material.
- Stateless application nodes: all ceremony state lives in Postgres.
- Swagger/OpenAPI and a uniform exception-handling middleware.

### Non-goals
- Multi-tenancy (single organisation only).
- Attestation verification / FIDO MDS (attestation is always `none`).
- Federation to external IdPs, SAML.
- Any API gateway. The service is reachable directly; **no `Gateway` header check and no Ocelot references**.

### Explicitly removed from the current implementation
| Removed | Replacement |
|---|---|
| Serilog (+ Postgres sink, enrichers) | `Microsoft.Extensions.Logging` (console, JSON formatter in production). OpenTelemetry optional. |
| RabbitMQ (`RabbitMQ__Uri`, `depends_on`) | Nothing. No messaging in scope. |
| MinIO / `IObjectStorageService` | Nothing. |
| `ValidateRequestOriginMiddleware` (`Gateway: btms.api.gateway`) | Standard authn/authz + CORS. |
| Ocelot / gateway references | None (grep the solution, compose files and docs before merge). |
| Hand-rolled JWT issuing in `UserController` | OpenIddict token endpoint. |
| `Microsoft.AspNetCore.Identity.UI`, `Http.Features 5.0.17`, `Http.Abstractions 2.3.0`, `Newtonsoft.Json` | Framework-provided equivalents / System.Text.Json. |
| `MapIdentityApi` | Custom endpoints below (its bearer tokens conflict with OIDC). |

---

## 2. Reverse-engineered current behaviour (reference)

Endpoints in `UserController`:

| Endpoint | Behaviour today |
|---|---|
| `POST /user/login` | Email + password, 1h HS256 JWT (`sub`, `jti`, `accountid`, name id, email). |
| `GET /user/{username?}/credential-options` | Builds `CredentialCreateOptions`; auto-creates unknown users; supports usernameless. |
| `PUT /user/{username}/credential` | Verifies attestation, stores `StoredCredential`. Returns `"OK"` or the exception text. |
| `GET /user/{username?}/assertion-options` | Builds `AssertionOptions`; empty allow-list when usernameless. |
| `POST /user/assertion` | Verifies assertion, updates sign count, returns `"Bearer <jwt>"`. |

Data: `ApplicationUser : IdentityUser` (+`AccountId` Guid v7, `StoredCredentials`); `fido2.StoredCredential` (id, public key, sign count, transports, BE/BS, attestation object + client data, user handle, AAGUID, format, reg date).

### Known defects (do NOT carry forward)
1. `GetCredentialById` filters `UserId == credentialId` (wrong column).
2. `GetUsersByCredentialIdAsync` returns an unstarted `new Task<>` — awaiting it never completes.
3. Challenges kept in static `Dictionary`s (breaks with >1 replica; `Request.Host` used as removal key; `(char)byte` key encoding is lossy).
4. Assertion JWT signed with a hardcoded string, not the configured secret; lacks `sub`/`accountid`/`jti`; uses `DateTime.Now`; 1-day lifetime.
5. Unauthenticated registration allows adding a credential to any existing username (account takeover).
6. Users auto-created on option request.
7. `Fido2OptionsValidator` demands `ServerDomain` ≥ 32 chars and is never registered.
8. `ExpiryMinutes` ignored; startup exceptions swallowed in `Program.cs`; `Nullable` disabled.

---

## 3. Technology and dependency baseline

Versions verified against NuGet on 2026-09-30. Use exact stable versions; re-check at implementation time.

| Concern | Package | Version |
|---|---|---|
| Runtime | .NET SDK / `net10.0` | 10.0.x |
| WebAuthn | **Built-in ASP.NET Core Identity passkeys** (part of `Microsoft.AspNetCore.Identity.EntityFrameworkCore`; `SignInManager<T>` / `UserManager<T>` / `IdentityPasskeyOptions`) | 10.0.12. `Fido2` is **not** referenced (see §3.1) |
| OIDC server | `OpenIddict.AspNetCore` + `OpenIddict.EntityFrameworkCore` | 7.7.1 |
| Identity | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.12 |
| ORM | `Microsoft.EntityFrameworkCore` (+`.Design`) | 10.0.12 |
| Postgres | `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 |
| Token validation (for protected endpoints in this service) | `OpenIddict.Validation.AspNetCore` (preferred) or `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 | |
| API docs | `Swashbuckle.AspNetCore` 10.2.3 (alt: `Microsoft.AspNetCore.OpenApi` 10.0.12 + a UI) | |
| Resilience (only if outbound HTTP is needed) | `Polly` 8.8.0 | |

All Microsoft packages must match the framework major version (10.x). The current project targets `net10.0` while referencing 9.0.x packages — fix this.

### 3.1 WebAuthn implementation decision: built-in Identity passkeys (not Fido2)

Verified against Microsoft's passkey documentation (aspnetcore-10.0, updated 2026-09) on 2026-09-30. Decision: **use the built-in support; drop the `Fido2`/`Fido2.AspNet` packages.**

| Requirement | Built-in support | Verdict |
|---|---|---|
| Passkey registration and passkey-only sign-in | `SignInManager.MakePasskeyCreationOptionsAsync` / `PerformPasskeyAttestationAsync`, `UserManager.AddOrUpdatePasskeyAsync`, `MakePasskeyRequestOptionsAsync` / `PasskeySignInAsync` | Covered |
| Passkeys alongside passwords | Explicit scenarios: add passkey to existing account, passwordless account creation, passwordless sign-in | Covered |
| Discoverable / usernameless login | `MakePasskeyRequestOptionsAsync(null)` → conditional UI / usernameless; `IdentityPasskeyOptions.ResidentKeyRequirement` | Covered |
| User verification required | `IdentityPasskeyOptions.UserVerificationRequirement = "required"` | Covered |
| Attestation `none` | Default; attestation statements are not validated unless `VerifyAttestationStatement` is set | Covered |
| RP ID / origin control | `ServerDomain`, `ValidateOrigin` callback | Covered, with caveat (see below) |
| Sign-count / BE / BS storage, friendly name | `UserPasskeyInfo` (credential id, public key, signature counter, backup flags, `Name`) | Covered |
| Multiple passkeys per user, rename, delete | `UserManager` passkey APIs; template shows rename/delete | Covered (API names to be confirmed at implementation) |
| Extensibility | `IPasskeyHandler<TUser>` (wrap the default `PasskeyHandler<TUser>`), registered after Identity services | Covered |
| Storage | `AspNetUserPasskeys` (`CredentialId` byte[] PK ≤1024 B, `UserId` FK, `Data` JSON); requires `IdentitySchemaVersions.Version3` | Covered |
| Ceremony state in Postgres | **Not by default** — state is kept in an encrypted/signed authentication cookie (ASP.NET Data Protection) | **Gap — see §5 / §7** |
| Soft-revoke (`RevokedAt`), `LastUsedAt` columns | Not in the stock schema | **Gap — delete the row and write an `AuditEvent`; `LastUsedAt` is recorded in the audit trail unless the stored entity can be extended (confirm at implementation)** |
| Exact-match origin allow-list | Default validation also accepts subdomains of `ServerDomain` | **Gap — set `ValidateOrigin` to an exact allow-list** |
| Attestation verification / FIDO MDS | Not built in (`VerifyAttestationStatement` hook only) | Not needed (attestation = none) |
| Passkey as 2FA | Not supported (primary factor only) | Not needed |
| Local development on `localhost` | Reported not to work with default validation (Duende, Oct 2025) | **Gap — dev-only `ValidateOrigin`/`ServerDomain` override; re-test on 10.0.12** |

Consequences accepted:
- Requirement "Postgres-backed challenges" is met by (a) persisting ASP.NET Data Protection keys to Postgres (`PersistKeysToDbContext`) so **any replica can decrypt the ceremony cookie**, and (b) not keeping any ceremony state in process memory. If strict single-use enforcement of a challenge server-side becomes necessary (a cookie could in theory be replayed until it expires), wrap `PasskeyHandler<TUser>` in a custom `IPasskeyHandler<TUser>` that also records the challenge in a `idp.PasskeyCeremony` table and consumes it atomically — this is the documented extension point.
- Passkey options are JSON strings in the WebAuthn JSON format; the login/enrollment page must use `PublicKeyCredential.parseCreationOptionsFromJSON` / `parseRequestOptionsFromJSON` (or a polyfill). Test password-manager extensions (1Password was reported problematic).
- The RP ID should be set explicitly (`ServerDomain`); the default infers it from the Host header, which needs host-header validation (`AllowedHosts`, and `ForwardedHeadersOptions.AllowedHosts` if a proxy is used).
- The built-in API is intentionally narrow ("not a general-purpose WebAuthn library"). If a later requirement needs attestation trust stores, MDS, or non-Identity credential handling, revisit `Fido2` 4.1.x then.
- Passkey APIs are exposed only through `SignInManager`/`UserManager`; the stock templates only cover Blazor, so the `/auth/*` endpoints in §7 are custom minimal APIs/controllers around those methods. Use `AddIdentity`/`AddIdentityCore` + cookie scheme (not `MapIdentityApi`).

Project settings: `Nullable` **enabled**, `TreatWarningsAsErrors` true, `ImplicitUsings` enabled.

---

## 4. Architecture

```
Client (SPA / mobile / web app)
   │  OIDC Authorization Code + PKCE
   ▼
┌───────────────────────────────────────────────┐
│ IDP (ASP.NET Core, .NET 10)                   │
│  ├─ OpenIddict server (/connect/*, discovery) │
│  ├─ Auth API  (/auth/*: passkey, password,    │
│  │            OTP, recovery)                  │
│  ├─ Admin API (/admin/*: users, roles, claims,│
│  │            clients, resets)                │
│  ├─ Swagger UI + exception middleware         │
│  └─ Background jobs (cleanup, key rotation)   │
└───────────────┬───────────────────────────────┘
                ▼
            PostgreSQL
```

Suggested project layout (single deployable, layered by folder or project):
- `Idp.Api` — endpoints/controllers, middleware, composition root.
- `Idp.Domain` — entities, policies, no framework dependencies beyond Identity types.
- `Idp.Data` — `IdpDbContext`, migrations, stores.
- `Idp.Contracts` — request/response DTOs, options classes.

Drop the shared `btms.common.*` coupling from the IDP; it must not depend on unrelated domain code (QR codes, transit records, etc.).

---

## 5. Data model (schema `idp`; migrations history table `idp.__EFMigrationsHistory`)

- **AspNetUsers** (`ApplicationUser : IdentityUser<Guid>`): `Id` (Guid), `UserName`, `Email`, `EmailConfirmed`, `PasswordHash` (nullable), `LockoutEnd`, `AccessFailedCount`, `DisplayName`, `IsActive`, `CreatedAt`, `MustEnrollPasskey` (bool). Drop the `AccountId` concept (single tenant); if a stable external subject is needed use `Id`.
- **AspNetRoles / AspNetUserRoles / AspNetRoleClaims / AspNetUserClaims**: standard Identity, used as the source of roles and custom claims.
- **Permissions**: `Permission(Id, Name unique, Description)`, `RolePermission(RoleId, PermissionId)`. Permissions are emitted as `permission` claims (multi-valued) derived from the user's roles plus direct user claims.
- **AspNetUserPasskeys** (Identity schema **Version3**, `options.Stores.SchemaVersion = IdentitySchemaVersions.Version3`): `CredentialId` (bytea PK), `UserId` (FK → AspNetUsers, cascade), `Data` (JSON: public key, sign count, transports, BE/BS flags, name, etc., managed by Identity). Replaces `fido2.StoredCredential`. Revocation = row deletion + `AuditEvent`.
- **DataProtectionKeys**: ASP.NET Data Protection key ring persisted to Postgres (`PersistKeysToDbContext`), required so passkey ceremony cookies and auth cookies work across replicas.
- **idp.PasskeyCeremony** *(optional, only if strict server-side single-use is adopted per §3.1)*: `Id` (Guid), `Purpose` (`Registration` | `Assertion`), `UserId` (nullable), `StateHash`, `CreatedAt`, `ExpiresAt`, `ConsumedAt`; consumed atomically (`UPDATE … WHERE ConsumedAt IS NULL AND ExpiresAt > now()`). TTL 5 minutes.
- **idp.OtpCode**: `Id`, `UserId`, `Purpose` (`Enroll` | `Recovery` | `EmailVerify`), `CodeHash` (HMAC-SHA256 with server pepper — never store plain), `ExpiresAt` (10 min), `Attempts`, `ConsumedAt`, `CreatedAt`.
- **idp.AdminResetRequest**: `Id`, `TargetUserId`, `RequestedByAdminId`, `Action` (`RevokeAllPasskeys` | `IssueEnrollmentOtp` | `ResetPassword`), `CreatedAt`, `Reason`.
- **idp.AuditEvent**: append-only (`Id`, `At`, `ActorId`, `SubjectId`, `Type`, `Ip`, `UserAgent`, `Detail` jsonb). Replaces the Serilog Postgres log sink for security-relevant events.
- **OpenIddict tables** (`OpenIddictApplications`, `Authorizations`, `Scopes`, `Tokens`) via `UseOpenIddict()`.
- **idp.SigningKey**: managed by OpenIddict key store or a custom table holding key id, public JWK, encrypted private key, `NotBefore`, `RetireAt`.

---

## 6. OIDC provider

Library: OpenIddict (server + validation).

- **Flows:** Authorization Code + PKCE (required, S256) for all interactive clients; Refresh Token; Client Credentials for service clients. Implicit and ROPC **disabled**.
- **Endpoints:** `/.well-known/openid-configuration`, `/.well-known/jwks` (as `/connect/jwks`), `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/logout`, `/connect/introspect`, `/connect/revocation`.
- **Scopes:** `openid`, `profile`, `email`, `roles`, `permissions`, `offline_access`, plus API-specific scopes registered by admin.
- **Claims in tokens:** `sub` (user Guid), `name`, `email`, `email_verified`, `role` (multi), `permission` (multi), `amr` (`pwd`, `hwk`/`swk`, `otp`, `mfa` as applicable), `auth_time`, `sid`. Claims are destination-scoped (only `openid`/`profile` claims go to the id_token; roles/permissions to access token, and to userinfo when scopes are granted).
- **Access tokens:** JWT, ES256, 10 min lifetime. Id token 10 min. Refresh token 14 days, rotating with reuse detection, revoked on passkey revoke / password reset / user deactivation.
- **Signing/encryption keys:** ES256 signing keys generated at first start, stored encrypted (Data Protection keys persisted to Postgres via `PersistKeysToDbContext`). Rotation every 90 days with 30-day overlap, old public keys remain in JWKS until all tokens signed by them expire. Development may use ephemeral keys; production must not. No shared symmetric secret anywhere (`Jwt:Secret` is removed).
- **Login UI:** the authorize endpoint redirects unauthenticated users to a minimal login page served by the IDP (`/account/login`) that offers passkey, password, and OTP entry, then resumes the authorization request. Static HTML + JS calling the Auth API in §7 is sufficient. The pages live in `wwwroot/account/*.html` and are served at the extensionless URLs `/account/login` and `/account/done` (a small middleware rewrites GET requests to the `.html` file before static files run), because `LoginPath` and the post-login redirect use those URLs. Passkey ceremonies must run on the IDP origin (RP ID = IDP domain).
- **Client registry:** managed through the Admin API; supports redirect URI / post-logout URI allow-lists (exact match), per-client scopes, confidential vs public clients.

---

## 7. Authentication API (`/auth`)

All request/response bodies are JSON (System.Text.Json), errors use RFC 9457 `application/problem+json`. Successful login in the browser establishes an IDP cookie session (`HttpOnly`, `Secure`, `SameSite=Lax`) consumed by `/connect/authorize`; it does not return a bearer token itself.

### 7.1 Passkey registration
Registration is **only** allowed for an authenticated session or a valid enrollment OTP. Users are never auto-created by these endpoints.

Implemented with `SignInManager<ApplicationUser>` / `UserManager<ApplicationUser>` (see §3.1).

1. `POST /auth/passkeys/registration/options` — authenticated (session or enrollment session), no body (the nickname is sent on `complete`, since the ceremony state cookie does not carry it). Calls `MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = user.Id, Name = user.UserName, DisplayName = user.DisplayName })`. Effective options come from `IdentityPasskeyOptions`: attestation `none`, `ResidentKeyRequirement = "required"` (enables usernameless login), `UserVerificationRequirement = "required"`, existing credentials excluded. Ceremony state is stored by Identity in a Data-Protection-protected, short-lived cookie. Returns the WebAuthn JSON options.
2. `POST /auth/passkeys/registration/complete` — body: `{ credential, nickname? }` (browser credential JSON). Calls `PerformPasskeyAttestationAsync`; **verify `attestationResult.UserEntity.Id` equals the signed-in / enrollment user's id** before storing; then `AddOrUpdatePasskeyAsync(user, attestationResult.Passkey)`; set `Name` from the nickname; audit-log; return `201` with a credential summary. The state cookie is deleted on use, and an enrollment session is signed out after one successful registration. Errors return problem+json (never raw exception text).

### 7.2 Passkey login
1. `POST /auth/passkeys/assertion/options` — body `{ username? }`. Calls `MakePasskeyRequestOptionsAsync(user | null)`. With a username: allow-list of that user's credentials; for unknown users call it with `null` (usernameless options) so the response shape is indistinguishable and does not reveal existence. Without a username: discoverable/usernameless. User verification required. State again lives in the protected ceremony cookie.
2. `POST /auth/passkeys/assertion/complete` — body: browser credential JSON. Calls `PasskeySignInAsync`; on success Identity issues the cookie session. Then enforce IDP policy before continuing the OIDC request: reject inactive/locked users; add `amr` (`hwk`/`swk`) to the session; write an `AuditEvent` (`LastUsedAt`). Sign-count regression handling is done by the built-in verifier as documented (counter is stored in `UserPasskeyInfo`); confirm behaviour on regression in an integration test and, if it only warns, add a wrapper `IPasskeyHandler` that rejects and audits (`Suspected clone`).

### 7.3 Password (retained)
- `POST /auth/password/login` `{ username|email, password }` — ASP.NET Identity `PasswordSignInAsync` with lockout enabled (5 failures / 15 min). Generic error message. `amr=pwd`.
- `POST /auth/password/change` (authenticated) and `POST /auth/password/set` (authenticated, when none exists).
- Password policy: min length 12, no composition rules, breached-password check optional (offline list). Passwords may be disabled per user (`PasswordHash = null`) or globally via config `Auth:AllowPasswordLogin`.
- Users with `MustEnrollPasskey` are directed to enrollment after password login.

### 7.4 OTP (enrollment and recovery)
- Delivery of OTP codes uses an `IOtpSender` abstraction. Default implementation: SMTP email. No queue/broker; sending is in-process with a retry (Polly) and failures are audit-logged.
- `POST /auth/otp/request` `{ email, purpose }` — always returns `202` regardless of whether the account exists; rate limited (per IP and per account). Code: 8 digits, CSPRNG, 10-min expiry, max 5 attempts, stored HMAC'd.
- `POST /auth/otp/verify` `{ email, code }` — on success returns a short-lived (10 min), single-purpose **enrollment session** that authorizes exactly one passkey registration (and optionally a password set) and nothing else. Consumes the OTP.
- Purposes: `Enroll` (new user invited by admin), `Recovery` (user lost all devices and self-serves), `EmailVerify`.
- Recovery via OTP does **not** revoke existing passkeys unless the user chooses "remove old devices" during enrollment; any recovery event emails the account owner.

### 7.5 Session management
- `GET /auth/me` — profile, methods available, list of passkeys (id, nickname, created, last used, backup status).
- `PATCH /auth/passkeys/{credentialId}` — rename (`UserPasskeyInfo.Name` via `AddOrUpdatePasskeyAsync`). `DELETE /auth/passkeys/{credentialId}` — revoke by removing the passkey (refuses to remove the last sign-in method unless a password or recovery path exists).
- `POST /auth/logout` — clears cookie; RP-initiated logout via `/connect/logout`.

---

## 8. Admin API (`/admin`, requires permission claims)

Protected by OpenIddict validation; caller needs the named permission. A bootstrap admin is created on first start from configuration (`Bootstrap:AdminEmail`) and receives an enrollment OTP (never a default password).

| Area | Endpoints | Permission |
|---|---|---|
| Users | `GET/POST /admin/users`, `GET/PATCH/DELETE /admin/users/{id}`, deactivate/reactivate | `users.read`, `users.write` |
| Invite | `POST /admin/users/{id}/enrollment-otp` — issues Enroll OTP | `users.write` |
| **Admin reset** | `POST /admin/users/{id}/reset` `{ action: RevokeAllPasskeys \| IssueEnrollmentOtp \| ResetPassword, reason }` — revokes tokens/sessions, records `AdminResetRequest`, emails the user; reason mandatory | `users.reset` |
| Roles | CRUD `/admin/roles`, assign/unassign users | `roles.read`, `roles.write` |
| Permissions | CRUD `/admin/permissions`, map to roles | `permissions.write` |
| Claims | CRUD user claims `/admin/users/{id}/claims` | `users.write` |
| Clients / scopes | CRUD OpenIddict applications and scopes | `clients.write` |
| Audit | `GET /admin/audit?…` (filter, paged) | `audit.read` |

Rules: an admin cannot reset or deactivate the last remaining admin; every admin write emits an `AuditEvent`; admin reset of a passkey does not delete rows, it sets `RevokedAt`.

---

## 9. Cross-cutting requirements

### 9.1 Swagger / OpenAPI (required)
- Swashbuckle 10.x: `AddEndpointsApiExplorer` + `AddSwaggerGen`; UI at `/swagger`, document at `/swagger/v1/swagger.json`.
- Document all endpoints in §7 and §8 with XML comments, response types (`ProducesResponseType`, including `ProblemDetails`), and an OAuth2 Authorization Code (PKCE) security scheme pointing at this IDP so admin endpoints can be tried from the UI.
- Enabled in Development and Staging by default; in Production only when `Swagger:Enabled=true` (and it should sit behind admin auth or network restrictions). OpenIddict `/connect/*` endpoints are described via a small document filter since they are not controllers.

### 9.2 Exception-handling middleware (required)
- A single global middleware (`ApiExceptionMiddleware`, registered first after `UseForwardedHeaders`; may be implemented as `IExceptionHandler` + `UseExceptionHandler`) that:
  - maps known domain exceptions to status codes (validation → 400, not found → 404, conflict → 409, forbidden → 403, ceremony expired/invalid → 400/401) and everything else to 500;
  - returns RFC 9457 `application/problem+json` with `type`, `title`, `status`, `traceId`, and an `errorId` (GUID) — never stack traces or inner exception messages in Production (the current code leaks `e.Message + InnerException.Message`);
  - logs via `ILogger` with `errorId`, `traceId`, route and user id (no secrets, no request bodies, no OTP/password/assertion data);
  - does not swallow exceptions during host start-up (remove the `try/catch` in `Program.cs`; let the process fail).
- Remove the current behaviour of answering `"Service is running..."` on `/`; expose `/health/live` and `/health/ready` (Npgsql check) instead.

### 9.3 Security
- HTTPS + HSTS in production; behind a reverse proxy use `UseForwardedHeaders` with a known-proxies list. No gateway header trust.
- CORS: explicit per-client origin allow-list derived from registered clients.
- Rate limiting (`AddRateLimiter`) on `/auth/*` and `/connect/token`.
- WebAuthn config (`Passkeys` section bound to `IdentityPasskeyOptions`, validated on start): `ServerDomain` (explicit RP ID, never inferred from Host), `AuthenticatorTimeout` (default 5 min; browser hint), `ChallengeSize` (default 32 bytes, keep ≥32), `UserVerificationRequirement = "required"`, `ResidentKeyRequirement = "required"`, plus an `Origins` allow-list enforced through `ValidateOrigin` (exact match; overrides the default subdomain acceptance). Dev-only override to allow `https://localhost:<port>`. Old `Fido2Options` and its ≥32-char `ServerDomain` rule are deleted; validate the RP ID is a valid domain and register with `.ValidateOnStart()`.
- Data Protection: keys persisted to Postgres and application name fixed so all replicas share the key ring; the passkey ceremony cookie is `Secure`, `HttpOnly`, short-lived.
- Anti-enumeration: identical responses/timing for unknown vs known users on options and OTP request endpoints.
- Cookies: `__Host-` prefix where possible; antiforgery on cookie-authenticated POSTs.
- Secrets (OTP pepper, DB connection string, SMTP creds) come from environment/secret store, never from `appsettings.json` or source.

### 9.4 Observability
- Structured logging via built-in providers only. Correlation through `Activity`/`traceId`. Security events go to `idp.AuditEvent`, not to an external log sink.
- Metrics/traces via OpenTelemetry are optional and must not add a hard dependency.

### 9.5 Background jobs (`BackgroundService`, no broker)
- Purge expired `PasskeyCeremony` (if used), `OtpCode`, and OpenIddict tokens/authorizations (OpenIddict `Quartz` integration is optional; a hosted service is sufficient).
- Signing-key rotation check (daily).
- Multi-replica safety: use Postgres advisory locks so jobs run once.

### 9.6 Project documentation (required)
- **Format:** the operational guide is a Jupyter notebook (`README.ipynb`), preferred over a `README.md`. Runnable shell steps are code cells that begin with `%%bash`; explanation lives in markdown cells. If a `README.md` is kept, it is a short pointer to the notebook and must not duplicate its steps.
- **Summary first:** the first cell is a short summary: what the service is, the stack, and what the notebook lets the reader do.
- **Numbered steps:** every step is numbered in order (`Step 1`, `Step 2`, ...), whether it is a runnable cell or a manual action. Numbering is continuous across sections, including teardown.
- **Teardown:** the notebook ends with numbered teardown steps: stop the API process, stop and remove containers (`docker compose down`), an explicitly marked destructive step for removing volumes (`docker compose down -v`), and a check that nothing is left running (`docker compose ps`).
- **Run-safe cells:** a step that never exits (for example `dotnet run`) is labelled as long-running and says how to stop it. Each `%%bash` cell runs in its own shell, so state such as `cd` must not be assumed to carry over between cells.

---

## 10. Configuration

```jsonc
{
  "ConnectionStrings": { "IdpDb": "" },          // env: ConnectionStrings__IdpDb
  "Idp": {
    "Issuer": "https://idp.example.com/",
    "AccessTokenMinutes": 10,
    "RefreshTokenDays": 14
  },
  "Passkeys": {                                   // bound to IdentityPasskeyOptions
    "ServerDomain": "idp.example.com",
    "AuthenticatorTimeoutMinutes": 5,
    "ChallengeSize": 32,
    "UserVerificationRequirement": "required",
    "ResidentKeyRequirement": "required",
    "Origins": [ "https://idp.example.com" ]      // enforced via ValidateOrigin
  },
  "Auth": { "AllowPasswordLogin": true, "Lockout": { "MaxFailures": 5, "Minutes": 15 } },
  "Otp": { "Pepper": "", "Length": 8, "TtlMinutes": 10, "MaxAttempts": 5 },
  "Smtp": { "Host": "", "Port": 587, "From": "" },
  "Bootstrap": { "AdminEmail": "" },
  "Swagger": { "Enabled": true }
}
```

Removed keys: `Jwt:Secret`, `Jwt:Audience` (audiences become OpenIddict resources/scopes), `RabbitMQ__*`, `ConnectionStrings__MonitoringDbContext`, any gateway settings.

`docker-compose` for the IDP: only `postgres` and the migration job as dependencies; drop `rabbitmq` from `depends_on`; keep port 5006 or as chosen; run `dotnet ef database update` (or an in-app migration on startup guarded by an advisory lock) before the app starts.

---

## 11. Migration from the current service

1. New schema/tables per §5; existing `AspNetUsers` rows carried over with Guid ids (existing string ids must be mapped). `AccountId` dropped.
2. Existing `fido2.StoredCredential` rows **do not migrate automatically** to `AspNetUserPasskeys` (different shape, and the old user handle was the UTF-8 of the username/string id). Default approach: users re-enroll through an admin-issued enrollment OTP. Optional: a one-off import that maps credential id, public key, sign count, transports and BE/BS into `UserPasskeyInfo`, valid only if the RP ID is unchanged and the user handle can be reproduced by the new `PasskeyUserEntity.Id`; verify with a real authenticator before relying on it.
3. Existing HS256 tokens are not accepted after cut-over; clients switch to the OIDC flow.
4. Remove Serilog, RabbitMQ and gateway config from compose and project files in the same change.

---

## 12. Acceptance criteria

- [ ] A user can be invited (admin OTP), verify OTP, register a passkey, and log in with it via the OIDC Authorization Code + PKCE flow; the id/access tokens validate against the JWKS with ES256.
- [ ] Usernameless (discoverable-credential) login works.
- [ ] Password login still works, is lockout-protected, and can be disabled by configuration.
- [ ] Lost-device recovery works through OTP; admin reset revokes passkeys/sessions/refresh tokens.
- [ ] Roles, claims and permissions created via the Admin API appear in tokens according to scopes.
- [ ] With two API replicas, a ceremony started on one can be completed on the other.
- [ ] With two IDP replicas sharing the Data Protection key ring, a passkey ceremony started on one completes on the other; a ceremony cookie is rejected after expiry or after use (server-side single-use if §3.1 wrapper is adopted); a cloned-counter assertion is rejected or audited per §7.2.
- [ ] Registering a passkey with tampered ceremony state (different user id) is rejected; the user-id check in §7.1 is covered by a test.
- [ ] Passkey login works on the local dev origin via the dev-only origin override and on production with exact origins only.
- [ ] Unknown vs known usernames are indistinguishable on options/OTP endpoints.
- [ ] `/swagger` documents every endpoint, including problem+json responses and the OAuth2 scheme.
- [ ] Unhandled exceptions return problem+json with an `errorId` and no internals in Production.
- [ ] Solution builds with `TreatWarningsAsErrors`, nullable enabled; no references to Serilog, RabbitMQ, MinIO, Ocelot, or `Gateway` header logic (verified by grep in CI).
- [ ] `README.ipynb` opens with a summary, numbers every step, uses `%%bash` cells for runnable steps, and ends with teardown steps (§9.6).
- [ ] Integration tests cover: registration, assertion, sign-count regression, OTP expiry/attempt limit, admin reset, key rotation with overlapping JWKS.

---

## 13. Open items (non-blocking)
- SMTP vs another OTP channel (SMS) — spec assumes email via `IOtpSender`.
- ~~Whether the login UI is served by the IDP or a separate SPA.~~ Resolved: served by the IDP (§6, `/account/login`).
- Exact `UserManager` passkey method names/overloads and whether `AspNetUserPasskeys.Data` can carry `LastUsedAt`/extra fields: confirm against the 10.0.12 API at implementation.
- Whether server-side single-use enforcement of ceremonies (custom `IPasskeyHandler`) is worth the extra table (§3.1).
- Browser support matrix for `parseCreationOptionsFromJSON` / `parseRequestOptionsFromJSON` and whether to ship a polyfill.
- Retention period for `AuditEvent` (suggest 13 months).