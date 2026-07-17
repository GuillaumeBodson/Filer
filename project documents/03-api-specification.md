# /docs/03-api-specification.md

# API Specification

## Purpose

Defines the REST API contract for V1: conventions, authentication, resources,
and endpoints. The API is the central entry point (API-first); web, desktop, and
mobile clients all consume it identically.

Related decisions: PostgreSQL (ADR-002), modular monolith + vertical slices
(ADR-003). Entities referenced here are defined in `02-data-model.md`.

---

## Conventions

* **Base path / versioning:** all routes are versioned, e.g. `/api/v1/...`.
* **Format:** JSON request and response bodies; `application/json`. File upload
  uses `multipart/form-data`; download returns the binary stream.
* **IDs:** UUIDs as strings.
* **Timestamps:** ISO 8601 UTC.
* **DTOs at boundaries:** requests and responses use explicit DTOs; entities are
  never exposed directly.
* **Validation:** requests are validated explicitly; invalid input returns 400
  with the standard error shape.
* **Pagination:** list endpoints accept `?page=` and `?pageSize=` and return a
  paged envelope (`items`, `page`, `pageSize`, `totalCount`).
* **Ownership:** every request is scoped to the authenticated user; accessing
  another user's resource returns 404 (not 403) to avoid leaking existence.

---

## Authentication

ASP.NET Core Identity with JWT bearer tokens (email/password). Clients send
`Authorization: Bearer <token>` on every protected request.

| Method | Route                      | Description                          | Auth |
|--------|----------------------------|--------------------------------------|------|
| POST   | `/api/v1/auth/register`    | Create account (email/password)      | No   |
| POST   | `/api/v1/auth/login`       | Obtain access + refresh tokens       | No   |
| POST   | `/api/v1/auth/refresh`     | Exchange refresh token for new access| No   |
| POST   | `/api/v1/auth/logout`      | Revoke refresh token                 | Yes  |
| GET    | `/api/v1/auth/me`          | Current user profile                 | Yes  |

---

## Standard Error Shape

All errors return a consistent body (RFC 7807-style problem details):

```json
{
  "type": "https://docs/errors/file_name_invalid",
  "title": "Validation failed",
  "status": 400,
  "detail": "FileName is required.",
  "code": "file_name_invalid",
  "errors": { "fileName": ["required"] }
}
```

Contract (#169): `title` is a short human-readable summary derived from the error
category (safe to display as a headline); the machine-readable error code
(snake_case, e.g. `invalid_credentials`) lives in the `code` extension member —
clients branch on `code`, never on `title` or `detail`. `type` is the code's
documentation URI.

Common statuses: 400 validation, 401 unauthenticated, 404 not found / not owned,
409 conflict (e.g. duplicate), 413 payload too large, 415 unsupported media type,
500 server error.

---

## Documents

| Method | Route                                   | Description                                   |
|--------|-----------------------------------------|-----------------------------------------------|
| GET    | `/api/v1/documents`                     | List owned documents (filter, paginate)       |
| GET    | `/api/v1/documents/{id}`                | Get document metadata                         |
| POST   | `/api/v1/documents`                     | Upload a document (`multipart/form-data`)     |
| GET    | `/api/v1/documents/{id}/content`        | Download the binary content                   |
| PATCH  | `/api/v1/documents/{id}`                | Update metadata (rename, move folder)         |
| DELETE | `/api/v1/documents/{id}`                | Soft-delete a document                        |
| GET    | `/api/v1/documents/{id}/analysis`       | Get AI analysis job status + result           |
| POST   | `/api/v1/documents/{id}/analysis/apply` | Apply confirmed AI suggestions                |

List filters: `?folderId=`, `?tagId=`, `?q=` (full-text), plus pagination.

Apply semantics (V1, see `06`): the request confirms a subset of the *stored*
suggestions; a tag name the user has not created yet is rejected with `400`
(`suggested_tag_not_created`), and a suggestion proposing a *new* folder cannot
be applied (`400 proposed_folder_not_supported`) — only suggestions resolving to
an existing folder can. No completed analysis → `404 analysis_not_found`.

### Upload behavior

Follows the async flow in `01`/`08`:

1. Validate file (type, size).
2. Persist bytes via `IFileStorageProvider`; compute `ContentHash`.
3. If hash matches an existing owned document, respond `409 Conflict` with the
   existing document reference (client decides whether to proceed).
4. Persist `Document` metadata with status `Uploaded`.
5. Queue an `AnalysisJob` (status `Queued`); return `201 Created` immediately.
6. Background worker runs analysis asynchronously; suggestions land in the job
   `Result` and are applied only after the client calls the `analysis/apply`
   endpoint.

Upload responds `201` with the created document metadata and the analysis job id;
it never blocks on AI processing.

---

## Folders

| Method | Route                            | Description                        |
|--------|----------------------------------|------------------------------------|
| GET    | `/api/v1/folders`                | List owned folders (tree or flat)  |
| GET    | `/api/v1/folders/{id}`           | Get a folder                       |
| POST   | `/api/v1/folders`                | Create folder (optional `parentId`)|
| PATCH  | `/api/v1/folders/{id}`           | Rename or move folder              |
| DELETE | `/api/v1/folders/{id}`           | Soft-delete folder                 |

Listing: `?view=tree|flat`, default `flat`; invalid value → `400`.

Deleting a non-empty folder: **reject with `409` by default; opt-in cascade
soft-delete via `?recursive=true`** (no silent reparenting). Resolved in
ADR-007 (`09`).

---

## Tags

| Method | Route                                  | Description                  |
|--------|----------------------------------------|------------------------------|
| GET    | `/api/v1/tags`                         | List owned tags              |
| POST   | `/api/v1/tags`                         | Create a tag                 |
| PATCH  | `/api/v1/tags/{id}`                    | Rename a tag                 |
| DELETE | `/api/v1/tags/{id}`                    | Delete a tag                 |
| GET    | `/api/v1/documents/{id}/tags`          | Get a document's tag set (with `source`) |
| PUT    | `/api/v1/documents/{id}/tags`          | Replace a document's tag set |
| POST   | `/api/v1/documents/{id}/tags/{tagId}`  | Add a tag to a document      |
| DELETE | `/api/v1/documents/{id}/tags/{tagId}`  | Remove a tag from a document |
| POST   | `/api/v1/documents/tags/batch`         | Bulk add/remove tags (selected docs or folder) |

Replace semantics: `PUT /documents/{id}/tags` manages only `Source=User`
associations. `AiSuggested` rows are preserved on replace, unless their tag is
included in the new set — the existing row is then promoted to `Source=User`
(composite PK `(DocumentId, TagId)`, `02`). AI suggestions are removed only via
explicit `DELETE`.

---

## Search

| Method | Route                  | Description                                      |
|--------|------------------------|--------------------------------------------------|
| GET    | `/api/v1/search`       | Full-text search across owned documents (`?q=`)  |

V1 search is backed by PostgreSQL full-text (`tsvector`/GIN). Semantic search
(pgvector) is a later addition exposed under the same endpoint or a sibling
route; the contract is designed to absorb it without breaking clients.

---

## Endpoint Summary by Module

Maps to the modular-monolith modules (ADR-003):

* **Auth** — `/auth/*`
* **Document Management** — `/documents/*`
* **Folder Management** — `/folders/*`
* **Tag Management** — `/tags/*`, `/documents/{id}/tags/*`
* **AI Analysis** — `/documents/{id}/analysis/*`
* **Search** — `/search`

---

## Cross-Cutting Requirements

* All protected endpoints enforce JWT validation and ownership checks.
* File upload endpoints enforce type and size limits (defined in
  `04-non-functional`).
* Responses are typed DTOs; no entity leakage.
* Long-running AI work is never performed inside a request handler.

---

## Open Questions

* Refresh-token strategy details (rotation, lifetime) — to expand in `05-security`.
* **Bulk operations.** Tag bulk operations — add/remove across selected documents
  or folder subtrees — are specified under M8 (epic #109, ADR-010) via
  `POST /documents/tags/batch`: synchronous, atomic, and capped, with an over-cap
  reject seam reserved for a later async path (#112). Multi-delete and multi-move
  remain open and are expected to follow the same batch-endpoint pattern.