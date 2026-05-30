# Filer — Document Management Platform

API-first modular monolith (.NET 10, PostgreSQL). The authoritative design lives
in [`project documents/`](project%20documents/README.md); the architecture layout
is fixed by `10-solution-structure.md` and the ADRs in `09-decision-log.md`.

## Walking skeleton (current state)

The walking skeleton proves the host, authentication, and persistence wire
together before any document feature is added (10-solution-structure.md):

```
Filer.Api  ──▶  Filer.Modules.Auth  ──▶  Filer.Modules.Auth.Contracts  ──▶  Filer.SharedKernel
   (host)          (impl)                  (public surface)                  (primitives)
```

Implemented Auth slices (vertical slices — endpoint + service + DTOs + validation):

| Method | Route                    | Auth | Description                          |
|--------|--------------------------|------|--------------------------------------|
| POST   | `/api/v1/auth/register`  | No   | Create an account (email/password)   |
| POST   | `/api/v1/auth/login`     | No   | Obtain a JWT access token            |
| GET    | `/api/v1/auth/me`        | Yes  | Current user profile (from the token)|

Refresh-token rotation, `/auth/refresh`, and `/auth/logout` are the planned
follow-up (05-security.md); this skeleton issues the access token only.

## Prerequisites

- .NET 10 SDK
- Docker (for PostgreSQL)

## Run

### Option A — everything in Docker

```bash
docker compose up --build
```

Brings up `postgres` and the `api` (on http://localhost:8080). The API applies
the Auth migration on startup.

### Option B — Postgres in Docker, API from the IDE / CLI

```bash
docker compose up -d postgres
dotnet run --project src/Filer.Api
```

The API reads the `Postgres` connection string and the dev JWT signing key from
`appsettings.Development.json`. It applies pending migrations on startup.

### Smoke test

Use `src/Filer.Api/Filer.Api.http` (register → login → me), or:

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
```

## Database migrations

The Auth module owns its migrations under
`src/Modules/Auth/Filer.Modules.Auth/Persistence/Migrations`. The initial
`InitialAuth` migration (all ASP.NET Identity tables in the `auth` schema) is
already committed. To add more:

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/Auth/Filer.Modules.Auth \
  --startup-project src/Filer.Api \
  --output-dir Persistence/Migrations \
  --context AuthDbContext
```

(`dotnet tool install --global dotnet-ef` if the tool is missing.)

## Configuration & secrets

- `ConnectionStrings:Postgres` — database connection.
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes`, `Jwt:SigningKey`.

The signing key in `appsettings.Development.json` is **dev-only**. In real
environments supply `Jwt__SigningKey` (≥32 chars) via environment variable or a
secret store — never source control (05-security.md). Startup fails fast if it is
missing or too short.

## Solution-wide conventions

- `Directory.Build.props` — net10.0, nullable, warnings-as-errors.
- `Directory.Packages.props` — central NuGet versions (projects carry no version attributes).
- `.editorconfig` — file-scoped namespaces, formatting, style rules.

## Next steps (build order — 08-ai-development-guidelines.md)

1. **Auth — refresh-token lifecycle** (refresh/logout/rotation).
2. Upload pipeline (`Documents` module + `Storage.Contracts` abstraction).
3. File storage abstraction (`IFileStorageProvider`).
4. Folder / Tag management.
5. AI analysis pipeline (`IAIAnalysisProvider`, background jobs).
6. Search, then observability.
