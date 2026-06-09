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

## ADR-005 — Logging: trace-context correlation and PII-safe auth events

* **Date:** 2026-05-31
* **Status:** Accepted

### Context

`04-non-functional.md` and `13-code-quality-and-design.md` already mandate
structured JSON logging, the `[LoggerMessage]` source-generator convention, and
correlation ids tying a request to the work it spawns. Two implementation choices
were left open when the API host was wired and the first slices (Auth) were
instrumented:

1. **What supplies the correlation id.** ASP.NET Core already establishes a W3C
   trace context (`Activity` → `TraceId`/`SpanId`) per request, but a bespoke
   `X-Correlation-ID` is a common alternative. A custom middleware for the latter
   was prototyped in this session, then removed.
2. **How to log failed authentication.** `05-security.md` requires that secrets and
   personal data stay out of logs and that personal data is redacted where not
   needed. Failed logins are a security signal worth capturing, which puts the
   audit value in tension with the redaction rule (the attempted email is PII).

### Decision

* **Correlate via the framework's trace context, not a custom id.** Enable
  `ActivityTrackingOptions` (`TraceId | SpanId | ParentId`) and the JSON console's
  `IncludeScopes`, so every `Information`-and-above line carries the trace id as a
  structured property. No bespoke correlation id is introduced. The client-facing
  reference is the `traceId` that `AddProblemDetails()` already writes into error
  responses.
* **Log auth events at the `13` levels, identifying by user id only.** Sign-in and
  account creation log at `Information` with the user's GUID. A failed login on an
  **existing** account logs at `Warning` with that user's GUID; a failed login on an
  **unknown** email logs at `Warning` with **no** identifier. Unexpected
  registration rejections log at `Warning` with Identity's error **codes** only.
  Email, password, and Identity error **descriptions** are never logged.

### Rationale

* **Trace context propagates for free.** W3C `traceparent` flows across HTTP and,
  later, messaging boundaries, giving the request→analysis-job correlation `04`
  requires without any hand-written plumbing into the future worker tier. A custom
  id would duplicate the trace id and have to be manually forwarded across every hop.
* **Zero new dependency, client reference already covered.** The built-in JSON
  console plus `AddProblemDetails()`'s `traceId` cover both the structured-log and
  the quotable-by-a-client needs without extra code.
* **Auth logs are a real security signal.** Logging the user id on a known-account
  failure makes repeated attempts against a real account visible to brute-force
  monitoring, while withholding any identifier for unknown emails keeps the raw
  email — PII under `05` — out of the logs. Identity error codes (not descriptions)
  avoid echoing submitted input.

### Trade-offs accepted

* The trace id is an infrastructure identifier, not a memorable business reference;
  acceptable since it is surfaced via problem-details rather than presented as a
  product feature.
* Failed-login logs cannot attribute the unknown-email case to a target account;
  accepted as the privacy-preserving default. Brute-force protection (rate limiting,
  `05`) is a separate control to be designed later, and can revisit this if it needs
  a richer signal.
* A `Warning` per wrong-password attempt adds some log volume; accepted for the
  security visibility.

### Alternatives considered

* **Bespoke `X-Correlation-ID` middleware** — gives a single id across hops and a
  client-facing value, but duplicates the framework trace id, must be propagated to
  the worker tier by hand, and adds middleware to maintain. Prototyped and removed
  this session as redundant.
* **Failed login — log nothing** — fully privacy-safe but discards the security
  signal; rejected.
* **Failed login — log the raw email** — best forensics, but stores PII against
  `05`'s redaction default; rejected.

---

## ADR-006 — Shared web kernel for cross-cutting route and error-mapping conventions

* **Date:** 2026-06-01
* **Status:** Accepted

### Context

Two web-layer conventions are written identically by every module that exposes
endpoints: the `Error` → RFC 7807 problem-details mapping (`03`/`05`) and the
versioned API route prefix `/api/v1` (`03`). With only the Auth module built, both
lived inside it — `ErrorResults` carried an explicit note that it could be
"promoted to a shared web kernel via ADR" once a second consumer appeared, and the
route prefix was a literal repeated between the endpoint group and the synthesized
`Location` header.

A second module (Documents, per the `08` build order) is a certainty, not a
hypothesis, so the second consumer is guaranteed. The open question was where these
conventions belong without violating the dependency rules (`10`): `SharedKernel` is
the domain bottom layer and must stay free of web concerns, and `*.Contracts`
projects must carry no infrastructure. A discussion also resolved that sharing the
version prefix does **not** force lockstep versioning — a registry can expose `V1`,
`V2`, … and each module picks its version independently.

### Decision

Introduce **`Filer.WebKernel`**, a shared web-layer library — the web sibling of
`SharedKernel`.

* It holds `ApiRoutes` (versioned prefixes: `V1 = "/api/v1"`, with later versions
  added additively) and `ErrorResults` (the `Error` → problem-details mapping),
  moved out of the Auth module.
* It references `Filer.SharedKernel` (for `Error`/`Result`) and the ASP.NET Core
  shared framework only; it never references a module or a `*.Contracts` project.
* Module implementations reference it. A module **composes** its own base path from
  the shared version — `AuthRoutes.BasePath = ApiRoutes.V1 + "/auth"` — keeping the
  version atom central and the per-module segment module-owned.
* Per-feature suffixes (`/register`, `/login`, `/me`) appear once each and stay
  co-located with their slice (ADR-003).
* This adds a sixth dependency rule in `10` for `Filer.Architecture.Tests` to
  enforce.

### Rationale

* **Dedupes a fact, not a slice.** The error mapping and the version prefix are
  single facts every module must agree on and that change together — exactly what
  belongs in a shared component, unlike vertical-slice logic where ADR-003 prefers
  duplication.
* **Keeps the domain layer web-free.** Putting web conventions in `SharedKernel`
  would expand its charter; a dedicated web kernel preserves the clean bottom of
  the graph and the "contracts carry no infrastructure" rule.
* **No lockstep versioning.** Exposing versions as additive constants lets modules
  adopt `V2` independently, so centralising the prefix costs nothing in flexibility.
* **Tests still guard the contract.** `ApiRoutes`/`ErrorResults` are referenced by
  production code only; the integration tests restate routes and problem shapes
  independently, so a route or status change still surfaces as a failing test
  rather than recompiling silently.

### Trade-offs accepted

* A new shared project the moment the second consumer is certain, rather than after
  it physically lands — accepted because the duplication and its drift risk
  (group prefix vs. `Location`) already exist today, and the move is small and
  reversible.
* One more boundary for `Filer.Architecture.Tests` to assert. Accepted: an
  unenforced boundary erodes (`10`).

### Alternatives considered

* **Leave both conventions in the Auth module and copy into each new module** —
  honours "duplication beats premature sharing," but these are shared facts, not
  slice logic; copying invites the mapping and the version prefix to drift across
  modules. Rejected.
* **Put the constants/mapping in `SharedKernel`** — no new project, but pushes
  ASP.NET Core (`IResult`, route conventions) into the dependency-free domain layer
  and through to every `*.Contracts` consumer. Rejected as a charter violation.
* **A global single `/api/v1` constant with no version registry** — simplest, but
  encodes an implicit all-modules-version-together assumption. Rejected in favour of
  an additive `ApiRoutes` that keeps versions independent.

---

## ADR-007 — Non-empty folder deletion: reject by default, explicit opt-in cascade

* **Date:** 2026-06-05
* **Status:** Accepted (ratified 2026-06-07 with the delete slice, #44)

### Context

`DELETE /api/v1/folders/{id}` soft-deletes a folder (`03`), but the behavior when
the folder still contains documents or sub-folders was left open: reject, cascade,
or move children to the parent (`02` open questions, `03`). Backlog issue **#44
"Folders: delete (non-empty semantics)"** is blocked on this entry.

The surrounding model constrains the choice. Folders and documents use **soft
delete** (`DeletedAt`), and the data model states recoverability is exactly why
(`02`). Folders form a self-referencing hierarchy with a `ParentId` (`NULL` = top
level) and a unique `(OwnerId, ParentId, Name)` constraint (`02`). V1 is
single-user and personal (`00`/`01`); the product philosophy is advisory and keeps
the user in control of destructive actions (`01`). The three candidate behaviors
trade off differently against those facts:

* **Reject** — safe and predictable, but forces the client to empty the folder
  first.
* **Cascade** — convenient, but couples a folder's lifecycle to its documents'.
* **Move-to-parent** — loses nothing, but silently relocates a user's files as a
  side effect of a delete, which is surprising for a personal-document tool.

### Decision

**Reject non-empty deletion by default; support an explicit, opt-in recursive
cascade.**

* `DELETE /api/v1/folders/{id}` with no flag: if the folder has any non-deleted
  child folder or document, return **`409 Conflict`** (standard problem-details,
  `03`) and make no change. An empty folder soft-deletes as today.
* `DELETE /api/v1/folders/{id}?recursive=true`: **cascade soft-delete** the folder
  and its entire non-deleted subtree — descendant folders and the documents within
  them — in one transaction, each row stamped with the same `DeletedAt`.
* Cascade is **soft only**: bytes in the storage provider are untouched (`07`);
  the subtree remains recoverable, consistent with the soft-delete rationale (`02`).
* Documents soft-deleted by cascade have their queued/running `AnalysisJob`s
  cancelled, identical to a direct document delete (ADR per `06`, issue #38).
* No silent reparenting: a delete never moves a user's content to a different
  folder.
* Ownership is enforced over the whole subtree; cross-owner access anywhere returns
  **404 not 403** (`05`).

### Rationale

* **Safe default, no surprises.** Rejecting a non-empty delete means no content
  ever disappears or moves without an explicit instruction — the right default for
  a personal-document tool where the folder *is* the user's filing (`01`).
* **Cascade is honest and reversible.** When the user does intend to remove a
  subtree, one opt-in flag does it, and because the cascade is soft the action
  stays recoverable — leaning on machinery `02` already mandates rather than
  introducing hard deletes.
* **Rejects silent relocation.** Move-to-parent is the most "lossless" option on
  paper but mutates the user's organization as a side effect of an unrelated verb;
  rejected as astonishing.
* **Consistent with existing delete semantics.** Cascade reuses the document
  soft-delete + job-cancellation behavior (`06`, #38), so there is one delete story,
  not two.

### Trade-offs accepted

* A two-step UX for the common "delete this whole folder" case (try → 409 → retry
  with `recursive=true`, or the client surfaces a confirm). Accepted: an explicit
  destructive action is worth one extra round-trip.
* A recursive soft-delete must walk a potentially deep subtree in a single
  transaction. Accepted for V1's personal scale; if depth/volume grows it can move
  to a background job without changing the contract (the `recursive` flag stays).
* **Restore semantics are deferred.** Cascade stamps a shared `DeletedAt` so a
  subtree *can* be restored coherently later, but no restore endpoint is specified
  in V1 — flagged as a follow-up, not resolved here.
* Does **not** resolve maximum folder nesting depth (`02`), which remains a separate
  open question.

### Alternatives considered

* **Always reject (no cascade).** Simplest and safest, but pushes recursive
  client-side deletion (walk the tree, delete leaves up) onto every client for a
  routine operation; rejected as offloading avoidable work.
* **Always cascade (no flag).** Fewest round-trips, but makes the destructive,
  whole-subtree case the unguarded default — too easy to wipe a filing tree by
  deleting one folder; rejected.
* **Move children to parent (or root).** Never loses data, but silently relocates
  the user's documents as a side effect of a delete and can collide with the
  `(OwnerId, ParentId, Name)` uniqueness constraint on merge; rejected as
  surprising and constraint-fragile.
* **Hard delete on cascade.** Frees storage immediately, but discards the
  recoverability `02` is built around and would require deleting provider bytes
  inline; rejected for V1.

---

## ADR-008 — Job dispatch: adopt RabbitMQ, keep PostgreSQL as the durable outbox

* **Date:** 2026-06-06
* **Status:** Accepted (implementation deferred until after M3)

### Context

Issue #32 shipped the V1 background-job infrastructure per `06`: the module-owned
`AnalysisJob` table in the `jobs` schema is the durable work source, claimed by a
polling hosted worker with `FOR UPDATE SKIP LOCKED`. `06` explicitly reserves "a
dedicated message broker for the scale-out phase".

For the project's measurable V1 needs (single user, low job volume), the
DB-backed queue is sufficient — by throughput alone, a broker is **not** a
pragmatic necessity today. The drivers for deciding now are different:

1. **Learning value.** Filer is in part a vehicle for extending the developer's
   experience; RabbitMQ is the most widely used message broker in the .NET
   ecosystem and operating one end to end (publishing, acks, redelivery,
   dead-lettering) is a deliberate goal. For a solo personal project this is a
   legitimate requirement, recorded honestly rather than disguised as a scale
   argument.
2. **Scale-out path.** The broker is the already-documented destination (`06`),
   so adopting it early walks the path the architecture reserved anyway:
   push-based dispatch (no poll latency/traffic) and workers that scale
   independently of the API.

The open design question was what the broker *replaces*. A naive adoption
(publish the job content to RabbitMQ, drop the table) loses the transactional
"persist metadata + enqueue job" guarantee `06`/`08` rely on and would discard
job-state tracking (`AnalysisJob.Status/AttemptCount/Result`, `02`).

### Decision

Adopt **RabbitMQ for job dispatch**, keeping **PostgreSQL as the durable source
of truth** — broker-for-dispatch, database-for-durability.

* The `AnalysisJobs` table stays exactly as shipped: it is the **outbox** and the
  job-state record. Enqueueing still inserts the row in the caller's transaction;
  a message ("job {id} is ready") is then published to RabbitMQ.
* The consumer receives the message and runs the **same claim** through
  `IAnalysisJobStore` (`FOR UPDATE SKIP LOCKED`), so the single-claim guarantee
  remains database-enforced; the broker only replaces the *polling* as the wake-up
  signal. The polling loop is retained as a configurable fallback/sweeper so a
  broker outage degrades to today's behavior instead of stopping work.
* Implementation is a second `IBackgroundJobQueue` implementation in
  `Filer.Modules.BackgroundJobs`, selected by configuration exactly like the
  storage provider (`07`); no code outside the module changes. `docker-compose`
  gains a `rabbitmq` service.
* **Sequencing:** after the upload pipeline (M3) runs end to end on the current
  queue, so there is real traffic to migrate; before or alongside M5.

### Rationale

* **Keeps the transactional enqueue.** Writing the job row in the caller's
  transaction and relaying a message afterwards (the outbox pattern) is the
  standard answer to dual-write inconsistency; the V1 design already *is* the
  outbox, so the migration adds the relay rather than rebuilding durability.
* **Push without losing crash-safety.** Messages can be lost or redelivered;
  rows cannot. Re-running the claim on the consumer makes a duplicated or raced
  message harmless (claim returns nothing), and the sweeper recovers any job
  whose message was lost.
* **Honest driver.** Recording learning value as the primary motivation keeps the
  decision log truthful and prevents the precedent that infrastructure can be
  added under a vague scale pretext (`08`'s anti-overengineering rule is about
  unjustified complexity; this entry is the justification).
* **Seams already paid for.** `IBackgroundJobQueue` / `IAnalysisJobStore` /
  `IAnalysisJobHandler` (#32) confine the change to one module.

### Trade-offs accepted

* A new piece of infrastructure to run, monitor, and secure in a Docker-first
  personal deployment — the cost accepted in exchange for the learning value and
  the scale-out path.
* Two dispatch mechanisms (message + fallback sweeper) instead of one; accepted
  because the sweeper is the existing, already-tested loop.
* Not pragmatic by V1 load alone; accepted explicitly (see Context).

### Alternatives considered

* **Stay DB-only until scale demands a broker** — the most pragmatic choice and
  the default this ADR consciously overrides; rejected because it serves neither
  driver (learning, scale-out rehearsal) and the override is cheap given the seams.
* **Broker as the only queue (drop the table)** — loses transactional enqueue and
  job-state tracking; requires solving exactly-once delivery in the broker layer.
  Rejected.
* **Hangfire / Quartz.NET** — "real background jobs" with dashboards and
  scheduling, but their storage is itself a polled database (same mechanics as
  V1, less control), and they teach a library, not messaging. Rejected for this
  project's goals.
* **Other brokers (Azure Service Bus, Kafka, NATS)** — managed offerings conflict
  with self-hostable Docker-first deployment (`07`); Kafka is a streaming log,
  oversized for a work queue. RabbitMQ is the fit for both the workload and the
  learning goal.

---

## ADR-009 — DocumentTag join: owned by the Documents module, app-layer tag ownership

* **Date:** 2026-06-08
* **Status:** Accepted (ratified with the document-tag slices, #49)

### Context

`PUT/POST/DELETE /api/v1/documents/{id}/tags*` associate tags with documents
through the `DocumentTag` join (`02`, `03`). Two questions were open. First,
**which module owns the join** — Documents or Tags — given project-per-module with
one DbContext and one Postgres schema per module (`10`, ADR-003/004), and the rule
that a module reaches another only through its `*.Contracts` (`10`, rule 1).
Second, **the replace semantics** of `PUT`: how a user-supplied set interacts with
the `AiSuggested` rows the analysis pipeline writes (`06`), which `03` flagged as
an open question.

The surrounding model constrains both. The join carries a composite PK
`(DocumentId, TagId)` — one row per pair — and a `Source` of `User` or
`AiSuggested` (`02`). A Tag lives in the `tags` schema and a Document in the
`documents` schema; EF Core does not support a foreign key across two DbContexts /
schemas. The product is document-centric and advisory: the user stays in control,
and AI suggestions are applied but never silently discarded (`01`).

### Decision

**The Documents module owns the `DocumentTag` join** — the table lives in the
`documents` schema, configured on `DocumentsDbContext`, and the three endpoints are
Documents-module slices.

* `DocumentId` is a real FK to `Documents` **within the same context**, cascade on
  delete: deleting a document removes its associations.
* `TagId` is a **plain Guid column** — no EF navigation, no cross-schema FK.
  Tag ownership is validated in the app layer through a narrow Tags contract,
  `ITagOwnershipChecker.OwnsAllTagsAsync` (owner-scoped; empty set is vacuously
  true), mirroring `IFolderOwnershipChecker`. A document or tag the caller does not
  own is a **uniform 404**, indistinguishable from missing (`05`).
* `Source` is persisted **as text** (`HasConversion<string>`, `varchar(32)`),
  exactly like `Document.Status` — readable in SQL and stable across enum
  reordering. An index on `TagId` backs the by-tag reads (the list filter and the
  tag-delete cascade), since the composite PK does not front `TagId`.
* **Tag-side cascade on tag delete** flows the other direction through a narrow
  `Documents.Contracts` interface — introduced and consumed by the tag-delete slice
  (#48), the same shape as `IFolderDocumentRemover` (ADR-007): the data-owning
  module exposes the deletion seam; the Tags module calls it.
* **Replace semantics (`PUT`)** manage only `Source=User` rows. For a new set `S`
  on document `D`: for each tag in `S` ensure a `(D, tag)` row exists as `User` —
  insert if absent, **promote** an existing `AiSuggested` row to `User` (an update,
  not a duplicate, because of the composite PK); delete existing `User` rows whose
  tag is not in `S`; **leave `AiSuggested` rows not in `S` untouched**. `POST` is
  the single-tag upsert (promote if it was `AiSuggested`, idempotent if already
  `User`); `DELETE` removes the `(D, tag)` row regardless of `Source` — the only
  way an `AiSuggested` association is removed.

### Rationale

* **Ownership follows the document-centric grain.** The join is part of a
  document's metadata; placing it in the Documents schema keeps a document and its
  tags in one transactional boundary (the cascade-on-document-delete is a local FK,
  not a cross-module dance) and matches how the feature is navigated (`01`, `03`).
* **App-layer tag ownership is the only correct option** given one-schema-per-module
  with no cross-context FK. The narrow `ITagOwnershipChecker` is the established
  cross-module pattern (`IFolderOwnershipChecker`) and keeps the uniform-404 rule
  enforceable at one chokepoint (`05`).
* **Replace/promote honours the AI philosophy.** Preserving `AiSuggested` rows on a
  user replace means a suggestion is never silently dropped by an unrelated user
  edit; promoting on re-add reflects that the user has now endorsed it; explicit
  `DELETE` is the only discard — the user stays in control (`01`).
* **Text `Source` and the `TagId` index** reuse the house conventions
  (`Document.Status` storage; owner/folder indexes) rather than inventing new ones.

### Trade-offs accepted

* No database-level referential integrity between `DocumentTag.TagId` and a Tag
  row: a deleted tag's associations are cleaned up by the application (#48), not by
  a DB cascade. Accepted as the unavoidable cost of per-module schemas; the
  cross-module contract makes the cleanup explicit and testable.
* A `PUT` must read the current associations to compute the promote/preserve diff
  (one extra read). Accepted at V1's personal scale; the diff is the slice's, not
  the store's, so the persistence seam stays rule-free.

### Alternatives considered

* **Tags module owns the join.** Symmetric on paper, but cuts against the
  document-centric grain — the endpoints are `/documents/{id}/tags*`, the cascade
  that matters most is document-delete, and it would force the Documents module to
  reach Tags for its own metadata. Rejected.
* **A shared-kernel join table.** Would give a single table both modules see, but
  violates module ownership (`10`): no entity is owned by two modules, and
  SharedKernel is infrastructure-free. Rejected.
* **Cross-schema FK on `TagId`.** Not supported across DbContexts; would also
  couple the two schemas' migration order. Rejected.
* **`PUT` wipes `AiSuggested` rows too.** Simpler, but silently discards
  suggestions on any user edit — against the advisory philosophy (`01`). Rejected.
