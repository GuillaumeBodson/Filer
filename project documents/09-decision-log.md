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

## ADR-010 — Bulk tag operations: synchronous and capped, async deferred behind a generalized job runner

* **Date:** 2026-06-09
* **Status:** Accepted (design; implementation under M8 — epic #109, slices #110/#111; async path #112 deferred)

### Context

The tag slices (#45–#49, M4) shipped single-document tag association
(`PUT/POST/DELETE /documents/{id}/tags*`, ADR-009). The follow-up need is **bulk**
tagging: add/remove tags across many documents at once — an explicit selection of
files, or every document in selected folder(s) with optional recursion. `03`
already lists "Bulk operations (multi-delete, multi-move…)" as an open question.

The primitives exist; the open questions are about *contract* and *execution
model*, not new domain concepts (the join, `Source` semantics, and the
uniform-404 ownership rule are fixed by ADR-009):

1. **Execution model.** A folder-recursive bulk can touch many documents. The
   async-by-default principle (`06`/`08`) and the existing `BackgroundJobs` worker
   make "run it as a job" tempting — but that module is today purpose-built for the
   AI pipeline (`AnalysisJob` + `AnalysisJobWorker` + `IAnalysisJobHandler`,
   single-document, analysis-shaped). There is no generic job runner; a job-backed
   bulk op needs new infrastructure.
2. **Failure semantics** of a multi-target write — atomic vs best-effort partial.
3. **Where dispatch is heading.** ADR-008 replaces polling with RabbitMQ as the
   wake-up signal, leaving the Postgres table as the durable outbox — the dispatch
   layer is about to change.

### Decision

**Ship bulk tagging synchronously, atomically, and capped; defer the async path;
and when async is needed, generalize the job runner rather than add a one-off job
type.**

* **One batch endpoint:** `POST /api/v1/documents/tags/batch` — an `operation`
  (`add`/`remove`) over a `tagIds` set and a selector: either `documentIds`
  (selected files, #110) or `folderIds` with a `?recursive=` query param
  (folder-scoped, #111; recursion as a query param to match ADR-007's
  `?recursive=true`). It lives in the Documents module, which owns the join
  (ADR-009); folder-subtree resolution comes through a narrow `Folders.Contracts`
  reader (`10`), not by reaching into Folders.
* **Atomic, all-or-nothing.** The whole operation is one transaction; any
  not-owned/not-found document, tag, or folder fails the request (uniform **404**,
  `05`) and rolls everything back. Per-association idempotency (re-add / absent-
  remove are no-ops) and the ADR-009 `Source` rules are preserved.
* **Synchronous with a count-first cap.** The handler resolves and counts the
  affected documents before mutating; above a configurable cap (default **500**)
  it **rejects** behind a single named seam. No background-job infrastructure is
  built now.
* **Async is deferred (#112), not designed away.** The reject seam is the exact
  point a later change flips to "enqueue + `202` + job id". When a real
  oversized-bulk need appears, `BackgroundJobs` is **generalized** into a
  multi-type runner (job-type discriminator + payload + `IJobHandler` registry),
  **sequenced with the RabbitMQ migration (ADR-008)** — not extended with a
  parallel `BulkTagJob`.

### Rationale

* **YAGNI on the expensive part.** Durable-job infrastructure for a capacity the
  personal-scale V1 may never reach is the speculative complexity `08`/`13` warn
  against. A cap + reject ships the feature now and makes the limit explicit, the
  way the 50 MB upload limit does (`04`).
* **The dispatch layer is in flux.** A `BulkTagJob` built on today's polling worker
  would be rebuilt onto RabbitMQ almost immediately (ADR-008). Deferring avoids
  throwaway plumbing; the synchronous path is insulated from that migration.
* **Generalize once, against two real consumers.** A one-off second job type
  duplicates the worker loop, status enum, store, `SKIP LOCKED` claim — and, post
  ADR-008, the RabbitMQ consumer/binding. A single typed runner wires the broker
  once and lets future bulk move/delete plug in as handlers. Doing it when a second
  consumer actually exists (analysis + bulk) avoids designing the abstraction blind.
* **Atomic is the predictable contract** for a personal-document tool: a bulk
  action either took or it did not, with the uniform-404 rule intact — no partial
  state to reconcile.

### Trade-offs accepted

* Selections above the cap are rejected rather than queued until #112 lands —
  accepted; the seam keeps that a one-line behavior swap, not a contract change.
* A folder-recursive op walks a possibly deep subtree in one synchronous
  transaction (bounded by the cap) — accepted at V1 scale, as ADR-007's recursive
  folder delete accepts.
* Atomic all-or-nothing fails the whole batch on one bad id (no partial success /
  per-item report) — accepted for contract simplicity; a best-effort mode can be
  added later without breaking the atomic default.
* The over-cap status code (422 vs 400) is left to the implementing slice — the
  one open sub-decision.

### Alternatives considered

* **Add a `BulkTagJob` now.** Scales immediately and leaves the green analysis path
  untouched, but duplicates the entire job mechanism (and future broker wiring) and
  commits to async before it is needed; rejected in favor of deferring + generalizing.
* **Generalize the job runner now.** Removes duplication up front, but touches
  already-shipped analysis code speculatively and designs the abstraction with only
  one real consumer; rejected as premature — revisit under #112.
* **Best-effort with a per-item result report (207-style).** Allows partial success
  on large selections, but pushes reconciliation onto every client and complicates
  the contract; rejected as the default, deferrable later.
* **No bulk endpoint — clients loop the single-document calls.** Zero new surface,
  but N round-trips and no atomicity for a routine operation; rejected as offloading
  avoidable work, the same reasoning as ADR-007.

---

## ADR-011 — API client: generate a typed Blazor client from OpenAPI with Kiota

* **Date:** 2026-06-14
* **Status:** Accepted (ratified 2026-07-17 — implementation shipped with the
  FE-M1 client slice, #144; see the Ratification section below)

### Context

The platform is API-first (ADR-003, `00`) and the frontend is Blazor WebAssembly
consuming the REST API over HTTP (ADR-001). Before frontend work starts, the
client↔API connection strategy must be settled: handwritten, generated from
OpenAPI, or sharing types directly.

Two facts shape the choice:

1. **The API already publishes OpenAPI natively** via
   `Microsoft.AspNetCore.OpenApi` (`AddOpenApi()` / `MapOpenApi()` in
   `Program.cs`) — the contract document already exists, so generating a client is
   nearly free.
2. **The transport DTOs are not shareable as-is.** HTTP request/response DTOs live
   inside each module's `Features/` (implementation), not in the `*.Contracts`
   projects — those contracts are *module-to-module* seams, not a browser-facing
   transport surface (`10`).

### Decision

**Generate a typed C# client from the published OpenAPI document using Kiota,
consumed by the Blazor WASM app; regenerate as part of the build/workflow so the
client always tracks the contract.** Do not handwrite the client, and do not share
server DTO classes with the browser.

* Client is derived from `/openapi/v1.json`, not authored by hand.
* Generation is wired into the build (checked-in vs generated-on-build is left to
  the implementing slice).
* Whether to keep the generated client in the shared Razor Class Library (so web
  and the future MAUI shell reuse it, ADR-001) is an implementation detail of the
  first frontend slice.

### Rationale

* **Contract-derived = literally API-first.** A generated client breaks at build
  time when the server contract drifts, turning silent client/server skew into a
  compile error — the strongest possible enforcement of the API-first principle.
* **Stack consistency.** Kiota is Microsoft's current OpenAPI client generator and
  pairs with the native `Microsoft.AspNetCore.OpenApi` pipeline already adopted —
  one ecosystem, actively supported, the documented .NET 10 path.
* **Zero drift, near-zero toil** versus handwriting every endpoint and DTO by hand.
* **Preserves module boundaries.** Sharing server DTOs would leak module internals
  into the browser and bypass the discipline `Filer.Architecture.Tests` enforces.

### Trade-offs accepted

* Kiota's generated surface and ergonomics differ from NSwag's; a small learning
  curve and less Blazor-specific community tooling.
* The generated client is a build artifact to manage (regeneration step, review of
  generated diffs).
* Client quality depends on OpenAPI-doc quality — this imposes discipline on
  `operationId`s, declared response types, and problem-details shapes server-side.

### Alternatives considered

* **NSwag.** Mature and battle-tested in Blazor, simple MSBuild integration; a
  viable fallback. Rejected only for stack consistency — it adds a second OpenAPI
  toolchain alongside the native Microsoft stack — not for any capability gap.
* **Handwritten typed `HttpClient`.** Full control, no codegen dependency, but
  silent drift and perpetual manual upkeep as the contract evolves; rejected.
* **Share `*.Contracts`/DTO classes directly (C#-to-C#).** Tempting in a
  single-language stack, but the transport DTOs aren't in `*.Contracts`, and
  sharing them couples the client to server internals and violates the boundary
  tests; rejected.
* **Refit (handwritten typed interfaces).** Still manual contract maintenance, just
  relocated; rejected for the same drift reason.

### Ratification (2026-07-17, FE-M1)

The implementation landed as designed (#144). The two sub-decisions this ADR
explicitly deferred to the implementing slice were resolved as follows (details
in `src/Clients/Filer.ApiClient/README.md`):

* **Checked-in generation, not generate-on-build.** The generated client *and*
  its input snapshot (`openapi/v1.json`) are committed. A CI **drift gate**
  regenerates from the snapshot and fails the build on any diff, so a contract
  change cannot land without regenerating — CI needs neither a running API nor
  PostgreSQL, and generated diffs are reviewable.
* **Standalone `Filer.ApiClient` project, not the shared RCL.** The client is a
  platform-neutral class library so the future MAUI shell (RM-02) can reference
  it without dragging in Razor/UI dependencies; the RCL (`Filer.Ui`) consumes it
  like any other host.

### Addendum (FE-M2, #146/#135)

The API serializes JSON with **`JsonNumberHandling.Strict`** (`Program.cs`).
The ASP.NET web default (`AllowReadingFromString`) made the OpenAPI generator
describe every number as an `["integer","string"]` union, which Kiota can only
map to `UntypedNode` — un-typing the paged envelope and sizes in the generated
client. Client quality depends on contract quality, so the contract says
numbers are numbers.

---

## ADR-012 — Frontend development: start in parallel after the core API, web-first

* **Date:** 2026-06-14
* **Status:** Accepted

### Context

The question is *when* to start frontend development, given that backend work is
currently mid-V1. Because the platform is API-first (ADR-003), the frontend is
gated by **contract stability**, not by V1 completion:

* **M1–M4** (Auth, Documents, Folders, Tags) already ship the stable core CRUD
  contract a UI renders against.
* **M5** (AI analysis) and **M6** (search) are *additive* to existing screens — a
  suggestions panel, a search box — not restructurings. M5 endpoint contracts
  (#54/#55) are still in flux.
* **M7** (observability/CI) has no frontend surface.

### Decision

**Start frontend now, in parallel with the remaining backend (M5/M6), web-first,
against the frozen core endpoints only.**

* Build the **web** client (Blazor WASM) plus the shared Razor Class Library
  (ADR-001) and groundwork (JWT acquisition/storage/refresh, the Kiota-generated
  client per ADR-011, app shell/navigation) — none of which depends on remaining
  backend work.
* Build the stable vertical slice first: login → upload → browse → folders → tags.
* **Defer** the AI-suggestions UI and search UI until the M5/M6 contracts settle.
* **Defer** the MAUI Blazor Hybrid mobile shell to RM-02 (photo capture, `14`),
  reusing the same shared RCL.

### Rationale

* **API-first wants an early consumer.** A real client is the best validator of the
  contract — async upload UX, job-status polling, error/problem-details shape,
  pagination, and the ownership-404 rule (`05`) only get exercised when something
  consumes them. Finding ergonomic problems during M5 is cheap; finding them after
  V1 is "done" means reopening finished work.
* **M5/M6 are incremental UI additions**, so waiting for full V1 buys the frontend
  almost nothing.
* **The groundwork is unblocked today** and is pure prep against the already-stable
  auth/document/folder/tag surface.

### Trade-offs accepted

* Some client rework if a core contract still shifts — mitigated by consuming only
  frozen endpoints first and treating M5/M6 UI as follow-on slices.
* Context-switching cost for a single maintainer — accepted for the
  contract-validation payoff.
* Frontend coding conventions are not yet written. They are captured **just-in-time
  in a future `15-frontend-architecture.md`** once the first slice exists, with
  frontend sections added to `12`/`13` then — deliberately not pre-authored, per
  the anti-anticipation rule in `13`.

### Alternatives considered

* **Wait until all of V1 (M5–M7) ships.** Loses the early-consumer feedback loop and
  defers discovery of API-ergonomics issues until after they're "done"; rejected.
* **Start mobile (MAUI) now.** Premature — mobile's distinctive value is RM-02
  photo capture (post-V1), and it reuses the web RCL anyway; rejected.
* **Write the full frontend best-practices doc up front.** Speculative, violates the
  no-anticipation rule (`13`); rejected in favour of just-in-time capture once real
  components exist.

---

## ADR-013 — Observability: OpenTelemetry, with trace context persisted on the job row for cross-process continuity

* **Date:** 2026-07-11
* **Status:** Accepted (correlation foundation precedes #75; full observability build is M7).
  Correlation foundation implemented in #59: OTel pipeline in the host,
  `AnalysisJob.CorrelationContext` stamped at enqueue and link-resumed by the
  worker's processing span.

### Context

`04-non-functional.md` (Observability) requires structured logging with
correlation ids, metrics (including analysis queue depth and job success/failure),
distributed **tracing spanning request → queued job → worker execution**, and
liveness/readiness health endpoints — noting the *tooling* (e.g. OpenTelemetry) is
an implementation detail as long as the data is emitted interoperably. ADR-005
resolved the logging half: correlate via the framework's W3C trace context
(`Activity`/`TraceId`), no bespoke correlation id, on the stated premise that
`traceparent` "flows across HTTP and, later, messaging boundaries … without any
hand-written plumbing into the future worker tier."

Two things were left open, and one premise turns out to be incomplete:

1. **The emit layer and its viewer were never chosen.** Logging exists; traces,
   metric export, and health endpoints do not. A local way to *see* the emitted
   data is also missing (the recent Ollama timeout was only diagnosable via `psql`
   against `AnalysisJobs`).
2. **ADR-005's "propagates for free" does not hold across our own hand-off.** Trace
   context flows automatically only over a *live synchronous call* (HTTP), where
   .NET rides it in `traceparent` headers and `Activity.Current`. But the
   upload→analysis hand-off is deliberately **asynchronous through a persisted
   outbox** (`06`, ADR-008): the request inserts an `AnalysisJob` row and ends —
   `Activity.Current` is cleared — and a background loop later *claims* that row
   with no ambient context from the originating request. The worker therefore
   starts a **new, unrelated trace**. The request→job→worker correlation `04`
   mandates is, today, already broken; it is a present gap, not a hypothetical.
3. **RabbitMQ (ADR-008) does not close it either.** That message is a bare "job
   {id} is ready" wake-up carrying no context, the consumer re-reads the row, and
   the retained **sweeper fallback** finds jobs with no message at all. The message
   cannot be the reliable carrier.

### Decision

Adopt **OpenTelemetry** as the emit layer for traces, metrics, and logs
(fulfilling `04`, extending ADR-005's logging decision rather than replacing it),
and **persist the originating W3C trace context on the `AnalysisJob` row** as the
durable correlation mechanism across the async hand-off.

* **Durable correlation on the row.** Enqueue stamps the request's `traceparent`
  onto a new nullable **`CorrelationContext`** column on `AnalysisJob` (`02`). When
  the worker claims the job it reads that value and starts its processing span
  **linked** to the originating trace, so request → queued job → worker execution
  is one connected trace. This rides the **row**, which is the source of truth in
  every dispatch path (ADR-008): it works for polling today, for the RabbitMQ
  message tomorrow, and for the sweeper fallback — none of which is true of a
  message-carried id. The column name is deliberately broader than "traceparent"
  so it can also carry W3C `baggage` later without a further migration.
* **Message-header propagation is additive, not authoritative.** When #75 lands,
  the publisher also injects `traceparent` into message headers (the OTel messaging
  convention) as a latency optimisation, but the row remains the source of truth.
* **Instrumentation.** ASP.NET Core and `HttpClient` instrumentation (so provider
  calls such as Ollama appear as timed spans), plus the existing
  `Filer.BackgroundJobs` `Meter` (#53) exported over OTLP. Per-service resource
  attributes (`service.name` = api / worker) so signals stay attributable once the
  worker is a separate process (`04` scalability).
* **Health endpoints.** Liveness and readiness (readiness checks DB and storage;
  it gains RabbitMQ and provider-reachability checks as those arrive).
* **Local viewer.** The **standalone Aspire dashboard** container as the OTLP sink
  for local dev — a viewer of the emitted data, added to `docker-compose` behind a
  profile like the `ollama` service. The full Aspire AppHost/orchestration model is
  **not** adopted (see Alternatives).
* **Sequencing.** The correlation foundation (OTel wiring + HttpClient/ASP.NET
  tracing + the `CorrelationContext` column + worker span linkage + the dashboard)
  lands **before or with #75**, so RabbitMQ is instrumented from day one rather
  than retrofitted. The remaining breadth (full metric coverage, health,
  dashboards, CI wiring) is **M7**.

### Rationale

* **Not speculative — a `04` requirement with a present gap.** This does not
  violate the anti-anticipation rule (`13`): observability is a stated V1
  non-functional requirement, and the trace-continuity fix repairs a correlation
  that is *already broken*, discovered concretely during the #52 Ollama bring-up.
* **The row is the only universal carrier.** Because ADR-008 keeps the table as the
  outbox and retains a message-less sweeper, the sole element present across every
  dispatch path is the row. Persisting context there makes ADR-005's promise
  actually hold, and makes the mechanism broker-independent — the migration to
  RabbitMQ adds nothing to the correlation design.
* **Debuggability falls out for free.** A failed job then carries the context tying
  it to its upload and to every worker log line — one trace lookup instead of a
  `psql` join, the exact friction that motivated this decision.
* **Seams already paid for.** The change is confined to the host (OTel registration,
  health), the `AnalysisJob` entity/enqueue/claim (`BackgroundJobs`), and one new
  compose service — no module boundary moves.
* **OTel keeps deployment cloud-agnostic.** OTLP export targets any backend
  (Aspire dashboard now, Grafana/App Insights/etc. at the SaaS phase) without code
  change, honouring `04`'s infrastructure-agnostic principle.

### Trade-offs accepted

* A new nullable column and a small enqueue/claim change to `AnalysisJob` — cheap,
  additive, and the durable-outbox shape already invites carrying such metadata.
* Two propagation mechanisms once #75 lands (row-authoritative + additive message
  header); accepted because the row path must exist for the sweeper regardless, and
  the header is a thin optimisation.
* A new local-dev container (Aspire dashboard); accepted, gated behind a compose
  profile so a plain `docker compose up` never starts it (as with `ollama`).
* Observability breadth is staged across the foundation slice and M7 rather than
  landing at once; accepted to keep #75 unblocked and M5/M6 focused.

### Alternatives considered

* **Rely on ADR-005's "propagates for free" as written.** The default this ADR
  corrects — it silently assumes a synchronous hop and leaves request→job→worker
  uncorrelated across the outbox; rejected because `04` requires that span and the
  gap is real today.
* **Carry the trace context only in the RabbitMQ message header.** The conventional
  OTel messaging approach, but it fails the sweeper fallback (no message) and is
  unavailable until #75; rejected as the *authoritative* mechanism, kept as an
  additive one.
* **Bespoke correlation id persisted on the row.** Works, but re-introduces exactly
  the custom id ADR-005 rejected as redundant with the trace id; rejected for
  consistency — persist the standard `traceparent`, not a parallel identifier.
* **Adopt full .NET Aspire (AppHost + orchestration).** Gives the dashboard plus
  service orchestration, but it is a hosting/orchestration framework that would
  reshape the composition root and local run model and pulls toward its own
  conventions — at odds with the Docker-first, cloud-agnostic deployment principle
  (`04`) and the "no unjustified complexity" rule (`13`). The dashboard alone
  delivers the local-viewer value at near-zero architectural cost; rejected in
  favour of the standalone dashboard.
* **Defer all observability to M7 as originally planned.** Simplest, but forces
  retrofitting trace propagation into RabbitMQ (#75) after the fact and leaves the
  known request→job→worker gap open through M5/M6; rejected in favour of landing the
  minimal correlation foundation before #75.

---

## Note - Agentic provider pulls owner-scoped data mid-analysis (#119)

* **Date:** 2026-07-16
* **Status:** Noted (no ADR - additive, contained experiment)

The experimental, opt-in `OllamaAgenticAnalysisProvider` (#119) is the first
provider to read owner-scoped data *during* an analysis run: it ranks folder
candidates from the request's tree, then samples each existing candidate's
contents through the new `IFolderContentLookup` port (Documents.Contracts)
before confirming. Points worth recording without a full ADR:

* **The shared contract did not change.** `IAIAnalysisProvider` stays a single
  `AnalyzeAsync` call; the loop lives entirely inside one adapter, selected via
  `AiAnalysis:Provider = OllamaAgentic`, never the default, and deletable
  without touching the shipped pipeline (`06`, Provider Abstraction).
* **Mid-analysis reads follow the 404 invariant.** The lookup is owner-scoped
  by construction (keyed by the request's additive `OwnerId`); a cross-owner or
  soft-deleted folder reads as *empty*, indistinguishable from an empty folder
  (`05`).
* **MCP was considered and rejected** for exposing the lookup as a tool the
  model calls itself: the provider runs in-process against a single owner's
  context, so a protocol boundary adds surface (tool descriptions, transport,
  authz mapping) with no consumer - a plain port is enough. Revisit only if a
  remote/multi-tool agent becomes a real requirement.
* Business value is unproven; the experiment does not gate M5 closure.

---

## ADR-014 — Client-side token storage: browser localStorage behind the host-owned `ITokenStore` seam

* **Date:** 2026-07-17
* **Status:** Accepted (records the FE-M1 implementation, #128; gap surfaced by
  the FE-M1 milestone review, #134/#171)

### Context

FE-M1 shipped the client auth plumbing (#128): `BearerTokenHandler` attaches the
JWT bearer token, `TokenRefresher` rotates the pair through `/auth/refresh`, and
persistence sits behind the `ITokenStore` interface in `Filer.ApiClient`, with
each host supplying its own implementation. `05-security.md` fixes the *server*
side — short-lived access JWT (~15 min), refresh tokens stored hashed, rotation
with family revocation on reuse — but said nothing about where a **client**
keeps the pair. The web host had to decide, and the decision shipped silently:
`LocalStorageTokenStore` writes both tokens to browser localStorage. Anything
readable by JavaScript in the origin is readable by successful XSS, so this is a
security-relevant choice that deserved an ADR at the time; this entry records it
retroactively, together with the 401-retry design that has no other home.

Constraints that shaped it:

* The API is stateless bearer-token auth (`05`); no cookie session exists
  server-side, and introducing one is a server-contract change.
* A session must survive a page reload and a browser restart — re-entering
  credentials on every F5 is unacceptable for a daily-use personal tool.
* ADR-001's multi-client plan means storage must be per-host: browser storage on
  web, platform secure storage on the future MAUI shell (RM-02).

### Decision

* **The persistence seam is host-owned.** `ITokenStore`
  (get/save/clear + a `Changed` event) lives in `Filer.ApiClient`; each host
  registers its own implementation. The shared plumbing never knows where
  tokens live.
* **The web host persists the full `TokenPair` — access and refresh token — in
  browser localStorage** (`LocalStorageTokenStore`, `internal` to `Filer.Web`),
  as camelCase JSON under the single key `filer.tokens`.
* **The store is the single source of truth for auth state.** `Changed` fires on
  save and clear and drives `FilerAuthenticationStateProvider`; signing out is
  clearing the store. Corrupted or legacy JSON reads as `null` (signed out) —
  it never throws.
* **401 → refresh-once → retry** (recorded here for lack of another home):
  `BearerTokenHandler` attaches the bearer when present; on a 401 *with a
  refresh token in hand* it refreshes once and retries the request once with the
  rotated token. `TokenRefresher` is single-flight (a semaphore serializes
  concurrent refreshes; a caller that arrives after the pair already rotated
  succeeds without a round-trip) and calls `/auth/refresh` through a dedicated
  handler-free named client, so a refresh can never recurse into the handler or
  carry a bearer token. A failed refresh clears the store, ending the session
  everywhere at once via `Changed`.

### Rationale

* **Session persistence is the point.** localStorage is the only browser
  primitive that survives reload and restart without server-side changes; it
  matches the stateless bearer contract the API already has.
* **The blast radius is bounded by the server design.** The access token expires
  in ~15 minutes; the refresh token is single-use with rotation, and reuse of a
  consumed token revokes the family (`05`). A stolen pair is therefore
  detectable and time-boxed, which is what makes the localStorage trade
  tenable at V1's single-user scale.
* **The seam contains the decision.** Hardening later (e.g. moving the refresh
  token to an HttpOnly cookie, or in-memory access tokens) is a host-level swap
  behind `ITokenStore` plus a server change — no shared plumbing moves.
* **Refresh-once is the loop-free minimum.** One refresh and one retry per
  request bounds the work a hostile 401 can cause; single-flight prevents a
  burst of parallel 401s from spending the refresh token twice (which rotation
  would punish by revoking the family).

### Trade-offs accepted

* **XSS exposure, accepted explicitly.** Any script running in the origin can
  read both tokens. Mitigations: Blazor's default output encoding (no raw HTML
  interpolation), no third-party scripts in the app shell, short access-token
  life + rotation/family-revocation server-side. Accepted for the V1
  personal-use threat model (`05`); revisit before SaaS.
* Tokens are shared across tabs — one session per browser profile. Also a
  feature (sign-out in one tab signs out all).
* The `Changed` event makes the store stateful, which pins its DI lifetime to
  the auth-state provider's — a composition constraint the DI-scoping fix
  (#166) must respect.

### Alternatives considered

* **Refresh token in an HttpOnly, Secure, SameSite cookie; access token in
  memory.** The strongest browser posture (script cannot read the refresh
  token), but requires the API to issue/accept cookies on the refresh endpoint,
  CSRF defense, and CORS credentialed-request handling — a server contract
  change serving only browser clients, while MAUI would keep the token flow.
  Deferred as the designated SaaS-phase hardening, not done for V1.
* **In-memory only.** Immune to storage theft, but the session dies on every
  reload; rejected on UX for a daily-use tool.
* **sessionStorage.** Per-tab and gone with it: marginally narrower exposure
  than localStorage (still script-readable) with strictly worse UX; rejected.

---

## ADR-015 — Test libraries: xUnit v3 runner, FluentAssertions, Moq at the seams, bUnit for components

* **Date:** 2026-07-17
* **Status:** Accepted (records choices already pinned in
  `Directory.Packages.props`; closes the corresponding `12` open item)

### Context

`12-testing-strategy.md` names the framework *roles* (runner, assertions, mocks,
component rendering) but left the concrete libraries as an open item to be
recorded "as a short ADR once picked". They are de facto picked: every test
project pins them centrally via `Directory.Packages.props`, and FE-M1 added the
component-test layer. This entry ratifies the set.

### Decision

* **Runner: xUnit v3** (`xunit.v3`) across all test projects.
* **Assertions: FluentAssertions 8.x** — used under the Xceed Community
  Licence, free for non-commercial use; Filer is a personal, non-commercial
  project. Revisit the licence position if the project ever commercializes
  (options then: pay, pin to 7.x, or migrate to Shouldly/AwesomeAssertions).
* **Mocks: Moq**, at designed seams only, per `12`'s mocking policy (mock the
  seam, never the type under test; hand-rolled fakes remain preferred where
  behavior matters, e.g. the `FakeTokenStore` in `Filer.Ui.Tests`).
* **Components: bUnit 2.x** for rendering Razor components in `Filer.Ui.Tests`
  — test-framework agnostic, pairs with xunit.v3.
* Versions stay centrally pinned in `Directory.Packages.props` (`10`).

### Rationale

* All four are the mainstream, actively maintained default in their role, and
  they are already in use across 600+ green tests — ratifying beats churning.
* The FluentAssertions 8.x licence caveat is the one non-obvious fact worth a
  permanent record; it is documented here and next to the pin.

### Alternatives considered

* **Shouldly / AwesomeAssertions** — licence-clean alternatives to
  FluentAssertions; not adopted now to avoid a mechanical rewrite of the whole
  suite, kept as the designated fallback if the licence position changes.
* **NSubstitute** — equivalent capability to Moq; no reason to switch an
  established suite.

---

## ADR-016 — Frontend design system: hand-rolled CSS on design tokens, no component library

* **Date:** 2026-07-17
* **Status:** Accepted (re-evaluate at the FE-M2 milestone review, #132)

### Context

FE-M1 shipped an implicit visual direction that was never recorded as a
decision: `tokens.css` in the `Filer.Ui` RCL as the single source of visual
primitives (colours, typography, spacing, radius, elevation), scoped
`.razor.css` per component, and **no component library**. The investment so far
is small (~200 lines of CSS) and deliberately token-driven — no component
hard-codes a colour or spacing.

FE-M2 builds the first six real screens (auth forms, server-paginated document
list, upload status UX, folder tree with rename/move, dialogs, menus, toasts).
This is where the choice becomes structural and the last moment where switching
is cheap, so it must be decided explicitly. The two credible component
libraries were evaluated against their mid-2026 state (sources on #177):

* **MudBlazor v9.7** — healthy monthly cadence, net10.0 supported, and its
  look is genuinely themable from CSS custom properties (`--mud-palette-*`,
  `--mud-typography-*`), so `tokens.css` could drive it. But it adds **~2 MB to
  the WASM download** (one of the heaviest Blazor libraries), has **no formal
  WCAG conformance** (community-driven a11y with documented gaps in menus,
  radio, pagination), `MudTreeView` has **no node drag-and-drop and no inline
  rename** — precisely what the folder tree (#138) needs — and bUnit testing
  carries documented friction (`MudPopoverProvider` setup, loose JSInterop,
  DataGrid disposal bug).
* **Fluent UI Blazor v4.14 / v5-RC** — *not* an officially supported Microsoft
  product (best-effort OSS maintained by MS employees + community). The v5
  rewrite is **still RC with no announced GA date**, so adopting v4 today buys
  a migration; moving away from the stock Fluent look is hard **by design**
  (v5 explicitly targets pixel-perfect Fluent 2); `FluentDataGrid` with a
  server-side `ItemsProvider` — the shape of the document list (#135) — has
  several open pagination bugs; bUnit needs JS-module and DesignToken mocks in
  v4.

Two cross-cutting observations weighed heavily. First, both libraries are
weakest exactly on FE-M2's core components (the folder tree and the
server-paginated list), so Filer would pay the dependency *and* still hand-roll
the hard parts. Second, Fluent's own v5 rewrite abandons its web-components
layer in favour of native `<dialog>` elements and plain HTML tables — the very
browser primitives a hand-rolled approach uses directly, without the
dependency.

### Decision

**Keep the hand-rolled design system.**

* `tokens.css` stays the single source of visual primitives; components and
  scoped styles reference tokens only, never raw values.
* Overlay primitives (dialogs, menus, toasts) build on **native browser
  primitives**: `<dialog>` (built-in focus trap and top-layer rendering) and
  the popover API — both baseline in evergreen browsers.
* Shared components live in the `Filer.Ui` RCL, which the future MAUI Blazor
  Hybrid shell (RM-02) reuses; plain HTML/CSS is Hybrid-compatible by
  construction.
* **Accessibility is an in-house responsibility** — no library was going to
  provide it anyway (see Context); the Definition of Done already mandates
  labelled controls and keyboard reachability per screen.
* A visual-identity pass on the tokens (palette, dark-mode decision, AA
  contrast check) precedes the first screen slice — tracked as #178.
* Frontend conventions, including these component-authoring rules, land in
  `15-frontend-architecture.md` as ADR-012 already planned.

### Rationale

* **The evaluation strengthened, not just permitted, the status quo**: the
  libraries' gaps land on FE-M2's core needs, their a11y story removes their
  best argument, and MudBlazor's ~2 MB payload is material for a WASM app.
* **Anti-anticipation** (`13`): a heavyweight dependency adopted for
  convenience components that then need hand-rolling anyway removes no real
  complexity.
* **bUnit stays trivial** — hand-rolled components are plain markup; both
  libraries carry documented test-harness friction while `12` requires
  component tests per slice.

### Trade-offs accepted

* Every primitive (tree, pagination, toasts) is paid for in build time and
  bUnit tests rather than downloaded — accepted for FE-M2's modest component
  needs.
* WCAG-correct behaviour must be verified in-house per the DoD, with no
  library baseline to lean on.
* **Explicit exit ramp:** re-evaluate at the FE-M2 milestone review (#132). If
  the screens revealed real pain, adopting MudBlazor then costs barely more
  than today — the protected investment is a token file and a small set of
  RCL components.

### Alternatives considered

* **MudBlazor v9** — richest free component set and honest CSS-variable
  theming; rejected on payload, absent WCAG conformance, tree-component gaps,
  and bUnit friction.
* **Fluent UI Blazor v4** — rejected on timing (v5 RC with no GA date means a
  guaranteed migration), rigid theming, DataGrid pagination bugs, and its
  unsupported-OSS status despite the Microsoft branding.
* **Wait for Fluent UI Blazor v5** — indefinite (no GA date) and it would
  still impose the Fluent 2 look; its own move to native primitives is better
  consumed directly.

### Status at the FE-M2 review (#132)

Held. The "Registre" identity (#178) landed on the token contract as planned;
every FE-M2 screen shipped on tokens + native primitives with no component
library. The `15-frontend-architecture.md` conventions doc promised here and in
ADR-012 was authored just after the review (#194), capturing the
component-authoring and token rules as built.

---

## ADR-017 — Search: thin API module over a Documents-owned query contract

* **Date:** 2026-07-18
* **Status:** Accepted

### Context

M6 delivers `GET /api/v1/search` (#57): ranked full-text over the caller's
documents, backed by a generated `tsvector` column with a GIN index (`02`).
`10-solution-structure.md` reserves a `Filer.Modules.Search` module and `03`
lists Search as its own endpoint group — but the searchable rows, and therefore
the tsvector column, live in the Documents module's `documents` schema, and a
module may not touch another module's DbContext or schema (ADR-003/004). Three
placements were possible: a search slice inside Documents serving a route
outside its own prefix; a Search module with its own DbContext mapping a SQL
view over the `documents` schema; or a thin Search module consuming a query
contract implemented by Documents.

### Decision

**Search is a thin API module; the searchable data stays owned by Documents.**

* `Filer.Modules.Documents.Contracts` exposes `IOwnerDocumentSearch`
  (owner-scoped query in, ranked page of hits out). The EF implementation —
  tsvector matching, `ts_rank` ordering, the tsquery strategy — lives in
  Documents, which owns the column, the GIN index, and the migration. The
  precedent is `IFolderContentLookup`/`IFolderDocumentRemover`: a narrow
  contract owned by the module that owns the rows.
* `Filer.Modules.Search` owns the HTTP contract only: the `/api/v1/search`
  slice (validation, error codes, response DTO with the opaque `score`). Like
  Storage, it has **no DbContext and no migrations**.
* The `SearchVector` column is a **shadow property**: a persistence concern the
  `Document` entity never sees.

### Rationale

* **The migration follows the table.** A generated column on
  `documents.Documents` can only be owned by `DocumentsDbContext` — any other
  owner would split the schema's migration history.
* **The module map stays honest**: `10`'s reserved Search module exists and
  serves its own route prefix, and NetArchTest keeps the dependency direction
  (Search → Documents.Contracts only) enforced.
* **The seam is where semantic search lands** (RM-04): a pgvector sibling adds
  a second implementation or a second contract behind the same thin HTTP
  module, and the opaque `score` (deliberately not specified as `ts_rank`,
  `03`) absorbs it without a client-breaking change.

### Trade-offs accepted

* Two small projects whose V1 content is one slice and one error-code class —
  accepted to keep the reserved module map and the future semantic seam real.
* Ranked search and the list's substring `?q=` filter deliberately coexist as
  two semantics (`03`): the list filter keeps intra-word matching for
  navigation; `/search` ranks. The `EfDocumentStore` seam comment records the
  decision not to upgrade the list filter.
