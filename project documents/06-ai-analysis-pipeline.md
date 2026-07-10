# /docs/06-ai-analysis-pipeline.md

# AI Analysis Pipeline

## Purpose

Defines how AI-assisted document analysis works: the asynchronous job lifecycle,
the provider abstraction, reliability requirements, and how suggestions reach the
user. AI is an assistive feature, never a blocking dependency (`01`, `08`).

Related documents: `02-data-model.md` (AnalysisJob), `03-api-specification.md`
(analysis endpoints), `04-non-functional.md`, `05-security.md`. Related decisions:
ADR-002, ADR-003, ADR-008.

---

## Principles

* **Asynchronous always.** Analysis never runs inside an upload request (`08`).
* **Isolated from the API.** Processing happens in a background worker, decoupled
  from request handling.
* **Provider-agnostic.** Concrete AI services sit behind `IAIAnalysisProvider`;
  domain logic never depends on a specific vendor.
* **Advisory.** Results are suggestions; nothing is applied without user
  confirmation.
* **Privacy-respecting.** A local/no-egress provider must be supported so
  sensitive documents need not leave the deployment (`05`).

---

## Capabilities (V1)

For an uploaded document the pipeline produces:

* **Folder suggestion** — a recommended folder (existing or proposed new name).
* **Tag suggestions** — zero or more recommended tags.
* **Duplicate findings** — surfacing content-hash matches and, later, semantic
  near-duplicates.

Out of V1 scope (future): summarization, semantic search indexing, natural-language
querying, AI chat over documents — tracked in `14-roadmap.md` (RM-04).

---

## Job Lifecycle

Backed by the `AnalysisJob` entity (`02`). State transitions:

```
Queued ──▶ Running ──▶ Succeeded
   │           │
   │           ├──▶ Failed (retryable ──▶ back to Queued, up to attempt limit)
   │           │
   │           └──▶ Failed (terminal, after attempt limit)
   └──▶ Cancelled
```

1. **Queued** — created at upload time; the upload response returns immediately
   with the job id (`03`).
2. **Running** — a worker claims the job, sets `StartedAt`, increments
   `AttemptCount`.
3. **Succeeded** — results written to `AnalysisJob.Result` (JSONB); document
   `Status` moves to `Ready`.
4. **Failed** — `Error` recorded. Retryable failures return to `Queued` with
   backoff until the configured attempt limit, then become terminal `Failed`.
5. **Cancelled** — job cancelled (e.g. document deleted before processing).

---

## Provider Abstraction

```csharp
public interface IAIAnalysisProvider
{
    Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken);
}
```

* The request carries the content (or a reference the provider can read) plus
  context such as existing folders and tags for better suggestions.
* The result is a provider-neutral DTO (suggested folder, suggested tags,
  duplicate signals, confidence scores) — never a vendor-specific shape.
* Implementations are selected by configuration. Candidates: OpenAI, Azure
  OpenAI, Ollama, local LLM (`00`/`08`). A zero-footprint `Fake` provider
  (deterministic canned suggestions, no model, no network) ships alongside them
  for development and tests on machines that cannot host a local LLM.
* Provider credentials live with the worker only and never reach clients (`05`).

---

## Worker & Queue

* A background worker (hosted service in the modular monolith for V1, separately
  deployable later — ADR-003) consumes queued jobs.
* The queue is durable so a crash loses no work; for V1 the persisted
  `AnalysisJob` table itself acts as the work source (poll/claim with row
  locking). **RabbitMQ is adopted for dispatch after the upload pipeline
  milestone (ADR-008):** the table remains the durable outbox and job-state
  record; the broker replaces polling as the wake-up signal, with the polling
  loop retained as a fallback sweeper.
* Workers are horizontally scalable independent of the API (`04`).
* Job claiming must be safe under concurrency (no two workers run the same job).

---

## Reliability Requirements

Per `08` (background jobs must support these):

* **Retries** with exponential backoff for transient failures (provider timeout,
  rate limit), capped by a configurable attempt limit.
* **Cancellation** via `CancellationToken`, honored mid-flight; deleting a
  document cancels its in-flight/queued jobs.
* **Idempotency** — re-running a job for the same document produces a consistent
  result and does not create duplicate suggestions.
* **Observability** — each job emits structured logs and metrics (queue depth,
  duration, success/failure), correlated to the originating upload (`04`).

---

## Applying Suggestions

* Suggestions live in `AnalysisJob.Result` and are exposed via
  `GET /documents/{id}/analysis` (`03`).
* They are applied to the document only when the user confirms, via
  `POST /documents/{id}/analysis/apply`.
* Applying tags records `Source = AiSuggested` on the `DocumentTag` rows (`02`),
  preserving the distinction between user- and AI-originated organization.
* A user may accept all, some, or none of the suggestions.

---

## Failure Handling & UX

* A failed analysis never blocks document usage: the document remains accessible;
  only suggestions are absent.
* Terminal-failed jobs are surfaced to the user (analysis unavailable) and may be
  manually retriggered (reserved endpoint; not required for V1).

---

## Privacy & Provider Selection

* For personal/sensitive use, the default provider should favor **local
  processing** (e.g. Ollama) so content does not leave the deployment (`05`).
* The active provider is environment configuration; switching providers requires
  no domain changes.
* The shipped no-egress option is the **Ollama adapter** (`OllamaAnalysisProvider`),
  a typed-HttpClient implementation that calls a self-hosted Ollama runtime so
  document content never leaves the deployment (`05`). Its runtime ships as the
  `ollama` Docker Compose service behind the `ai` profile, so a plain
  `docker compose up` never pulls it; select it with `AiAnalysis__Provider=Ollama`.

---

## Future Evolution

* ~~Dedicated message broker for the queue at scale.~~ Decided: RabbitMQ for
  dispatch over the Postgres outbox (ADR-008), sequenced after the upload
  pipeline milestone.
* Embedding generation feeding pgvector for semantic search (`02`) — the
  prerequisite for AI chat over documents (`14`, RM-04).
* Summarization and AI chat capabilities layer on the same provider abstraction;
  intent and sequencing in `14-roadmap.md` (RM-04). Text quality for these
  depends on OCR (RM-01).

---
