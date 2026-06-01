# /docs/10-solution-structure.md

# Solution & Architecture Layout

## Purpose

Defines the physical shape of the codebase: the .NET solution, its projects, the
folder layout of a module, the dependency rules between projects, and how a
vertical slice is wired internally. Where `00` and ADR-003 decide the
architecture *style* (modular monolith + vertical slices), this document decides
the concrete *structure* that style takes in source.

Related documents: `00-project-context.md` (modules, principles),
`02-data-model.md` (entities), `03-api-specification.md` (API conventions),
`07-storage-and-deployment.md` (infrastructure abstractions). Related decisions:
ADR-003 (modular monolith + vertical slices), ADR-004 (project-per-module,
plain feature services, pragmatic API style).

---

## Decisions This Document Encodes

* **Project per module.** Each module is its own class library, paired with a
  thin public `*.Contracts` library. Boundaries are enforced by the compiler,
  not by convention alone.
* **Plain feature services.** A slice is an endpoint plus a feature service plus
  its DTOs. No mediator, no command bus.
* **Pragmatic API style.** Minimal APIs by default; controllers where a resource
  cluster justifies them. The choice is made per slice, not globally (see
  *API Style* below).

Full rationale lives in ADR-004 (`09-decision-log.md`).

---

## Repository Layout (Top Level)

```
/
├── Filer.sln
├── Directory.Build.props          # shared MSBuild settings (nullable, warnings)
├── Directory.Packages.props       # central NuGet package versions
├── docker-compose.yml             # api + postgres (+ worker) for local/dev
├── .editorconfig
├── project documents/             # this documentation set
├── src/
│   ├── Filer.Api/                 # ASP.NET Core host — composition root
│   ├── Filer.SharedKernel/        # cross-cutting primitives, no business logic
│   ├── Filer.WebKernel/           # shared web conventions (routes, error mapping)
│   ├── Modules/                   # one folder per module (see below)
│   └── Clients/                   # Blazor / MAUI front-ends (summarized below)
└── tests/
    ├── Filer.Architecture.Tests/  # boundary-rule enforcement
    ├── Filer.IntegrationTests/    # API + real Postgres (Testcontainers)
    └── Filer.Modules.*.Tests/     # per-module unit tests
```

The backend is the focus of this document. The front-end projects
(`Filer.Web` Blazor WebAssembly, `Filer.Ui` shared Razor Class Library,
`Filer.App` MAUI Blazor Hybrid) live under `src/Clients/` and consume the REST
API like any other client (ADR-001); their internal structure is out of scope
here.

---

## Modules

The modules are fixed by `00-project-context.md`. Each becomes a project pair
under `src/Modules/`:

| Module          | Implementation project             | Contracts project                       |
|-----------------|------------------------------------|-----------------------------------------|
| Authentication  | `Filer.Modules.Auth`               | `Filer.Modules.Auth.Contracts`          |
| Documents       | `Filer.Modules.Documents`          | `Filer.Modules.Documents.Contracts`     |
| Folders         | `Filer.Modules.Folders`            | `Filer.Modules.Folders.Contracts`       |
| Tags            | `Filer.Modules.Tags`               | `Filer.Modules.Tags.Contracts`          |
| AI Analysis     | `Filer.Modules.AiAnalysis`         | `Filer.Modules.AiAnalysis.Contracts`    |
| Storage         | `Filer.Modules.Storage`            | `Filer.Modules.Storage.Contracts`       |
| Search          | `Filer.Modules.Search`             | `Filer.Modules.Search.Contracts`        |
| Background Jobs  | `Filer.Modules.BackgroundJobs`     | `Filer.Modules.BackgroundJobs.Contracts`|

A **Contracts** project holds only the module's public surface — the interfaces,
DTOs, and integration events other modules are allowed to touch. An
**Implementation** project holds everything else (entities, persistence,
feature slices, endpoints, concrete infrastructure) and is private to the module.

Folders and Tags are listed as separate modules to match `00`. If they prove too
small to stand alone in practice they may be merged later via ADR; the layout
does not force the decision now.

---

## Project Responsibilities

### `Filer.Api` (host / composition root)

* Owns `Program.cs`, middleware pipeline, authentication wiring, error-handling,
  OpenAPI/Swagger, and CORS.
* References each module's implementation project **only** to invoke its
  registration entry point (`AddAuthModule`, `MapAuthEndpoints`, …). It never
  reaches into module internals beyond those entry points.
* Contains no business logic.

### `Filer.SharedKernel`

* Cross-cutting primitives shared by every module: the standard error/`Result`
  shape (`03`), the paged-envelope type (`03`), base entity conventions
  (`Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `OwnerId`, `TenantId` per `02`),
  a UTC clock abstraction, and the ownership/authorization marker interfaces.
* Depends on nothing else in the solution. No business rules, no persistence.

### `Filer.WebKernel`

* The web sibling of `SharedKernel`: cross-cutting ASP.NET Core conventions every
  module's endpoints rely on — the `Error` → problem-details mapping (`03`) and the
  versioned API route prefixes (`ApiRoutes.V1`, …). Each module composes its own base
  path from these (`ApiRoutes.V1 + "/auth"`) while keeping ownership of its segment.
* References `Filer.SharedKernel` only (for `Error`/`Result`) plus the ASP.NET Core
  shared framework. Never references a module or a `*.Contracts` project. Kept
  separate from `SharedKernel` precisely so web concerns stay out of the domain
  bottom layer (ADR-006).

### `Filer.Modules.* ` (implementation)

* The module's entities, EF Core `DbContext`, migrations, feature slices, and
  endpoints.
* Concrete infrastructure for the module lives here — e.g. the
  `LocalFileSystemStorageProvider` implementing `IFileStorageProvider` sits in
  `Filer.Modules.Storage` (`07`); concrete `IAIAnalysisProvider` adapters sit in
  `Filer.Modules.AiAnalysis` (`06`).

### `Filer.Modules.*.Contracts`

* Interfaces, DTOs, and integration events the module exposes.
* The infrastructure abstractions other modules consume live here:
  `IFileStorageProvider` in `Storage.Contracts`, `IAIAnalysisProvider` and the
  analysis-result events in `AiAnalysis.Contracts`, the queue abstraction in
  `BackgroundJobs.Contracts`.
* References `Filer.SharedKernel` only — never another module, never EF Core.

---

## Dependency Rules

These rules are the point of choosing project-per-module; they are enforced by
the compiler and by `Filer.Architecture.Tests`.

1. **No module references another module's implementation.** A module depends
   only on other modules' `*.Contracts` projects.
2. **Contracts are lean.** A `*.Contracts` project references `SharedKernel`
   only. It carries no EF Core, no infrastructure, no transitive module deps.
3. **The host depends inward.** `Filer.Api` references module implementations
   solely to register them; nothing references `Filer.Api`.
4. **SharedKernel depends on nothing.** It is the bottom of the graph.
5. **Infrastructure stays behind its abstraction.** Domain and feature code
   depends on `IFileStorageProvider` / `IAIAnalysisProvider` (from Contracts),
   never on a concrete provider (`05`, `07`, `08`).
6. **WebKernel is web-only and module-agnostic.** Module implementations may
   reference `Filer.WebKernel`; it references `SharedKernel` (plus the ASP.NET Core
   framework) only, and never a module or a `*.Contracts` project (ADR-006). A
   `*.Contracts` project must not reference it — contracts stay web-free.

Allowed direction, in short:

```
Filer.Api ──▶ Modules.*  (impl, for registration only)
Modules.* (impl) ──▶ own Contracts ──▶ other Modules' Contracts ──▶ SharedKernel
Modules.* (impl) ──▶ WebKernel ──▶ SharedKernel
Modules.*.Contracts ──▶ SharedKernel
```

### Cross-module communication

* **Synchronous:** call the other module's interface from its Contracts project
  (e.g. Documents calls `IFileStorage` from `Storage.Contracts`).
* **Asynchronous (in-process):** the upload→analysis hand-off must not run
  inline (`06`, `08`). The uploading slice persists metadata and enqueues a job
  through `IBackgroundJobQueue` (`BackgroundJobs.Contracts`); the worker resolves
  and runs it. Modules react to outcomes via integration events defined in the
  emitting module's Contracts, not by calling back into each other's internals.

---

## Persistence Per Module

* **One PostgreSQL database, one `DbContext` per module.** Each module owns its
  tables and maps them to a dedicated Postgres **schema** (`auth`, `documents`,
  `folders`, …). This keeps each module the sole owner of its data and preserves
  the service-extraction path (ADR-003) without a physical split in V1.
* **Migrations live with their module**, under
  `Filer.Modules.X/Persistence/Migrations`, and are applied per module at
  startup/deploy.
* Cross-schema foreign keys are avoided; references across module boundaries are
  by id and validated through the owning module's Contracts, so a module can be
  extracted later without untangling database-level constraints.
* Persistence concerns stay inside the module and do not leak into feature
  logic (`08`).

> Trade-off: a `DbContext` per module is slightly more setup than one shared
> context. It is accepted because shared-context coupling is exactly what the
> modular-monolith seams exist to prevent.

---

## Anatomy of a Module

Using Documents as the worked example:

```
Filer.Modules.Documents/
├── DocumentsModule.cs              # AddDocumentsModule(IServiceCollection)
│                                   # MapDocumentsEndpoints(IEndpointRouteBuilder)
├── Domain/
│   └── Document.cs                 # entity (per 02-data-model)
├── Persistence/
│   ├── DocumentsDbContext.cs
│   ├── Configurations/
│   │   └── DocumentConfiguration.cs
│   └── Migrations/
└── Features/                       # vertical slices — one folder per feature
    ├── UploadDocument/
    │   ├── UploadDocumentEndpoint.cs    # minimal API route or controller action
    │   ├── UploadDocumentService.cs     # the feature service (plain class)
    │   ├── UploadDocumentRequest.cs     # request DTO
    │   ├── UploadDocumentResponse.cs    # response DTO
    │   └── UploadDocumentValidator.cs   # explicit validation
    ├── DownloadDocument/
    ├── ListDocuments/
    └── DeleteDocument/
```

Each slice is self-contained: its endpoint, service, DTOs, and validation live
together. Slices share the module's entities and `DbContext` but not each
other's services — duplication across slices is preferred over premature shared
abstractions (ADR-003).

### A slice end to end (plain feature service)

```
HTTP request
  → Endpoint (binds + validates the request DTO)
    → FeatureService.HandleAsync(request, ct)   // the only business entry point
        → DbContext / IFileStorage / IBackgroundJobQueue …
    ← Response DTO
  ← typed HTTP result
```

The endpoint is a thin adapter: bind, validate, call the service, map the
result to an HTTP response. No mediator sits between them. The feature service
is an ordinary injected class, unit-testable without the web stack.

---

## API Style (Per-Slice Choice)

Both styles register through the module's `MapXEndpoints` entry point, so routing
stays co-located with the slice either way. Choose per slice by size and purpose:

**Default to Minimal APIs** for:

* simple slices (one or two endpoints),
* binary streaming (upload/download),
* lightweight handlers where controller ceremony adds nothing.

**Reach for a Controller** when a resource cluster earns it:

* many related endpoints sharing route prefix, filters, and model binding,
* complex model binding or content negotiation,
* cases where attribute-based grouping and action filters genuinely cut
  boilerplate.

The deciding question is whether controller conventions *remove* code for that
slice. If they do not, use a minimal API. The choice is local and reversible; it
never changes the module's public contract.

---

## Conventions

* **Namespaces mirror folders:** `Filer.Modules.Documents.Features.UploadDocument`.
* **Central package management** via `Directory.Packages.props`; versions are not
  pinned per project.
* **Nullable reference types enabled** solution-wide; warnings treated as errors
  in CI.
* **DTOs at boundaries** (`03`); entities are never returned from an endpoint.
* **Versioned routes** under `/api/v1` (`03`), assembled from each slice's
  endpoint registration.
* **Solution folders** in `Filer.sln` mirror `src/Modules/` so the IDE view
  matches the disk layout.

---

## Boundary Enforcement

`Filer.Architecture.Tests` (using an architecture-rules library such as
NetArchTest) asserts the dependency rules as executable tests, run in CI:

* no `Filer.Modules.X` references `Filer.Modules.Y` (only `*.Contracts`);
* no `*.Contracts` project references EF Core or another module;
* nothing references `Filer.Api`;
* feature code references storage/AI only through their abstractions.

A boundary that is only documented erodes; encoding it as a failing test keeps
project-per-module honest as the codebase grows.

---

## Build Order Mapping

The module layout lines up with the recommended build order in `08`
(Authentication → Upload pipeline → File storage abstraction → Folder/Tag →
AI analysis → Search → Observability). The walking skeleton is built first:
`Filer.Api` + `Filer.SharedKernel` + `Filer.Modules.Auth` + Postgres via
`docker-compose`, proving the host, auth, and persistence wire together before
any document feature is added.

---

## Open Items

* Whether Folders and Tags remain separate modules or merge into one
  organization module (revisit after the first slices are built).
* Whether in-process integration events need a lightweight dispatcher or can stay
  as direct interface calls in V1 (`06`).
* Exact architecture-test tooling and the CI gate that runs it (ties into the
  not-yet-written CI/CD doc).
