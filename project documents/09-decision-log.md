# /docs/09-decision-log.md

# Decision Log

This document records significant project decisions, their context, and rationale.
Each entry is an Architecture Decision Record (ADR). Decisions are append-only;
when a decision is superseded, mark it and add a new entry rather than deleting it.

---

## ADR-001 — Frontend technology: Blazor

* **Date:** 2026-05-30
* **Status:** Accepted

### Context

The platform is API-first. The first version ships as a web application only,
but Windows desktop and Android mobile clients may follow. The preference is to
stay within the .NET ecosystem (Blazor, MAUI), with Angular as a fallback option.

### Decision

Use **Blazor** as the frontend technology.

* **Web:** Blazor WebAssembly, consuming the REST API over HTTP.
* **Desktop (Windows) and mobile (Android):** MAUI Blazor Hybrid.
* **Shared UI:** UI components live in a Razor Class Library reused across the
  web app and the MAUI Hybrid shells.
* Blazor Server is **not** used as the foundation.

### Rationale

* **Maximum code reuse.** With an API-first backend, every client just consumes
  the same REST API. Blazor + MAUI Blazor Hybrid lets the same Razor components
  serve web, Windows, and Android, written essentially once.
* **Single ecosystem.** Keeps the entire stack in C#/.NET, matching the backend
  skill set. Angular would require a separate desktop/mobile strategy
  (Capacitor/Ionic or Electron) in TypeScript, with little reuse from the backend.
* **Target platforms are first-class.** Windows and Android are both supported
  MAUI targets.
* **No SEO requirement.** The application sits behind authentication, so Blazor
  WebAssembly's weaker SEO is irrelevant.
* **Blazor Server avoided** because its persistent SignalR connection does not
  suit the multi-client / Hybrid / offline-capable direction.

### Trade-offs accepted

* Larger initial download for Blazor WebAssembly (acceptable behind a login).
* Smaller component/library ecosystem than Angular.

### Alternatives considered

* **Angular** — more mature for large web SPAs and stronger public-SEO tooling,
  but leaves desktop/mobile unsolved and splits the stack across two ecosystems.
  Would only win with existing Angular expertise or a public-SEO requirement,
  neither of which applies.

---

## ADR-002 — Database engine: PostgreSQL

* **Date:** 2026-05-30
* **Status:** Accepted

### Context

The platform needs a relational store for metadata (users, documents, folders,
tags, ownership). The roadmap includes AI-assisted features — semantic search,
embeddings, and AI chat over documents — and a future evolution toward
multi-tenant SaaS. The preference was to experiment with PostgreSQL; this ADR
confirms it against the alternatives.

### Decision

Use **PostgreSQL** as the primary database engine.

* Relational metadata store accessed from .NET via Npgsql + the EF Core provider.
* JSONB columns for flexible/evolving metadata and AI analysis results.
* Built-in full-text search for V1 search.
* pgvector extension reserved for future semantic search / embeddings.

### Rationale

* **pgvector is the deciding factor.** Vector storage and similarity search are
  available as a Postgres extension, so semantic search and embeddings can be
  added later without introducing a separate vector database. No mainstream
  relational alternative matches this today.
* **JSONB** stores flexible metadata and AI results alongside relational data,
  without giving up joins and constraints.
* **Built-in full-text search** covers V1 before semantic search is needed.
* **First-class .NET support** via Npgsql and its EF Core provider.
* **No licensing cost**, which matters as the system scales toward SaaS.
* **Strong multi-tenancy primitives** (schemas, row-level security) for the SaaS future.
* **Docker-native** and available as a managed service everywhere (RDS, Azure,
  Supabase, Neon) when local hosting is outgrown.

### Trade-offs accepted

* Operational responsibility for tuning/maintenance (mitigated by managed
  offerings later).

### Alternatives considered

* **SQL Server** — excellent .NET integration, but licensing scales poorly for
  SaaS, heavier in Docker, and vector support is newer/less proven than pgvector.
* **MySQL / MariaDB** — weaker JSON and full-text handling, no mature pgvector
  equivalent.
* **MongoDB / document DB** — tempting given the project name, but the documents
  themselves are binary files on the filesystem; the database holds inherently
  relational data (users, folders, tags, ownership). Would sacrifice
  transactional integrity and joins for no real gain.

---

## ADR-003 — Architecture: modular monolith with vertical slices

* **Date:** 2026-05-30
* **Status:** Accepted

### Context

The architecture style was intentionally left undecided early on. The project is
built by a solo developer, ships a personal-use V1, and must evolve toward a
multi-tenant SaaS **without major rewrites**. It is Docker-first, isolates AI
work as background processing, and the guidelines explicitly warn against
overengineering and premature microservices.

The candidate styles answer two separate questions: deployment topology
(monolith / modular monolith / microservices) and internal code organization
(Clean/layered / vertical slice), with CQRS and DDD as optional overlays.

### Decision

* **Topology: modular monolith.** One deployable unit, split into well-bounded
  modules (Auth, Document Management, Folder/Tag Management, AI Analysis,
  Storage, Search, Background Jobs). Modules communicate through explicit
  interfaces, not by reaching into each other's internals, so any module can be
  extracted into its own service later if needed.
* **Internal organization: vertical slices.** Code is organized by feature
  (e.g. "Upload Document", "Suggest Tags") rather than by technical layer. Each
  slice owns its request/handler/validation.
* **Infrastructure behind interfaces.** Borrow the one key idea from Clean
  Architecture — keep infrastructure (storage, AI providers, persistence) behind
  abstractions such as `IFileStorageProvider` and `IAIAnalysisProvider`.
* **Defer CQRS and DDD** until a concrete need appears.

### Rationale

* **Single deployable unit** keeps V1 fast to build, test, and run in Docker.
* **Module seams** preserve the SaaS / service-extraction path without paying
  distributed-systems costs now.
* **Vertical slices** keep ceremony low and features easy to add for a solo dev.
* **Infrastructure abstraction** aligns with already-committed provider
  interfaces and the infrastructure-agnostic principle.
* **Defers complexity** (CQRS/DDD/microservices) in line with the project's own
  anti-overengineering guidelines.

### Trade-offs accepted

* Module boundaries require discipline to keep clean (no enforced process
  isolation as in microservices).
* Vertical slices can duplicate small bits of logic across slices; acceptable
  versus premature abstraction.

### Alternatives considered

* **Microservices** — rejected for V1; unjustified overhead for a single-user,
  solo-built application.
* **Modular monolith + full Clean Architecture layering** — more long-term
  structural purity, but more upfront ceremony than V1 warrants. Vertical slices
  preferred, with infrastructure abstraction retained as the one borrowed layer.

---

## ADR-004 — Solution layout: project-per-module, plain feature services, pragmatic API style

* **Date:** 2026-05-30
* **Status:** Accepted

### Context

ADR-003 fixed the architecture *style* (modular monolith with vertical slices)
but not the *structure* it takes in source. Three structural choices were open
and shape everything downstream: how strict the module boundaries are physically,
how a vertical slice is wired internally, and which API endpoint style the slices
use. The full structure is specified in `10-solution-structure.md`; this ADR
records the choices and why.

### Decision

* **Project per module.** Each module is its own class library paired with a thin
  public `*.Contracts` library. A module depends only on other modules'
  `*.Contracts`, never their implementation. Boundaries are compiler-enforced and
  asserted by an architecture-test project.
* **Plain feature services.** A vertical slice is an endpoint, a feature service
  (an ordinary injected class), and its DTOs/validation. No mediator or command
  bus is introduced.
* **Pragmatic API style.** Minimal APIs are the default; controllers are used
  only where a resource cluster's size or binding needs make controller
  conventions remove code. The choice is made per slice, not globally, and never
  affects a module's public contract.

### Rationale

* **Project-per-module** turns the module seams of ADR-003 into boundaries the
  compiler enforces, strengthening the future SaaS / service-extraction path
  without distributed-systems cost now. The `*.Contracts` split keeps the public
  surface of each module explicit and lean.
* **Plain feature services** keep ceremony low and code obvious, matching the
  project's "prefer readability, avoid unnecessary abstraction" guideline (`08`).
  MediatR's pipeline benefits are not yet needed and would add indirection the
  guidelines lean against.
* **Pragmatic API style** avoids a dogmatic global rule; it co-locates routing
  with the slice while letting genuinely large resource clusters use controllers
  where they cut boilerplate.

### Trade-offs accepted

* More projects to manage than a single-project, folder-based modular monolith;
  accepted for enforceable boundaries.
* A `DbContext` per module is more setup than one shared context; accepted to
  keep each module the sole owner of its data.
* Plain services can duplicate small cross-cutting bits (validation/logging
  wiring) that a mediator pipeline would centralize; accepted versus premature
  abstraction, and revisitable if cross-cutting needs grow.

### Alternatives considered

* **Folders in a single project** — least ceremony, but boundaries rely on
  discipline alone and are easy to erode; rejected in favor of compiler
  enforcement.
* **Hybrid (module class libraries, no per-module Contracts split)** — real
  assembly boundaries without separate contract projects; rejected because the
  Contracts split is what keeps each module's public surface explicit and
  prevents implementation leakage.
* **MediatR commands/queries** — useful pipeline behaviors for validation and
  logging, but adds a dependency and indirection without a concrete present need;
  deferred, consistent with deferring CQRS in ADR-003.

---
