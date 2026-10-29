# /docs/12-testing-strategy.md

# Testing Strategy

## Purpose

Defines how the platform is tested: the kinds of tests, what each kind is
responsible for, what *must* be tested before a change merges, and how coverage
is measured and reported. Where `CLAUDE.md` says tests are expected and `11` says CI runs
them, this document defines the bar that makes "tested" mean something concrete.

Related documents: `04-non-functional.md` (maintainability, reliability),
`05-security.md` (ownership, upload rules), `10-solution-structure.md` (project
layout, boundary tests), `11-git-workflow.md` (CI). Related decisions:
ADR-003 (modular monolith + vertical slices), ADR-004 (plain feature services).

---

## Principles

* **Tests are part of the change, not a follow-up.** A slice is not done until it
  ships with the tests this document requires. "Add tests later" is not a state
  `main` is allowed to be in.
* **Test behaviour, not implementation.** Assert on observable outcomes (returned
  `Result`, HTTP status, persisted state), not on private internals. Tests must
  survive a refactor that preserves behaviour (`13`).
* **The plain feature service is the primary unit.** ADR-004's "no mediator, plain
  injected service" choice exists partly so a slice is unit-testable without the
  web stack. Exploit that: most logic is covered by fast service tests.
* **Real Postgres for integration, not in-memory.** The EF Core in-memory and
  SQLite providers do not behave like Npgsql/PostgreSQL (JSONB, concurrency, SQL
  semantics). Integration tests run against a real Postgres via Testcontainers,
  mirroring `docker-compose.yml` and CI (`11`).
* **Determinism.** No test depends on wall-clock time, machine timezone, network,
  or execution order. Time comes through `IClock` (`SharedKernel`); inject a fixed
  clock in tests.

---

## The Test Pyramid

| Layer | Project | Scope | Speed | Bulk of the suite |
|-------|---------|-------|-------|-------------------|
| Unit | `Filer.Modules.*.Tests` | One feature service / domain rule in isolation; collaborators mocked through their interfaces | Milliseconds | Yes — the broad base |
| Integration | `Filer.IntegrationTests` | A slice end to end through the real HTTP pipeline + real Postgres (Testcontainers); auth, validation, ownership, persistence | Seconds | A focused middle |
| Architecture | `Filer.Architecture.Tests` | Module boundary and dependency rules as executable assertions (`10`) | Milliseconds | A small, complete guard |
| Component (frontend) | `Filer.Ui.Tests` | Client auth plumbing as plain unit tests; Razor components rendered with bUnit (markup, parameters, UI states) | Milliseconds | Small — grows with the frontend |

There is intentionally no broad end-to-end UI layer in V1; the API is the
contract and is covered at the integration layer. Push test weight **down** the
pyramid: if a behaviour can be proven by a unit test, do not promote it to an
integration test.

---

## What Must Be Tested (Definition of "Tested" per slice)

Every feature slice merges with, at minimum:

* **Unit tests for the feature service** covering the success path and **every**
  failure `Error` it can return (validation, not-found, conflict, unauthorized).
  The `Result` failure branches are behaviour, not edge cases — cover them.
* **Validation tests**: each rule rejects what it should and accepts a valid
  request. Today validators are plain static methods returning `Result`
  (`LoginValidator`); test them directly.
* **One integration test per externally observable outcome** of the slice: the
  happy path plus the security-critical paths below.
* **Ownership / authorization**: for any protected resource access, a test proving
  cross-owner access returns **404, not 403** (`05`). This is mandatory wherever
  ownership applies — it is the single most important behavioural guarantee in the
  system and must never regress.


### Security-critical behaviours that always require a test

These come from `05`/`04` and are not optional once the relevant slice exists:

* Cross-owner access returns 404 (not 403, not 200).
* Upload type allow-list: a disallowed type and a content/extension mismatch are
  rejected (415); content sniffing, not just declared MIME, is exercised.
* Upload size over the configured limit returns 413.
* Unauthenticated access to a protected endpoint returns 401.
* Error responses use the problem-details shape and leak no stack trace (`05`).

---

## Background Jobs & AI Pipeline

The analysis pipeline (`06`) has reliability requirements that are themselves
test obligations:

* **Retry / backoff**: a transient failure re-queues up to the attempt limit, then
  becomes terminal `Failed` — assert the state machine, not the timing.
* **Cancellation**: a `CancellationToken` cancelled mid-flight stops work and
  leaves the job `Cancelled`; deleting a document cancels its jobs.
* **Idempotency**: re-running a job for the same document does not create duplicate
  suggestions.
* **Provider isolation**: tests run against a fake `IAIAnalysisProvider`; no test
  calls a real model. The abstraction (`06`) exists so this is trivial.
* **Concurrency**: two workers must not run the same job — cover the claim path.

---

## Test Doubles — what to mock, what to keep real

* **Mock at the seam, through the interface.** `IFileStorageProvider`,
  `IAIAnalysisProvider`, `IBackgroundJobQueue`, `IClock`, `ICurrentUser` are the
  designed seams (`07`, `06`, `10`, `SharedKernel`). Mock these in unit tests.
* **Do not mock what you own and can run cheaply.** Prefer a real `DbContext`
  against Testcontainers Postgres in integration tests over a mocked repository.
* **Never mock the type under test**, and never assert on mock call-counts when an
  outcome assertion would do — that couples the test to implementation (`13`).

---

## Conventions

* **Naming**: `MethodOrFeature_StateUnderTest_ExpectedOutcome`, e.g.
  `HandleAsync_WhenEmailUnknown_ReturnsUnauthorized`. One logical assertion per
  test where practical; arrange/act/assert structure.
* **Frameworks** (recorded as ADR-015): xUnit v3 as the runner;
  `FluentAssertions` for readable failures; `Moq` for mocks at the designed
  seams; `Testcontainers` for Postgres; `WebApplicationFactory` for
  integration host bootstrapping; `bUnit` for rendering Razor components in
  `Filer.Ui.Tests`. Versions are pinned centrally in
  `Directory.Packages.props` (`10`).
* **Test data**: build objects with explicit, intention-revealing factories/builders
  rather than sharing mutable fixtures across tests.
* **One module's tests per test project**, mirroring the project-per-module layout
  (`10`).

---

## Coverage

Coverage is collected and **reported** on every CI run — it is a signal, never a
gate (ADR-021). The rules:

* **Line + branch coverage is collected on every run**
  (`Microsoft.Testing.Extensions.CodeCoverage` via `dotnet test --coverage
  --coverage-output-format cobertura`), and a human-readable report is produced
  as a build artifact (`coverage-report`, ReportGenerator HTML) plus a Markdown
  summary on the job page. The collector changed with xunit.v3 4.0.0
  (ADR-015 update): coverlet's was a VSTest data collector, and MTP has no
  VSTest bridge. The format is still Cobertura, so the report is unaffected.
* **No threshold.** A percentage over untested behaviour proves nothing, and a
  threshold drives tests toward lines rather than outcomes. The per-slice
  requirements above are the bar, checked in review (PR template).
* **Use the report to find gaps**: a slice whose failure `Error`s show as
  uncovered branches is missing the tests this document requires.
* **Generated and trivial code is excluded** from the report: EF Core migrations
  (`-*.Migrations.*` class filter), `Program.cs` wiring and source-generated files
  (file filters).

---

## CI

The pipeline — steps, order, services, required checks — is described once, in
`11-git-workflow.md` ("Continuous Integration"). From this document's point of
view: `dotnet test Filer.slnx` runs every layer of the pyramid in one pass
(unit, integration against the PostgreSQL service, architecture, bUnit), the
Kiota drift gate runs before the build so a contract change that lands without
regenerating the client fails fast (ADR-011), and coverage is reported as above.

---

## Open Items

Tracked as issues: #313 (mutation testing with Stryker.NET), #314 (nightly run
against a matrix of provider configurations).
