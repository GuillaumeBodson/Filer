# /docs/12-testing-strategy.md

# Testing Strategy

## Purpose

Defines how the platform is tested: the kinds of tests, what each kind is
responsible for, what *must* be tested before a change merges, and how coverage
is measured and gated. Where `08` says tests are expected and `11` says CI runs
them, this document defines the bar that makes "tested" mean something concrete.

Related documents: `04-non-functional.md` (maintainability, reliability),
`05-security.md` (ownership, upload rules), `10-solution-structure.md` (project
layout, boundary tests), `11-git-workflow.md` (CI gate). Related decisions:
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

  Until the first owned-resource module (Documents, `08` build order) exists,
  this guarantee is proven against a **temporary, `Testing`-environment-only
  probe**: `Filer.Api/Infrastructure/OwnershipProbeEndpoints.cs` maps a throwaway
  owned resource, and `OwnershipProbeTests` drives the real JWT → `ICurrentUser`
  → `OwnershipGuard` → problem-details chain through it to assert cross-owner →
  404 ahead of any real resource. The probe is scaffolding, not a fixture: it is
  gated to the Testing environment (never shipped). Its **removal trigger is
  defined**: the `Documents: get metadata` slice (`GET /api/v1/documents/{id}`,
  milestone M3, issue **#35**) — the same endpoint the skipped `OwnershipTests`
  waits on. When #35 lands, the probe and `OwnershipProbeTests` are deleted and
  `OwnershipTests` takes the guarantee over against a real resource; deleting the
  probe is an acceptance criterion on #35, so it cannot be closed with the
  scaffold still present.

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
* **Frameworks**: xUnit as the runner; an assertion library
  (e.g. FluentAssertions or `Shouldly`) for readable failures; `NSubstitute` or
  `Moq` for mocks; `Testcontainers` for Postgres; `WebApplicationFactory` for
  integration host bootstrapping; `bUnit` for rendering Razor components in
  `Filer.Ui.Tests`. Versions are pinned centrally in
  `Directory.Packages.props` (`10`).
* **Test data**: build objects with explicit, intention-revealing factories/builders
  rather than sharing mutable fixtures across tests.
* **One module's tests per test project**, mirroring the project-per-module layout
  (`10`).

---

## Coverage

Coverage is a signal, not a target to game. The rules:

* **Line + branch coverage is collected on every CI run** (`coverlet` via
  `dotnet test --collect:"XPlat Code Coverage"`), and a human-readable report is
  produced as a build artifact.
* **Gate (once the suite exists):** the `build-test` check fails if coverage of
  the changed module drops below **80% line / 70% branch**. The gate is on the
  module, so a thin host (`Filer.Api`) does not dilute or inflate the number.
* **Generated and trivial code is excluded**: EF Core migrations
  (`**/Migrations/*.cs`, already marked generated — `.editorconfig`), DTOs/records
  with no logic, and `Program.cs` wiring.
* A high percentage on untested-behaviour does not count: coverage is necessary,
  not sufficient. The per-slice requirements above are the real bar.

> Until the first real test suite lands, the coverage *gate* is staged: CI
> collects and reports coverage immediately, and the failing threshold is enabled
> in the same PR that introduces the first module's tests, so it never blocks an
> empty baseline.

---

## CI Wiring (extends `11`)

The `build-test` job (`.github/workflows/ci.yml`) runs, in order:
`tool restore` → **Kiota drift gate** → `restore` → `build` (Release,
warnings-as-errors) → `test` with coverage collection → coverage report/threshold.
The PostgreSQL 17 service already present in CI backs the integration tests;
`Filer.Architecture.Tests` run in the same pass. The frontend is covered in this
same pass: building `Filer.slnx` compiles the Blazor WASM host (`Filer.Web`) and
the shared RCL (`Filer.Ui`) under warnings-as-errors, and `dotnet test Filer.slnx`
runs the bUnit component tests (`Filer.Ui.Tests`) with the rest. `build-test`
remains the single required status check for branch protection.

The **Kiota drift gate** runs early (it needs only the committed OpenAPI snapshot —
no API, no Postgres) so it fails fast: it regenerates the typed client from
`src/Clients/Filer.ApiClient/openapi/v1.json` and fails the job if the result
differs from the checked-in `Generated/`. This enforces ADR-011 (#126): a contract
change that lands without regenerating the client breaks the build. The regenerate
command and recovery steps live in `src/Clients/Filer.ApiClient/README.md`.

---

## Open Items

* Final choice of assertion and mocking libraries (record as a short ADR in `09`
  once picked, then pin in `Directory.Packages.props`).
* Whether to add a nightly job that runs the full integration suite against a
  matrix of provider configs once more `IAIAnalysisProvider` adapters exist.
* Mutation testing (e.g. Stryker.NET) as a later quality ratchet — deferred, noted
  so it is not forgotten.
* Remove the temporary ownership probe (`OwnershipProbeEndpoints` +
  `OwnershipProbeTests`) when `Documents: get metadata` (issue **#35**, milestone
  M3) lands and the live `OwnershipTests` cover cross-owner → 404 against a real
  owned resource. Tracked as an acceptance criterion on #35.
