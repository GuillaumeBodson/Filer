# CLAUDE.md

Filer — document management platform. API-first modular monolith (.NET 10,
PostgreSQL, Blazor WebAssembly). This file is the entry point **and the single
behavioural rule layer for AI assistants**: it routes to the canonical docs,
gives the commands, and lists the non-negotiables with a pointer each. It does
not restate project facts.

## Canonical docs (the source of truth)

Every *what* lives in `project documents/`. On a conflict with this file, **the
canonical document wins** — fix the doc, do not fork the fact.

| For…                                                      | See |
|-----------------------------------------------------------|-----|
| Stack, scope, modules, principles, architecture           | `00-project-context.md` |
| Product vision and workflows                              | `01-product-vision.md` |
| Entities, relationships, persistence                      | `02-data-model.md` |
| API conventions, endpoints, error shape                   | `03-api-specification.md` |
| File limits, performance, observability                   | `04-non-functional.md` |
| Authentication, ownership, upload security, secrets       | `05-security.md` |
| AI job lifecycle, provider abstraction                    | `06-ai-analysis-pipeline.md` |
| Storage abstraction and deployment                        | `07-storage-and-deployment.md` |
| Decisions and their rationale (ADRs, index at the top)    | `09-decision-log.md` |
| Solution layout, projects, dependency rules               | `10-solution-structure.md` |
| Git workflow, branches, commits, PR, **CI**, releases     | `11-git-workflow.md` |
| Testing strategy, required tests per slice                | `12-testing-strategy.md` |
| Coding standards, OOP/SOLID, patterns, Definition of Done | `13-code-quality-and-design.md` |
| Post-V1 roadmap (deferred features)                       | `14-roadmap.md` |
| Frontend architecture, UI seams, tokens, bUnit tests      | `15-frontend-architecture.md` |

`08-ai-development-guidelines.md` is retired; its rules are the section below.
Work is tracked in GitHub milestones and issues (`gh issue list --milestone …`).
Open items are issues, never doc sections.

## Commands

```bash
# Postgres in Docker, API from the IDE/CLI
docker compose up -d postgres
dotnet run --project src/Filer.Api          # http://localhost:5232 (http profile), migrations at startup

# Everything in Docker (API on http://localhost:8080)
docker compose up --build

# Optional compose profiles: Aspire dashboard (telemetry UI :18888) and RabbitMQ
docker compose --profile observability up -d aspire-dashboard
docker compose --profile mq up -d rabbitmq   # + BackgroundJobs__Queue=RabbitMq

# Web client (Blazor WASM) — API base: src/Clients/Filer.Web/wwwroot/appsettings.json
dotnet run --project src/Clients/Filer.Web

# Regenerate the typed API client (Kiota): see src/Clients/Filer.ApiClient/README.md
# (committed OpenAPI snapshot + CI drift gate)

# Tests (test projects live under tests/ — see 10-solution-structure.md)
dotnet test

# Self-hosted single-node deployment: contract + runbook in deploy/README.md
# (image published to GHCR by CI, pinned tag on the node — ADR-018)

# New EF migration (the Auth module owns its migrations)
dotnet ef migrations add <Name> \
  --project src/Modules/Auth/Filer.Modules.Auth \
  --startup-project src/Filer.Api \
  --output-dir Persistence/Migrations \
  --context AuthDbContext
```

Smoke test: `src/Filer.Api/Filer.Api.http` (register → login → me).

## Non-negotiables

- **Resolved decisions stay resolved.** Blazor (ADR-001), PostgreSQL (ADR-002),
  modular monolith + vertical slices (ADR-003), project-per-module + plain feature
  services (ADR-004). Do not reopen them, and do not propose microservices, CQRS,
  DDD aggregates, a mediator/command bus or a generic repository over EF Core
  without a concrete, documented need (`13`, "deferred patterns").
- **Abstractions.** Storage only via `IFileStorageProvider` (`07`), AI only via
  `IAIAnalysisProvider` (`06`); auth, background jobs and persistence behind their
  seams. Never a concrete infrastructure implementation inside domain logic.
- **Upload is always asynchronous.** Persist metadata, enqueue a background job,
  return. Never synchronous AI analysis (`06`/`03`).
- **Security is not optional.** JWT + ownership check on every protected operation.
  Cross-owner access → **404, not 403** (`05`). Validate uploads: type allow-list,
  size, content sniffing (`04`/`05`).
- **Secrets** never in source control, never logged. `Jwt__SigningKey` (≥ 32 chars)
  from env or a secret store; the key in `appsettings.Development.json` is dev-only.
- **Code** (`13`): expected outcomes via `Result`/`Error`, never exceptions for
  business flow; `CancellationToken` propagated everywhere; structured logging via
  `[LoggerMessage]`; `sealed` by default, `record` DTOs, entities never cross the
  API boundary. Avoid unnecessary abstraction and speculative complexity — a
  pattern is applied only where it removes a present complexity; do not
  overengineer V1.
- **Tests** (`12`): a slice is not done without its tests — feature service
  (success + every `Error`) and the security-critical integration tests
  (ownership → 404, auth, upload validation). Full Definition of Done: `13`.
- **Docs and decisions.** A changed canonical fact is updated in its doc in the
  same PR; a non-trivial design or pattern decision gets an ADR in `09`
  (append-only; supersede, never delete).
- **Git** (`11`): branch per issue off `main`, Conventional Commits, `Closes #N`
  in the PR body, CI green, squash-merge, verify the issue closed.

## Structure conventions

- Project-per-module under `src/Modules/<Module>/`; a module exposes only its
  `*.Contracts` project. Dependencies enforced by the compiler **and** by
  `Filer.Architecture.Tests` (`10`).
- Vertical slices: endpoint + service + DTOs + validation per feature.
- `Directory.Build.props` (net10.0, nullable, **warnings-as-errors**),
  `Directory.Packages.props` (central NuGet versions), `.editorconfig`
  (file-scoped namespaces, style enforced at build).
