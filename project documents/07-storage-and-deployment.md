# /docs/07-storage-and-deployment.md

# Storage & Deployment

## Purpose

Defines how binary files are stored and how the system is packaged and deployed.
Both are infrastructure concerns kept behind abstractions and driven by
configuration (`08`, `04`).

Related documents: `02-data-model.md`, `04-non-functional.md`, `05-security.md`.
Related decisions: ADR-002 (PostgreSQL), ADR-003 (modular monolith).

---

# Storage

## Strategy

* **Metadata** lives in PostgreSQL (`02`).
* **Binary file contents** live in a storage provider, referenced from the
  `Document.StorageKey`. Bytes are never stored in the database.

## Abstraction

```csharp
public interface IFileStorageProvider
{
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct); // returns StorageKey
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct);
}
```

* `StorageKey` is opaque and non-guessable (`05`); the rest of the system treats
  it as a token, not a path.
* No part of the domain assumes a particular storage backend.

## V1 Implementation — Local Filesystem

* Files stored on a local filesystem path mounted as a **Docker volume** (`00`).
* The storage directory is **not** web-exposed; all access flows through the
  authenticated download endpoint (`05`).
* Layout uses a sharded directory scheme derived from the key (e.g. first bytes
  of the key) to avoid huge flat directories.

## Future Implementation — Object Storage

* S3-compatible / Azure Blob implementations of `IFileStorageProvider` (`08`).
* Selected by configuration; no domain changes required.
* Pre-signed download URLs may be introduced but must preserve ownership
  guarantees and use short expiries (`05`).

## Integrity & Lifecycle

* Save and metadata-persist are coordinated so no orphaned blobs or
  half-created documents are exposed (`04`).
* Soft-deleted documents retain their blob until the retention window elapses,
  then the blob is purged alongside the record (`04`).

---

# Deployment

## Principles

* **Docker-first and mandatory** (`00`): every component runs in containers.
* **Infrastructure-agnostic**: no hard dependency on a specific cloud in V1
  (`04`).
* **Configuration-driven**: connection strings, storage paths, JWT keys, and AI
  provider settings come from environment variables / secret store, never baked
  into images (`04`, `05`).

## V1 Topology

A single modular-monolith application (ADR-003) plus its dependencies, composed
locally:

* **api** — the ASP.NET Core application (REST API + hosted background worker for
  V1).
* **db** — PostgreSQL with a persistent volume.
* **storage volume** — host/Docker volume mounted into the api container for
  file blobs.
* **ollama** *(optional)* — self-hosted LLM runtime for the no-egress AI
  provider, behind the Compose `ai` profile so a plain `docker compose up`
  never pulls it (`06`, Privacy & Provider Selection).
* **aspire-dashboard** *(optional)* — local telemetry viewer (ADR-013): the
  standalone Aspire dashboard container as OTLP sink for traces/metrics/logs,
  behind the Compose `observability` profile (same pattern as `ai`). UI on
  `http://localhost:18888`, OTLP/gRPC ingest published on host port 4317 for an
  API run outside Compose. Runs with anonymous auth — a local-only viewer; no
  real environment ships it. The api service points at it unconditionally
  (`Observability__Otlp__Endpoint`); with the profile down, export batches are
  dropped silently and the app is unaffected.

The api container exposes `/health/live` (process up) and `/health/ready`
(PostgreSQL + storage root writable) for orchestrators and uptime checks
(`04`); both are anonymous and outside the versioned API contract (`03`).

The web client (`Filer.Web`, Blazor WebAssembly) is static assets; in dev it is
served by `dotnet run --project src/Clients/Filer.Web` (cross-origin calls to
the API need the CORS policy tracked in #148). It is **not** part of the Compose
topology yet — production hosting (served by the api container vs. a static
host/CDN) is decided when the first deployable frontend milestone ships (open
item).

For V1 the background worker runs as a hosted service inside the api container.
The boundary is kept clean so it can be split into a separate **worker** service
later without code restructuring.

## Scale-Out Topology (Future)

* Multiple stateless **api** replicas behind a load balancer.
* Separately deployable **worker** replicas for AI analysis (`06`).
* Managed PostgreSQL and S3-compatible object storage replacing the local volume.
* A message broker backing the analysis queue (`06`).

No session affinity is required; no component relies on in-process state for
correctness (`04`).

## Configuration & Secrets

| Concern              | Source                                  |
|----------------------|-----------------------------------------|
| DB connection        | Environment variable                    |
| Storage path/backend | Environment variable / config           |
| JWT signing key      | Secret store / environment variable     |
| AI provider + keys   | Environment variable (worker scope only)|

Distinct values per environment; all secrets rotatable without rebuilding images
(`05`).

## Environments

* **Local/dev:** Docker Compose with all services and seeded config.
* **Production (V1):** single-node container deployment with persistent volumes
  and a documented manual backup procedure (PostgreSQL dump + volume snapshot,
  per `04`).
* **Production (SaaS phase):** orchestrated multi-replica deployment with managed
  data services.

## Health & Readiness

* Liveness and readiness endpoints (`04`); readiness verifies DB and storage
  reachability before a replica receives traffic.

---

## Open Questions

* Choice of message broker for the scale-out queue (deferred to `06` future work).
* Container orchestration target for production (Compose vs Kubernetes) at the
  SaaS phase.
* Backup automation and retention specifics for managed services.

---
