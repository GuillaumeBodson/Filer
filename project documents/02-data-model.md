# /docs/02-data-model.md

# Data Model

## Purpose

Defines the core domain entities, their relationships, and persistence
conventions for V1. The model is relational (PostgreSQL) and intentionally
shaped so that the future multi-tenant SaaS evolution requires additive changes,
not rewrites.

Related decisions: PostgreSQL (ADR-002), modular monolith + vertical slices
(ADR-003).

---

## Modeling Principles

* Every user-owned entity carries an explicit `OwnerId` for ownership validation.
* Identifiers are UUIDs (`uuid`), generated application-side, to keep IDs
  non-sequential and merge-friendly across future tenants.
* Timestamps are stored in UTC (`timestamptz`): `CreatedAt`, `UpdatedAt`.
* Soft-delete via `DeletedAt timestamptz NULL` where recoverability matters
  (documents, folders); hard-delete elsewhere.
* Flexible / evolving attributes use `jsonb` rather than schema churn.
* Binary file contents are **not** stored in the database — only metadata and a
  storage key pointing at the file in the storage provider.

---

## Multi-Tenancy Readiness

V1 is single-user, but the schema is tenant-ready:

* A nullable `TenantId uuid` column is included on owned entities now (NULL or a
  default tenant in V1).
* When SaaS arrives, `TenantId` becomes non-null and is enforced via row-level
  security and/or query filters — no structural migration of relationships.
* `OwnerId` (the user) and `TenantId` (the organization) are kept distinct so a
  tenant can later contain many users.

---

## Entities

### User

Authentication and ownership principal. Backed by ASP.NET Core Identity.

| Field            | Type          | Notes                                  |
|------------------|---------------|----------------------------------------|
| Id               | uuid (PK)     | Identity user key                      |
| Email            | text (unique) | Login identifier                       |
| PasswordHash     | text          | Managed by ASP.NET Identity            |
| TenantId         | uuid NULL     | Reserved for SaaS                      |
| CreatedAt        | timestamptz   |                                        |
| UpdatedAt        | timestamptz   |                                        |

### Document

The central entity. Holds metadata only; bytes live in the storage provider.

| Field            | Type          | Notes                                              |
|------------------|---------------|----------------------------------------------------|
| Id               | uuid (PK)     |                                                    |
| OwnerId          | uuid (FK→User)| Ownership validation                               |
| TenantId         | uuid NULL     | Reserved for SaaS                                  |
| FolderId         | uuid NULL (FK→Folder) | NULL = root / unfiled                      |
| FileName         | text          | Original file name                                 |
| ContentType      | text          | MIME type                                          |
| SizeBytes        | bigint        |                                                    |
| StorageKey       | text          | Opaque key resolved by `IFileStorageProvider`      |
| ContentHash      | text          | SHA-256 of bytes; drives duplicate detection       |
| Status           | text/enum     | `Uploaded`, `Analyzing`, `Ready`, `Failed`         |
| Metadata         | jsonb         | Flexible extra attributes                          |
| CreatedAt        | timestamptz   |                                                    |
| UpdatedAt        | timestamptz   |                                                    |
| DeletedAt        | timestamptz NULL | Soft delete                                     |

Indexes: `(OwnerId)`, `(OwnerId, FolderId)`, `(OwnerId, ContentHash)` for
duplicate lookup, partial index `WHERE DeletedAt IS NULL`.

### Folder

Hierarchical organization. A folder belongs to one owner and may have a parent.

| Field            | Type          | Notes                              |
|------------------|---------------|------------------------------------|
| Id               | uuid (PK)     |                                    |
| OwnerId          | uuid (FK→User)|                                    |
| TenantId         | uuid NULL     | Reserved for SaaS                  |
| ParentId         | uuid NULL (FK→Folder) | NULL = top level           |
| Name             | text          | Unique among siblings per owner    |
| CreatedAt        | timestamptz   |                                    |
| UpdatedAt        | timestamptz   |                                    |
| DeletedAt        | timestamptz NULL |                                 |

Constraint: unique `(OwnerId, ParentId, Name)`. Cycles must be prevented in
application logic. Decision pending: maximum nesting depth (see open questions).
Non-empty deletion semantics are resolved in ADR-007 (`09`): reject with `409` by
default, opt-in cascade soft-delete via `?recursive=true`.

### Tag

A label owned by a user. Free-form in V1, per-owner scope.

| Field            | Type          | Notes                          |
|------------------|---------------|--------------------------------|
| Id               | uuid (PK)     |                                |
| OwnerId          | uuid (FK→User)|                                |
| TenantId         | uuid NULL     | Reserved for SaaS              |
| Name             | text          | Unique per owner               |
| CreatedAt        | timestamptz   |                                |

Constraint: unique `(OwnerId, Name)`.

### DocumentTag (join)

Many-to-many between Document and Tag.

| Field       | Type                 | Notes                         |
|-------------|----------------------|-------------------------------|
| DocumentId  | uuid (FK→Document)   | PK part                       |
| TagId       | uuid (FK→Tag)        | PK part                       |
| Source      | text/enum            | `User` or `AiSuggested`       |
| CreatedAt   | timestamptz          |                               |

Composite PK `(DocumentId, TagId)`.

### AnalysisJob

Tracks asynchronous AI processing of a document (see upload flow in `01`).

| Field          | Type          | Notes                                          |
|----------------|---------------|------------------------------------------------|
| Id             | uuid (PK)     |                                                |
| DocumentId     | uuid (FK→Document) |                                           |
| Status         | text/enum     | `Queued`, `Running`, `Succeeded`, `Failed`, `Cancelled` |
| Provider       | text          | Which `IAIAnalysisProvider` ran it; stamped at claim on every attempt, NULL until the first run starts |
| AttemptCount   | int           | For retry/backoff                              |
| NextAttemptAt  | timestamptz NULL | Earliest next attempt after a retryable failure; the claim query skips rows scheduled in the future |
| Error          | text NULL     | Last failure detail (worker-internal; never exposed to clients) |
| Result         | jsonb NULL    | Suggested folders/tags                         |
| CreatedAt      | timestamptz   |                                                |
| UpdatedAt      | timestamptz   |                                                |
| StartedAt      | timestamptz NULL |                                             |
| CompletedAt    | timestamptz NULL |                                             |

The `Result` JSONB holds AI suggestions; suggestions are applied to the document
only after user confirmation (per product philosophy in `01`). Its shape is a
durable contract owned by a single serializer (`AnalysisJobResultJson` in the
AiAnalysis Contracts project): camelCase property names, absent fields tolerated
on read (rows persisted before a field was dropped — e.g. `duplicateSignals`,
#164 — stay readable). The worker writes it and the Documents module reads it
through that one class — no parallel serializers.

---

## Relationships Summary

* User 1—* Document (owner)
* User 1—* Folder (owner)
* User 1—* Tag (owner)
* Folder 1—* Document
* Folder 0..1—* Folder (self-referencing hierarchy)
* Document *—* Tag (via DocumentTag)
* Document 1—* AnalysisJob

---

## Duplicate Detection

Primary mechanism: `ContentHash` (SHA-256) computed on upload. A new upload whose
hash matches an existing non-deleted document owned by the same user is flagged
as a potential duplicate. AI-based near-duplicate detection (semantic similarity)
is a later enhancement layered on top, not a V1 requirement.

---

## PostgreSQL-Specific Notes

* **JSONB** for `Document.Metadata` and `AnalysisJob.Result` — flexible without
  migrations.
* **Full-text search** (V1): a stored generated `tsvector` column
  (`Documents.SearchVector`, #57) with a GIN index backs `/api/v1/search`:
  `setweight(to_tsvector('simple', translate("FileName", '._-', '   ')), 'A')
  || setweight(jsonb_to_tsvector('simple', coalesce("Metadata", '{}'), '["string"]'), 'B')`.
  The `'simple'` regconfig keeps tokenization language-neutral (file names are
  multilingual; stemming is compensated by last-token prefix matching, `03`);
  `translate` splits `.`/`_`/`-` so name parts become individual lexemes; the
  weights rank file-name matches above metadata matches. The column is a shadow
  property of the Documents module — the entity never sees it (ADR-017).
* **pgvector** (future): an `embedding vector(N)` column on Document (or a
  separate `DocumentEmbedding` table) is reserved for semantic search. Not
  created in V1.
* **EF Core / Npgsql** is the access path; persistence concerns stay isolated
  per ADR-003.

---

## Open Questions (to resolve before/with implementation)

* Maximum folder nesting depth (or unlimited with cycle prevention only).
* Whether a document may belong to multiple folders (current model: single
  folder). If yes, replace `FolderId` with a join table.
* Support