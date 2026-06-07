# Filer — Document Management Platform

API-first modular monolith (.NET 10, PostgreSQL). The authoritative design lives
in [`project documents/`](project%20documents/README.md); the architecture layout
is fixed by `10-solution-structure.md` and the ADRs in `09-decision-log.md`.

## Current state

Milestones M1 (foundation) and M2 (authentication) are complete; M3 (upload
pipeline) is in progress. Four modules are wired into the host, each exposing
only its `*.Contracts` project (enforced by the compiler and
`Filer.Architecture.Tests`):

```
Filer.Api (host)
 ├─▶ Filer.Modules.Auth            JWT auth, refresh-token rotation, ownership guard
 ├─▶ Filer.Modules.Documents       async upload, SHA-256 dedupe, content sniffing
 ├─▶ Filer.Modules.Storage         IFileStorageProvider (local filesystem in V1)
 └─▶ Filer.Modules.BackgroundJobs  IBackgroundJobQueue, DB-backed queue + worker

Cross-cutting: Filer.SharedKernel (Result/Error primitives)
               Filer.WebKernel    (route + error-mapping conventions — ADR-006)
```

Modules talk to each other through Contracts only (e.g. Documents depends on
`Storage.Contracts` and `BackgroundJobs.Contracts`). Job dispatch will move to
RabbitMQ with PostgreSQL as the durable outbox (ADR-008, not yet implemented).

## Endpoints

| Method | Route                    | Auth | Description                                |
|--------|--------------------------|------|--------------------------------------------|
| POST   | `/api/v1/auth/register`  | No   | Create an account (email/password)         |
| POST   | `/api/v1/auth/login`     | No   | Obtain an access + refresh token pair      |
| POST   | `/api/v1/auth/refresh`   | No   | Rotate: exchange refresh token for new pair|
| POST   | `/api/v1/auth/logout`    | Yes  | Revoke the refresh-token family → 204      |
| GET    | `/api/v1/auth/me`        | Yes  | Current user profile (from the token)      |
| POST   | `/api/v1/documents`      | Yes  | Upload a file (multipart `file` part)      |

Upload validates against an allow-list with mandatory content sniffing
(05-security.md), rejects oversize with a problem-details 413 (default max
50 MB — `Documents:MaxUploadBytes`), and returns **409 with
`existingDocumentId`** when identical content (SHA-256) already exists.

## Prerequisites

- .NET 10 SDK
- Docker (for PostgreSQL)

## Run

### Option A — everything in Docker

```bash
docker compose up --build
```

Brings up `postgres` and the `api` (on http://localhost:8080). The API applies
each module's pending migrations on startup.

### Option B — Postgres in Docker, API from the IDE / CLI

```bash
docker compose up -d postgres
dotnet run --project src/Filer.Api
```

The API reads the `Postgres` connection string, the dev JWT signing key, and the
dev storage root from `appsettings.Development.json`. It applies pending
migrations on startup.

### Smoke test

Use `src/Filer.Api/Filer.Api.http` (register → login → me / refresh / logout), or:

```bash
# register
curl -X POST http://localhost:8080/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"demo@filer.local","password":"Passw0rd!"}'

# login -> copy the accessToken from the response
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"demo@filer.local","password":"Passw0rd!"}'

# me
curl http://localhost:8080/api/v1/auth/me -H "Authorization: Bearer <accessToken>"

# upload
curl -X POST http://localhost:8080/api/v1/documents \
  -H "Authorization: Bearer <accessToken>" \
  -F "file=@./sample.pdf"
```

## Tests

```bash
dotnet test
```

Test projects live under `tests/` (xunit v3): per-module unit tests,
`Filer.SharedKernel.Tests`, `Filer.IntegrationTests` (Testcontainers — requires
Docker; covers auth flows, the job queue, and upload validation), and
`Filer.Architecture.Tests` (module dependency boundaries). Strategy and required
coverage: `12-testing-strategy.md`.

## Database migrations

Each module owns its migrations under its `Persistence/Migrations` folder, in
its own Postgres schema (`auth`, `documents`, `jobs`). All are applied at
startup. To add one (swap project/context for the module concerned):

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/Auth/Filer.Modules.Auth \
  --startup-project src/Filer.Api \
  --output-dir Persistence/Migrations \
  --context AuthDbContext
```

(`dotnet tool install --global dotnet-ef` if the tool is missing. Other
contexts: `DocumentsDbContext`, `JobsDbContext`.)

## Configuration & secrets

- `ConnectionStrings:Postgres` — database connection.
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes`, `Jwt:SigningKey`.
- `Storage:Provider` (V1: `Local`), `Storage:RootPath` — blob storage root.
- `Documents:MaxUploadBytes`, `Documents:AllowedContentTypes` — upload limits.

The signing key in `appsettings.Development.json` is **dev-only**. In real
environments supply `Jwt__SigningKey` (≥32 chars) via environment variable or a
secret store — never source control (05-security.md). Startup fails fast if it is
missing or too short. `Storage__RootPath` is likewise supplied via env / volume
mount outside Development (07-storage-and-deployment.md).

## Solution-wide conventions

- `Directory.Build.props` — net10.0, nullable, warnings-as-errors.
- `Directory.Packages.props` — central NuGet versions (projects carry no version attributes).
- `.editorconfig` — file-scoped namespaces, formatting, style rules.

## Next steps (build order — 08-ai-development-guidelines.md)

1. **Finish the upload pipeline (M3)** — document download / list / get /
   soft-delete, RabbitMQ job dispatch with Postgres outbox (ADR-008).
2. Folders & Tags (M4) — CRUD, cycle-safe moves, non-empty-folder deletion
   semantics (ADR-007).
3. AI analysis pipeline (M5) — `IAIAnalysisProvider`, worker job processing,
   suggestion review.
4. Search (M6) — full-text endpoint (tsvector / GIN).
5. Observability & CI (M7) — metrics, correlation ids, coverage + architecture
   gate.
