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
* **rabbitmq** *(optional)* — broker dispatch for background jobs (ADR-008,
  #75), behind the Compose `mq` profile: a plain `docker compose up` keeps the
  default Db dispatch. Opt in with `docker compose --profile mq up` **and**
  `BackgroundJobs__Queue=RabbitMq` on the api service; connection settings come
  from env (`05`), the compose defaults being the container's dev credentials.
  Management UI on `http://localhost:15672` (dev only).

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
* **Production (V1):** single self-hosted node — see below.
* **Production (SaaS phase):** orchestrated multi-replica deployment with managed
  data services.

## Production (V1) — self-hosted single node

Resolved by **ADR-018**. The assets live in `deploy/` (production compose, `.env`
template, backup script, runbook); this section states the contract, the runbook
states the procedure.

* **The deployable artifact is a published image**, built once by CI and pushed
  to a container registry. The node never builds: it pulls a tag. The running
  version is a **pinned tag** in the host's `.env` — no floating `latest` is
  published — so that file records what is deployed and rollback is a re-pin.
* **The orchestration is delivered with the image, from the same commit.** The
  compose file on the node is written by the pipeline out of the revision the
  image was built from; it is never edited in place. Image and composition are
  two halves of one deployment, so "what is deployed" stays a single revision
  and a rollback restores both. Left to be copied by hand, the composition
  drifts from the repository silently — nothing compares them and the pipeline
  still reports success. The host's `.env` is the deliberate exception: it holds
  the secrets and the image pin, and no deploy overwrites it.
* **The node supplies persistence, not application state**: bind mounts for the
  PostgreSQL data directory and the blob root, and a separate destination for
  backups. The blob root must be owned by the container's UID; the readiness
  probe fails otherwise, which is the intended signal.
* **PostgreSQL publishes no port.** It is reachable only over the Compose
  network. The api publishes on the loopback interface alone; anything wider goes
  through a reverse proxy on the host, because **Docker writes its iptables rules
  ahead of the host firewall** — a port published on `0.0.0.0` is reachable from
  the LAN even when the firewall is configured to deny it.
* **The AI runtime runs natively on the host**, not in the Compose topology: one
  runtime per GPU. Reaching it from a container needs two settings, not one — see
  `06`, Privacy & Provider Selection.
* **Deployment is triggered by a release tag** and delivered over the node's
  existing private-network membership; the node exposes nothing to the internet
  (`11`). Migrations apply at container startup, so a release is the unit of both
  deployment and rollback.
* **Backups follow a fixed order — database dump first, blobs second.** A
  document created between the two leaves an orphaned blob, which is harmless;
  the reverse order produces a dump referencing a `StorageKey` whose bytes were
  never copied, which is a silently broken restore (`04`). The backup runs
  unattended on a systemd timer that survives reboots, and reports through a
  dead-man's-switch ping: the monitoring service alerts when no ping arrives,
  which is the only signal that also covers a timer that stopped firing.

## Health & Readiness

* Liveness and readiness endpoints (`04`); readiness verifies DB and storage
  reachability before a replica receives traffic.
* The api image carries a container `HEALTHCHECK` against `/health/ready`, so an
  orchestrator can distinguish a started process from a serving one — without it
  a container looping on a failed migration reports as up. Deployment waits on
  that health state rather than on the exit status of the start command.

---

## Open Questions

* Choice of message broker for the scale-out queue (deferred to `06` future work).
* Container orchestration target for production (Compose vs Kubernetes) at the
  SaaS phase.
* Backup automation and retention specifics for managed services.
* Off-node backup destination for the V1 deployment: the current backup target is
  a second disk in the same chassis, which protects against neither theft, fire,
  nor a power event that takes the disks together (OPS-M2).
* Production observability sink: the api exports OTLP unconditionally (ADR-013),
  but the Aspire dashboard is a local-only viewer that no real environment ships.
  Where production telemetry lands is undecided (OPS-M2).

---
