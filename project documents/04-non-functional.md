# /docs/04-non-functional.md

# Non-Functional Requirements

## Purpose

Defines the quality attributes and operational constraints the platform must
meet, separate from its features. These requirements apply to V1 unless marked
otherwise.

Related documents: `02-data-model.md`, `03-api-specification.md`,
`05-security.md`. Related decisions: ADR-002 (PostgreSQL), ADR-003 (modular
monolith + vertical slices).

---

## File Handling

### Supported file types (V1)

Allow a controlled list rather than "anything":

* PDF (`application/pdf`)
* Images: PNG, JPEG, WebP
* Office documents: `.docx`, `.xlsx`, `.pptx`
* Plain text and Markdown (`text/plain`, `text/markdown`)

Type is validated by both declared `Content-Type` **and** content sniffing
(magic bytes); mismatches are rejected (415). The allowed list is configuration,
not hardcoded, so it can expand without code changes.

### Size limits (V1)

* Maximum single-file upload: **50 MB** (configurable).
* Requests exceeding the limit return `413 Payload Too Large`.
* Per-user storage quota: not enforced in V1 (single user); a quota hook is
  reserved for the SaaS phase.

---

## Performance

Targets for typical V1 load (single user, modest library):

* Synchronous API reads (list, get metadata): p95 < 300 ms.
* Upload request (excluding AI analysis): p95 < 2 s for a 50 MB file on local
  storage.
* AI analysis is asynchronous and has **no** request-time budget; it must not
  block the upload response.
* Full-text search: p95 < 500 ms on the V1 library scale.

These are guideline SLOs to design against, not contractual SLAs.

---

## Scalability

* V1 runs as a single deployable modular monolith (ADR-003) in Docker.
* The design must scale **out** later without rewrites: stateless API processes
  behind a load balancer, shared PostgreSQL, shared object storage.
* Background analysis workers must be horizontally scalable independently of the
  API.
* No in-process state may be assumed for correctness (session affinity is not
  required).

---

## Availability & Reliability

* V1 target: best-effort, single-node; no formal uptime SLA.
* AI analysis jobs must be durable: a worker crash must not lose queued work
  (jobs persisted in `AnalysisJob`, see `02`).
* Background jobs must support retries with backoff, cancellation, and a
  terminal `Failed` state after a configurable attempt limit.
* Uploads are atomic from the user's perspective: if metadata persistence fails
  after bytes are stored, the orphaned blob is cleaned up (or vice versa) — no
  half-created documents are exposed.

---

## Data Integrity & Retention

* Documents and folders use soft-delete (`DeletedAt`); a configurable retention
  window precedes permanent purge (default: 30 days).
* `ContentHash` (SHA-256) guarantees duplicate detection is content-based, not
  name-based.
* Database backups: daily for the SaaS phase; for V1, document the manual
  backup procedure (PostgreSQL dump + storage volume snapshot).

---

## Observability

Per `08` (background jobs must be observable):

* **Structured logging** (JSON) across API and workers, with correlation IDs
  that tie an upload request to its analysis job.
* **Metrics:** request latency/error rates, upload throughput, analysis queue
  depth, job success/failure counts.
* **Tracing:** distributed tracing spanning request → queued job → worker
  execution.
* **Health endpoints:** liveness and readiness (readiness checks DB and storage
  reachability).

Tooling choice (e.g. OpenTelemetry) is an implementation detail; the requirement
is that the data is emitted in an interoperable form.

---

## Portability & Deployment

* Docker-first: the entire stack (API, worker, PostgreSQL, storage volume) runs
  via container orchestration locally and in any target environment.
* No dependency on a specific cloud provider in V1 (infrastructure-agnostic
  principle). Cloud-managed equivalents (object storage, managed PostgreSQL) are
  adopted only in later phases.
* All environment-specific values (connection strings, storage paths, JWT keys,
  AI provider config) are supplied via configuration/environment variables, not
  baked into images.

---

## Maintainability

* Code organized by vertical slice with clear module boundaries (ADR-003).
* Infrastructure behind abstractions (`IFileStorageProvider`,
  `IAIAnalysisProvider`) so implementations are swappable.
* Automated tests expected at the slice/module level; AI provider and storage
  are mockable via their interfaces.

---

## Internationalization

* Store and serve timestamps in UTC; localize at the client.
* Text storage is UTF-8 end to end.
* UI localization is not a V1 requirement but must not be precluded by the API
  (no locale-baked response strings beyond the standard error `title`).

---

## Compliance (Forward-Looking)

Not enforced in V1 (single personal user) but anticipated for SaaS:

* Per-tenant data isolation (see multi-tenancy readiness in `02`).
* Data export and account/data deletion ("right to be forgotten").
* Audit trail for sensitive actions.

These are noted so V1 choices do not make them expensive later.

---
