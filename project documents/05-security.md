# /docs/05-security.md

# Security Model

## Purpose

Defines the security requirements and mechanisms for the platform: how users are
authenticated, how access is authorized, how files are handled safely, and how
secrets are managed. Security is a core, first-class concern (`08`).

Related documents: `02-data-model.md`, `03-api-specification.md`,
`04-non-functional.md`. Related decisions: ADR-002, ADR-003, ADR-014.

---

## Threat Model (V1 Scope)

Primary concerns for V1:

* Unauthorized access to another user's documents or metadata.
* Malicious or malformed file uploads.
* Token theft / replay.
* Direct access to stored blobs bypassing the API.
* Injection (SQL, path traversal) via user-supplied input.

Out of scope for V1 (revisit for SaaS): advanced multi-tenant isolation guarantees,
DDoS mitigation, formal pen-testing/compliance certification.

---

## Authentication

### Mechanism

* ASP.NET Core Identity with email/password.
* JWT bearer access tokens for API calls.
* Passwords stored only as Identity-managed hashes; never logged or returned.

### Tokens

* **Access token:** short-lived JWT (target 15 minutes), signed with a
  server-held key. Carries the user id and minimal claims; no sensitive data.
* **Refresh token:** long-lived, opaque, stored server-side (hashed) and bound
  to the user. Used to obtain new access tokens via `/auth/refresh`.
* **Rotation:** each refresh issues a new refresh token and invalidates the prior
  one (rotation); reuse of a consumed refresh token revokes the token family
  (theft detection).
* **Logout / revocation:** `/auth/logout` revokes the active refresh token.
* JWT signing keys come from configuration/secret storage, never source control,
  and are rotatable.

### Client-side token storage (ADR-014)

Where a *client* keeps the token pair is host-specific, behind the `ITokenStore`
seam in `Filer.ApiClient`:

* **Web (Blazor WASM):** both tokens persist in browser **localStorage** so the
  session survives reload/restart. This is script-readable storage — an XSS
  compromise of the origin leaks the pair. Accepted for V1's personal-use threat
  model because the server design bounds the damage: the access token is
  short-lived and the refresh token is single-use with rotation + family
  revocation (theft detection). Client-side mitigations: Blazor's default output
  encoding, no third-party scripts in the app shell.
* On a 401 the client refreshes **once** and retries once (single-flight, via a
  bearer-free client so the refresh cannot recurse); a failed refresh clears the
  store, which signs the user out everywhere at once.
* **Future MAUI shell (RM-02):** platform secure storage, same seam.
* **SaaS-phase hardening path:** move the refresh token to an HttpOnly cookie
  (requires server cookie handling + CSRF defense) and/or keep access tokens
  in memory only — a host-level swap behind the seam (ADR-014).

### Future

Migration toward OpenID Connect / OAuth2 / external IdP is anticipated (`00`);
the auth boundary is kept behind an abstraction so the token-issuing mechanism
can change without touching domain logic.

---

## Authorization & Ownership

* Every protected endpoint requires a valid access token (JWT validation:
  signature, expiry, issuer, audience).
* **Ownership validation** is mandatory on every resource access: a resource is
  reachable only if its `OwnerId` matches the authenticated user.
* Cross-owner access returns **404, not 403**, so the API does not reveal that a
  resource exists (`03` convention).
* Authorization checks live at the slice/handler boundary; no handler trusts an
  id from the client without an ownership check.
* SaaS phase adds tenant-scoping (`TenantId`) enforced via row-level security
  and/or mandatory query filters, layered on top of ownership.

---

## File Upload Security

Uploads are the largest attack surface. Required controls:

* **Type allow-list** validated by declared MIME **and** content sniffing (magic
  bytes); mismatches rejected (415). List defined in `04`.
* **Size limit** enforced (413 over the configured maximum, `04`).
* **Filename sanitization:** the original name is stored as metadata only; the
  stored blob is referenced by an opaque `StorageKey` (`02`). User input never
  forms a filesystem path — prevents path traversal.
* **No execution:** uploaded files are treated as inert data; nothing in the
  pipeline executes or interprets them as code.
* **Antivirus/malware scanning:** a scanning hook is reserved in the upload
  pipeline (recommended before serving downloads in the SaaS phase); not
  mandatory for single-user V1 but the integration point is designed in.
* Content is hashed (SHA-256) for duplicate detection (`02`), not used as a
  security control.

---

## Secure File Access

* Stored blobs are **never** served via a public/static path. Download flows
  exclusively through the authenticated `/documents/{id}/content` endpoint, which
  enforces ownership before streaming.
* The storage location (local volume in V1) is not web-exposed.
* Storage keys are opaque and non-guessable; even a leaked key cannot be
  retrieved without authentication + ownership.
* Future signed-URL / pre-signed download support (for S3-compatible storage)
  must preserve the same ownership guarantees and use short expiries.

---

## Input Handling & Injection Defense

* All persistence goes through EF Core / parameterized queries — no string-built
  SQL.
* Request DTOs are explicitly validated (`03`); unexpected fields are ignored,
  required fields enforced.
* Path and identifier inputs are treated as opaque values, never concatenated
  into filesystem or query paths.

---

## Transport & Headers

* All traffic over HTTPS/TLS; HTTP redirected or refused.
* Security headers on API responses (HSTS, `X-Content-Type-Options: nosniff`,
  appropriate `Content-Disposition` on downloads to force download rather than
  inline render where relevant).
* CORS configured to the known client origins only (relevant once the Blazor
  WebAssembly client and future apps are defined).

---

## Secrets & Configuration

* Secrets (DB credentials, JWT signing keys, AI provider keys) supplied via
  environment/secret store, never committed (`04` portability rules).
* Distinct keys/credentials per environment; keys are rotatable without code
  changes.
* AI provider credentials are scoped to the worker, not exposed to clients.

---

## Logging & Privacy

* Never log secrets, tokens, passwords, or full file contents.
* Structured logs use correlation ids (`04`) but redact personal data where not
  needed.
* Error responses use the standard problem-details shape (`03`) and do not leak
  stack traces or internal details to clients.

---

## AI Processing Security

* Document content sent to an external `IAIAnalysisProvider` is a privacy
  consideration: the provider abstraction must allow a **local/no-external-egress
  provider** (e.g. Ollama/local LLM) so sensitive documents need not leave the
  deployment.
* Which provider is used is configurable; the default for sensitive/personal use
  should favor local processing.
* AI suggestions are advisory and applied only after user confirmation (`01`),
  so a compromised/incorrect AI result cannot silently alter organization.

---

## Open Questions

* Email verification and password-reset flows (deferred; needed before SaaS).
* Content-Security-Policy for the web client (tightens the XSS exposure that
  localStorage token storage accepts — ADR-014); revisit with production
  hosting of the static assets (`07`).
* Rate limiting / brute-force protection on `/auth/login` (recommended early).
* Whether antivirus scanning is enabled in V1 or deferred to SaaS.
* MFA support (SaaS-phase consideration).

---
