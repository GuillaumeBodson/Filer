# /docs/13-code-quality-and-design.md

# Code Quality & Design Standards

## Purpose

Defines *how the code itself is written* — the standards that turn the
architecture in `00`/`10` into consistently high-quality source. Where `08` tells
AI assistants which decisions to respect, this document gives both humans and
assistants the concrete code-level rules: error handling, cancellation,
validation, logging, the OOP/SOLID posture, which design patterns are endorsed
(and which to avoid prematurely), static analysis, and the Definition of Done.

Related documents: `03-api-specification.md` (error shape, DTOs),
`04-non-functional.md` (maintainability), `05-security.md`,
`08-ai-development-guidelines.md` (assistant behaviour),
`10-solution-structure.md` (layout, slices), `12-testing-strategy.md`. Related
decisions: ADR-003 (modular monolith + vertical slices), ADR-004 (plain feature
services, pragmatic API style).

---

## The Governing Tension: principled, not gold-plated

This project deliberately avoids speculative complexity (ADR-003/004; `08`:
"avoid unnecessary abstraction", "avoid speculative complexity", "do not
overengineer V1"). The principles below — OOP, SOLID, design patterns — serve
that goal; they are not in tension with it. They exist to make code **readable
and cheap to change**, not to maximise indirection.

The deciding question for any abstraction, interface, or pattern is always:
*does it remove or contain a real, present complexity?* If yes, apply it. If it
only anticipates a complexity that may never arrive, leave the seam documented
and the code simple. An interface with exactly one implementation and no test
double and no planned second implementation is usually premature — the designed
exceptions are the infrastructure seams (`IFileStorageProvider`,
`IAIAnalysisProvider`, `IBackgroundJobQueue`, `IClock`, `ICurrentUser`), which
earn their interface through swappability, testability, and the SaaS evolution
path (`00`, `07`).

---

## Coding Standards (C#)

### General

* **Readability first.** Optimise for the next reader. Clear, explicit names over
  cleverness; small methods with one responsibility; early returns over deep
  nesting.
* **`sealed` by default** for classes not designed for inheritance (feature
  services, providers, entities unless a hierarchy is intended). This is already
  the house style (`LoginService`, `Error`).
* **Immutability at the boundary.** Request/response DTOs and value-like types are
  `record` types; mutate state only where the domain genuinely changes.
* **File-scoped namespaces, `using` outside the namespace, system directives
  first** — enforced by `.editorconfig` at build time (`10`).
* **No magic values.** Limits, timeouts, and allow-lists come from configuration
  (`04`), bound through the Options pattern, not literals in the middle of logic.

### Nullability

* Nullable reference types are on solution-wide and the core null-deref warnings
  are **errors** (`.editorconfig`: CS8600/CS8602/CS8618). Do not silence them with
  `!` to make the compiler quiet — the `null!` escape hatch is reserved for
  framework-mandated patterns (e.g. EF-materialised properties), not for skipping
  a real null check.
* Express "may be absent" in the type (`T?`), and handle it; do not paper over it.

### Async

* **Async all the way.** Every I/O-bound path is `async`/`await`; never block with
  `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`.
* **`CancellationToken` is non-negotiable.** Every async feature-service and
  endpoint method accepts a `CancellationToken ct` and **propagates it** to every
  call that takes one (EF Core queries, HTTP calls, provider calls — `06` already
  bakes `ct` into `IAIAnalysisProvider.AnalyzeAsync`). A token accepted and then
  ignored is a bug. (Note: some ASP.NET Core Identity APIs do not accept a token;
  that is the framework's limit, not a license to drop it elsewhere.)
* Name async methods with the `Async` suffix.

### Error handling — `Result` vs exceptions

The model is already established in `SharedKernel` and must be applied
consistently:

* **Expected, domain-level outcomes use `Result` / `Result<T>` + `Error`** — not
  exceptions. Validation failure, not-found, conflict, unauthorized, invalid
  credentials are normal control flow; return `Result.Failure(Error.X(...))` and
  let the endpoint map it via `Error.ToHttpResult()` (`03`, `ErrorResults`).
  Exceptions are not used for ordinary business outcomes.
* **Exceptions are for the genuinely exceptional**: programmer error / broken
  invariants (as `Result` itself throws on misuse), and unrecoverable
  infrastructure failures (DB unreachable, storage I/O error). These bubble to a
  centralised exception handler in the host that converts them to a 500
  problem-details response — never caught-and-swallowed.
* **Never catch `Exception` to hide it.** Catch only what you can handle; let the
  rest reach the host's handler. No empty `catch` blocks; no `catch` that logs and
  returns a success.
* Map `ErrorType` → HTTP status in exactly one place per concern (today
  `ErrorResults`; promote to a shared web kernel via ADR only when a second module
  needs the identical mapping — `10`).

### Validation

* **Validate explicitly at the slice boundary**, before any work. The current
  house pattern is a plain validator returning `Result`
  (`LoginValidator.Validate`) — keep new slices consistent with it.
* Validation rejects malformed input with `Error.Validation`; it is distinct from
  authorization (which returns 401/404) and from domain conflicts (409).
* If a slice's rules grow beyond a handful of simple checks, a declarative
  validation library (e.g. FluentValidation) is an acceptable, ADR-worthy upgrade
  — but it must still surface results as `Result`/`Error` so the boundary contract
  is unchanged. Do not mix two validation styles within a module without recording
  the move.

### Logging & observability (code-level; see `04` for the platform view)

* **Structured logging only** — message templates with named properties, never
  string interpolation into the message, so logs stay queryable. Because CA1848 is
  enforced (analyzers + warnings-as-errors), logging uses the `[LoggerMessage]`
  source generator (compile-time, allocation-free), not direct `_logger.LogX(...)`
  calls.
* **Where log messages live (house convention):** in a dedicated
  `static partial class` of `[LoggerMessage]` `ILogger` extension methods,
  co-located in the same file as the type that emits them and named
  `<Type>Log` — see `GlobalExceptionHandlerLog` for the reference pattern. Call
  sites then read naturally: `_logger.UnhandledException(ex, method, path)`. Use a
  top-level class rather than a class nested inside a primary-constructor type —
  the source generator does not reliably emit implementations for that nesting
  (CS8795). There is **no** app-wide logging registry: a global log class would cut
  across the module boundaries `10` enforces and become a dumping ground. EventIds
  need not be hand-assigned — they are namespaced by the calling logger's
  `ILogger<T>` category, so messages in different types never collide. If a module
  accumulates many shared messages, promote to one `Log` class per module (still
  inside the module), but only when it earns it.
* **Levels mean something**: `Trace`/`Debug` for development detail, `Information`
  for business milestones (upload accepted, job succeeded), `Warning` for handled
  degradations (retryable provider failure), `Error` for unhandled failures.
  `Information`-and-above must carry the correlation id that ties a request to its
  analysis job (`04`).
* **Never log secrets, tokens, passwords, or file contents** (`05`). This is a
  hard rule, checked in review.

### Mapping & boundaries

* **Entities never cross the API boundary** (`03`, `10`). Map entity → response
  DTO explicitly; do not serialise EF entities directly.
* Keep mapping simple and explicit (constructor / projection). A mapping library
  is not warranted at V1 scale; revisit only if mapping volume justifies it.

---

## Object-Oriented Design

* **Encapsulation is real, not nominal.** Objects guard their invariants; avoid
  anaemic types that are pure public setters manipulated from elsewhere. The
  `Result` type modelling its own valid states (a success cannot carry an error)
  is the reference example.
* **Prefer composition over inheritance.** Inheritance is for genuine
  is-a hierarchies (e.g. `BaseEntity` conventions in `SharedKernel`); behaviour
  reuse otherwise comes from composition and injected collaborators, not deep base
  classes.
* **Program to the interface at the seams**, to the concrete type everywhere else.
  Don't introduce an interface for a type that has one implementation and no
  test/extensibility need (see the governing tension above).
* **Keep types cohesive and small.** A feature service does one feature; a class
  that needs "and" to describe it is two classes.

---

## SOLID — applied pragmatically

SOLID is the lens, not a compliance checklist. Each principle, in this codebase:

* **S — Single Responsibility.** The vertical slice already enforces this: one
  service per feature, one reason to change. A service that validates *and* talks
  to three subsystems *and* maps DTOs is fine if that is the one feature; it is
  not fine if those are really separate features sharing a class.
* **O — Open/Closed.** Achieved through the provider seams: adding an
  `IAIAnalysisProvider` (OpenAI, Ollama, …) or an `IFileStorageProvider` (local,
  S3) extends behaviour without modifying consumers (`06`, `07`). Do **not**
  pursue open/closed by building extension points nobody needs yet.
* **L — Liskov Substitution.** Every `IFileStorageProvider` /
  `IAIAnalysisProvider` implementation must honour the contract fully — same
  pre/post-conditions, no "this provider throws where others return empty". The
  `12` provider tests exist to defend this.
* **I — Interface Segregation.** Contracts (`*.Contracts`) expose narrow,
  purpose-built interfaces, not god-interfaces. `IAIAnalysisProvider` is a single
  focused method (`06`); keep new seams equally lean.
* **D — Dependency Inversion.** Feature code depends on abstractions from
  `*.Contracts`, never on concrete infrastructure (`10`'s dependency rules,
  enforced by `Filer.Architecture.Tests`). Concretes are wired only at the
  composition root (`Filer.Api`). This is the principle the whole module layout is
  built to guarantee.

The recurring guardrail: SOLID serves changeability. When "applying a principle"
means adding indirection with no present payoff, that is a violation of ADR-003,
not an application of SOLID.

---

## Design Patterns — endorsed, and deferred

### Endorsed (in use or naturally fitting)

* **Strategy** — the provider abstractions (`IFileStorageProvider`,
  `IAIAnalysisProvider`) are Strategy; provider selection by configuration is the
  intended use (`06`, `07`).
* **Options pattern** — strongly-typed configuration (`JwtOptions` exists);
  all tunables (size/type limits, retry attempts, token lifetimes) bind this way,
  never read as raw literals (`04`).
* **Result / Operation Result** — the `Result`/`Error` types; the standard for
  expected outcomes (above).
* **Factory / builder** — for non-trivial object construction (token creation via
  `ITokenService` is effectively this); and test data builders (`12`).
* **Background worker / producer–consumer** — the durable queue + worker for AI
  analysis (`06`).
* **Adapter** — wrapping a vendor SDK behind the provider contract so vendor shapes
  never leak inward (`06`).

### Deferred until a concrete need (do not add pre-emptively)

* **Mediator / command bus** — explicitly rejected by ADR-004; endpoints call the
  feature service directly. Do not reintroduce.
* **Generic / abstract repository over EF Core** — `DbContext` *is* the unit of
  work and repository. A repository layer is added only if a module gains real
  persistence-ignorance needs; default is to use `DbContext` directly in the
  service (as the codebase does).
* **CQRS, full DDD aggregates, event sourcing** — deferred (`00`, ADR-003); revisit
  only with a justified, documented need via ADR.
* **Microservices / distributed patterns** — out of scope for the modular
  monolith; the module seams preserve the *option* without paying the cost now.

A pattern is introduced because it removes present pain, and its introduction of
note goes in the decision log (`09`). "It's a known pattern" is not, by itself, a
reason to add it.

---

## Static Analysis & Build Enforcement

Quality rules are enforced by the compiler and analyzers so review can focus on
design, not style.

* **Warnings are errors** solution-wide (`Directory.Build.props`) and **code style
  is enforced in build** (`EnforceCodeStyleInBuild`) — already in place (`10`).
* **.NET analyzers are enabled** with `AnalysisLevel` at `latest`. The analysis
  ramp is staged so `main` stays green (`11`): start at the `Recommended` rule set,
  drive the backlog to zero, then ratchet toward `All`, recording any
  project-specific suppressions with a justification comment rather than blanket
  disabling.
* **Nullability diagnostics are errors** (`.editorconfig`).
* **Generated code is exempt** from style/analysis (EF migrations are marked
  `generated_code` — `.editorconfig`), so enforcement never fights the toolchain.
* **Dependency & secret hygiene** (`11`): Dependabot, secret scanning + push
  protection. CI additionally fails on a known-vulnerable package
  (`dotnet list package --vulnerable`) and, once enabled, on a CodeQL alert.

Suppressing a rule is a decision: prefer fixing; if suppressing, scope it as
narrowly as possible (line/member, not project) and say why.

---

## Definition of Done

A change is done — and only then mergeable — when **all** hold. This supersedes
the ad-hoc checklist and is mirrored in the PR template (`11`):

1. **Builds clean** in Release with warnings-as-errors and analyzers; code style
   passes.
2. **Tests per `12` exist and pass**: feature-service unit tests for success and
   every failure `Error`; the slice's security-critical integration tests
   (ownership → 404, auth, upload validation where applicable); architecture tests
   green.
3. **Coverage** meets the module threshold once the gate is active (`12`).
4. **Respects ADRs and boundaries** (`08`, `10`) — no new cross-module
   implementation dependency, infrastructure only behind its abstraction.
5. **Security obligations met** (`05`): ownership checks present, no secret logged,
   problem-details errors leak nothing internal, uploads validated.
6. **`CancellationToken` threaded** through new async paths; no blocking on async.
7. **Observability**: meaningful structured logs at the right levels; new
   background work emits the metrics/correlation `04` requires.
8. **Docs updated** if a canonical fact changed (`project documents/`), and an ADR
   added to `09` for any non-trivial design or pattern decision.
9. **Conventional Commit** title; PR explains the *why* (`11`).

---

## Open Items

* Adopt the analyzer ramp in `Directory.Build.props` and confirm the `Recommended`
  set is green, then schedule the move toward `All`.
* Decide whether a shared web kernel (centralising `ErrorResults`-style mapping and
  problem-details) is warranted once a second module needs it (`10`); record via
  ADR.
* Confirm assertion/mocking library choices (`12`) and pin them centrally.
