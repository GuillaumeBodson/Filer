#!/usr/bin/env bash
# Auto-generated backlog creator for Filer.
# Requires: gh (authenticated via `gh auth login`) run from inside the repo.
# Usage:  bash create-github-issues.sh   (override target with REPO=owner/name)
set -euo pipefail
REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
echo "Target repo: $REPO"

echo "== Labels =="
gh label create "epic" --color "6f42c1" --description "Epic / tracking issue" --repo "$REPO" --force
gh label create "type:feature" --color "0e8a16" --description "User-facing feature slice" --repo "$REPO" --force
gh label create "type:infra" --color "1d76db" --description "Platform / infrastructure work" --repo "$REPO" --force
gh label create "type:test" --color "fbca04" --description "Testing / quality gate" --repo "$REPO" --force
gh label create "type:chore" --color "c2e0c6" --description "Scaffolding / tooling" --repo "$REPO" --force
gh label create "module:platform" --color "5319e7" --description "SharedKernel / WebKernel / Api host" --repo "$REPO" --force
gh label create "module:auth" --color "b60205" --description "Authentication module" --repo "$REPO" --force
gh label create "module:documents" --color "0052cc" --description "Documents module" --repo "$REPO" --force
gh label create "module:storage" --color "006b75" --description "Storage module" --repo "$REPO" --force
gh label create "module:jobs" --color "fbca04" --description "Background jobs module" --repo "$REPO" --force
gh label create "module:folders" --color "d93f0b" --description "Folders module" --repo "$REPO" --force
gh label create "module:tags" --color "e99695" --description "Tags module" --repo "$REPO" --force
gh label create "module:ai" --color "8a2be2" --description "AI analysis module" --repo "$REPO" --force
gh label create "module:search" --color "fef2c0" --description "Search module" --repo "$REPO" --force
gh label create "module:ci" --color "bfd4f2" --description "CI / observability" --repo "$REPO" --force

echo "== Milestones =="
existing_ms=$(gh api "repos/$REPO/milestones?state=all" --jq ".[].title" 2>/dev/null || true)
if ! grep -qxF "M1 — Foundation / Walking skeleton" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="M1 — Foundation / Walking skeleton" -f description="Solution scaffolding, SharedKernel, WebKernel, API host, Postgres, architecture tests. Proves host+auth+persistence wire together." >/dev/null && echo "  + M1 — Foundation / Walking skeleton"; else echo "  = M1 — Foundation / Walking skeleton (exists)"; fi
if ! grep -qxF "M2 — Authentication" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="M2 — Authentication" -f description="ASP.NET Identity + JWT: register, login, refresh, logout, me. Ownership + JWT enforcement primitives." >/dev/null && echo "  + M2 — Authentication"; else echo "  = M2 — Authentication (exists)"; fi
if ! grep -qxF "M3 — Upload pipeline" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="M3 — Upload pipeline" -f description="Storage abstraction, background-job queue + worker, and the Documents slices (upload, download, list, get, update, delete)." >/dev/null && echo "  + M3 — Upload pipeline"; else echo "  = M3 — Upload pipeline (exists)"; fi
if ! grep -qxF "M4 — Folders & Tags" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="M4 — Folders & Tags" -f description="Folder hierarchy and tag management slices, plus document-tag association." >/dev/null && echo "  + M4 — Folders & Tags"; else echo "  = M4 — Folders & Tags (exists)"; fi
if ! grep -qxF "M5 — AI analysis pipeline" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="M5 — AI analysis pipeline" -f description="IAIAnalysisProvider, local provider, worker processing (retry/idempotency/cancellation), status + apply slices." >/dev/null && echo "  + M5 — AI analysis pipeline"; else echo "  = M5 — AI analysis pipeline (exists)"; fi
if ! grep -qxF "M6 — Search" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="M6 — Search" -f description="PostgreSQL full-text (tsvector/GIN) search endpoint." >/dev/null && echo "  + M6 — Search"; else echo "  = M6 — Search (exists)"; fi
if ! grep -qxF "M7 — Observability & CI" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="M7 — Observability & CI" -f description="Structured logging, metrics, CI pipeline with coverage + architecture-test gate." >/dev/null && echo "  + M7 — Observability & CI"; else echo "  = M7 — Observability & CI (exists)"; fi

echo "== Issues =="
echo "  [1/45] [EPIC] Foundation — walking skeleton"
gh issue create --repo "$REPO" \
  --title "[EPIC] Foundation — walking skeleton" \
  --milestone "M1 — Foundation / Walking skeleton" \
  --label "epic" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Stand up the solution, shared kernels, API host, Postgres and the architecture-test harness so host + auth + persistence demonstrably wire together before features (`10` build-order mapping).

**Acceptance criteria**
- [ ] All Phase-M1 issues closed
- [ ] `docker compose up -d postgres` + `dotnet run --project src/Filer.Api` boots with Swagger
- [ ] Architecture tests run green in CI

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 10 (Repository Layout, Build Order Mapping)_
FILER_EOF
  )"

echo "  [2/45] Scaffold solution, build props and docker-compose"
gh issue create --repo "$REPO" \
  --title "Scaffold solution, build props and docker-compose" \
  --milestone "M1 — Foundation / Walking skeleton" \
  --label "type:chore" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Create `Filer.sln`, `Directory.Build.props` (net10.0, nullable, warnings-as-errors), `Directory.Packages.props` (central NuGet versions), `.editorconfig` (file-scoped namespaces), and `docker-compose.yml` (api + postgres).

**Acceptance criteria**
- [ ] `Filer.sln` builds empty solution
- [ ] `Directory.Build.props` sets net10.0, nullable enable, warnings-as-errors
- [ ] `Directory.Packages.props` centralizes versions (no per-project pins)
- [ ] `.editorconfig` enforces file-scoped namespaces at build
- [ ] `docker compose up -d postgres` starts Postgres

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 10 (Repository Layout, Conventions), 11_
FILER_EOF
  )"

echo "  [3/45] Build Filer.SharedKernel primitives"
gh issue create --repo "$REPO" \
  --title "Build Filer.SharedKernel primitives" \
  --milestone "M1 — Foundation / Walking skeleton" \
  --label "type:infra" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Cross-cutting primitives shared by every module; depends on nothing else in the solution.

**Acceptance criteria**
- [ ] `Result`/`Result<T>` + `Error` types (`03` error shape)
- [ ] Paged envelope type (`items,page,pageSize,totalCount`)
- [ ] Base entity conventions: `Id`,`CreatedAt`,`UpdatedAt`,`DeletedAt`,`OwnerId`,`TenantId` (`02`)
- [ ] UTC clock abstraction
- [ ] Ownership/authorization marker interfaces
- [ ] Project references nothing else in the solution (enforced by architecture test)

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 10 (SharedKernel), 02, 03_
FILER_EOF
  )"

echo "  [4/45] Build Filer.WebKernel conventions"
gh issue create --repo "$REPO" \
  --title "Build Filer.WebKernel conventions" \
  --milestone "M1 — Foundation / Walking skeleton" \
  --label "type:infra" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Shared ASP.NET Core web conventions every module's endpoints rely on.

**Acceptance criteria**
- [ ] `Error` -> RFC7807 problem-details mapping (`03`)
- [ ] Versioned route prefixes (`ApiRoutes.V1`)
- [ ] References `SharedKernel` + ASP.NET shared framework only; never a module or `*.Contracts` (ADR-006)

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 10 (WebKernel), 03_
FILER_EOF
  )"

echo "  [5/45] Stand up Filer.Api host / composition root"
gh issue create --repo "$REPO" \
  --title "Stand up Filer.Api host / composition root" \
  --milestone "M1 — Foundation / Walking skeleton" \
  --label "type:infra" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
ASP.NET Core host: `Program.cs`, middleware pipeline, central exception handling, OpenAPI/Swagger, CORS, JWT auth wiring placeholder.

**Acceptance criteria**
- [ ] `Program.cs` wires middleware + central exception handler (broken-invariant exceptions only)
- [ ] Swagger/OpenAPI served in Development
- [ ] CORS configured
- [ ] Module registration entry-point pattern in place (`AddXModule`/`MapXEndpoints`)
- [ ] Contains no business logic

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 10 (Filer.Api), 08, 13_
FILER_EOF
  )"

echo "  [6/45] Add Filer.Architecture.Tests boundary harness"
gh issue create --repo "$REPO" \
  --title "Add Filer.Architecture.Tests boundary harness" \
  --milestone "M1 — Foundation / Walking skeleton" \
  --label "type:test" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Encode the dependency rules from `10` as executable tests (e.g. NetArchTest), run in CI.

**Acceptance criteria**
- [ ] No `Modules.X` references `Modules.Y` (only `*.Contracts`)
- [ ] No `*.Contracts` references EF Core or another module
- [ ] Nothing references `Filer.Api`
- [ ] Feature code references storage/AI only via their abstractions
- [ ] Tests fail on violation and run in CI

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 10 (Dependency Rules, Boundary Enforcement)_
FILER_EOF
  )"

echo "  [7/45] [EPIC] Authentication module"
gh issue create --repo "$REPO" \
  --title "[EPIC] Authentication module" \
  --milestone "M2 — Authentication" \
  --label "epic" --label "module:auth" \
  --body "$(cat <<'FILER_EOF'
ASP.NET Core Identity + JWT bearer. Register/login/refresh/logout/me, plus the JWT-validation and ownership-check primitives every other module reuses.

**Acceptance criteria**
- [ ] All Phase-M2 issues closed
- [ ] `/auth/*` endpoints pass the `Filer.Api.http` smoke test (register -> login -> me)
- [ ] Ownership helper returns 404 (not 403) for cross-owner access

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03 (Authentication), 05_
FILER_EOF
  )"

echo "  [8/45] Auth: register account"
gh issue create --repo "$REPO" \
  --title "Auth: register account" \
  --milestone "M2 — Authentication" \
  --label "type:feature" --label "module:auth" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/auth/register` — create an account with email/password via ASP.NET Identity.

**Acceptance criteria**
- [ ] Valid email+password creates a user; 201/200 per house convention
- [ ] Duplicate email -> 409
- [ ] Weak/invalid input -> 400 problem-details
- [ ] PasswordHash managed by Identity; never logged
- [ ] Unit tests: success + each `Error`

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02 (User), 05, 12_
FILER_EOF
  )"

echo "  [9/45] Auth: login (access + refresh tokens)"
gh issue create --repo "$REPO" \
  --title "Auth: login (access + refresh tokens)" \
  --milestone "M2 — Authentication" \
  --label "type:feature" --label "module:auth" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/auth/login` — issue JWT access token + refresh token for valid credentials.

**Acceptance criteria**
- [ ] Valid credentials -> access + refresh tokens
- [ ] Invalid credentials -> 401 (no user-enumeration leak)
- [ ] Access token signed with `Jwt__SigningKey` from env/secret store (>=32 chars)
- [ ] Token contains owner id claim used for ownership checks
- [ ] Unit tests: success + invalid-credential `Error`

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 05_
FILER_EOF
  )"

echo "  [10/45] Auth: refresh token"
gh issue create --repo "$REPO" \
  --title "Auth: refresh token" \
  --milestone "M2 — Authentication" \
  --label "type:feature" --label "module:auth" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/auth/refresh` — exchange a valid refresh token for a new access token (rotation per `05`).

**Acceptance criteria**
- [ ] Valid refresh token -> new access (+ rotated refresh)
- [ ] Revoked/expired refresh -> 401
- [ ] Reused/rotated token is rejected
- [ ] Unit tests cover rotation + rejection paths

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 05 (refresh-token strategy)_
FILER_EOF
  )"

echo "  [11/45] Auth: logout (revoke refresh)"
gh issue create --repo "$REPO" \
  --title "Auth: logout (revoke refresh)" \
  --milestone "M2 — Authentication" \
  --label "type:feature" --label "module:auth" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/auth/logout` — revoke the caller's refresh token. Requires auth.

**Acceptance criteria**
- [ ] Authenticated call revokes the refresh token
- [ ] Subsequent refresh with revoked token -> 401
- [ ] Unauthenticated -> 401
- [ ] Unit test for revoke path

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 05_
FILER_EOF
  )"

echo "  [12/45] Auth: get current user (me)"
gh issue create --repo "$REPO" \
  --title "Auth: get current user (me)" \
  --milestone "M2 — Authentication" \
  --label "type:feature" --label "module:auth" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/auth/me` — return the authenticated user's profile DTO.

**Acceptance criteria**
- [ ] Authenticated -> profile DTO (no entity leakage, no PasswordHash)
- [ ] Unauthenticated -> 401
- [ ] Unit test for mapping

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 13_
FILER_EOF
  )"

echo "  [13/45] Auth: JWT validation + ownership primitives"
gh issue create --repo "$REPO" \
  --title "Auth: JWT validation + ownership primitives" \
  --milestone "M2 — Authentication" \
  --label "type:infra" --label "module:auth" \
  --body "$(cat <<'FILER_EOF'
Shared enforcement reused by every protected slice: JWT bearer validation and an ownership guard.

**Acceptance criteria**
- [ ] JWT bearer validation wired in host
- [ ] Ownership helper resolves owner id from token and compares to resource OwnerId
- [ ] Cross-owner access returns 404 not 403 (`05`)
- [ ] Integration test: cross-owner access -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 05, 03 (Cross-Cutting), 12_
FILER_EOF
  )"

echo "  [14/45] [EPIC] Upload pipeline (Storage + Jobs + Documents)"
gh issue create --repo "$REPO" \
  --title "[EPIC] Upload pipeline (Storage + Jobs + Documents)" \
  --milestone "M3 — Upload pipeline" \
  --label "epic" --label "module:documents" \
  --body "$(cat <<'FILER_EOF'
The core async upload flow and document CRUD: storage abstraction, durable job queue + worker, then the Documents slices. Upload never blocks on AI (`06`/`08`).

**Acceptance criteria**
- [ ] All Phase-M3 issues closed
- [ ] Upload returns 201 immediately and queues an AnalysisJob
- [ ] Duplicate upload (same owner+hash) -> 409
- [ ] Cross-owner document access -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03 (Documents), 06, 08_
FILER_EOF
  )"

echo "  [15/45] Storage: IFileStorageProvider + local provider"
gh issue create --repo "$REPO" \
  --title "Storage: IFileStorageProvider + local provider" \
  --milestone "M3 — Upload pipeline" \
  --label "type:infra" --label "module:storage" \
  --body "$(cat <<'FILER_EOF'
Define `IFileStorageProvider` in `Storage.Contracts` and implement `LocalFileSystemStorageProvider`. Binary bytes never touch the database (`02`).

**Acceptance criteria**
- [ ] `IFileStorageProvider` (save/read/delete by opaque `StorageKey`) in `Storage.Contracts`
- [ ] `LocalFileSystemStorageProvider` implementation
- [ ] Selected by configuration; no concrete provider leaks into domain (`07`/`08`)
- [ ] Unit tests for save/read/delete round-trip

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 07, 02, 08, 12_
FILER_EOF
  )"

echo "  [16/45] Jobs: IBackgroundJobQueue + worker + DB-backed queue"
gh issue create --repo "$REPO" \
  --title "Jobs: IBackgroundJobQueue + worker + DB-backed queue" \
  --milestone "M3 — Upload pipeline" \
  --label "type:infra" --label "module:jobs" \
  --body "$(cat <<'FILER_EOF'
Define `IBackgroundJobQueue` in `BackgroundJobs.Contracts` and a hosted-service worker. For V1 the `AnalysisJob` table is the durable work source (poll/claim with row locking).

**Acceptance criteria**
- [ ] `IBackgroundJobQueue` abstraction in Contracts
- [ ] Hosted-service worker claims jobs safely under concurrency (no double-run)
- [ ] Durable: a crash loses no work
- [ ] Worker honors `CancellationToken`
- [ ] Unit/integration test for safe single-claim

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 06 (Worker & Queue), 08, 12_
FILER_EOF
  )"

echo "  [17/45] Documents: upload (async, dedupe)"
gh issue create --repo "$REPO" \
  --title "Documents: upload (async, dedupe)" \
  --milestone "M3 — Upload pipeline" \
  --label "type:feature" --label "module:documents" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/documents` (`multipart/form-data`) — validate, persist bytes, hash, dedupe, persist metadata, queue analysis, return 201. Never runs AI inline.

**Acceptance criteria**
- [ ] File validated: type allow-list, size limit, content sniffing (`04`/`05`)
- [ ] Bytes persisted via `IFileStorageProvider`; `ContentHash` = SHA-256
- [ ] Hash matches existing non-deleted owned doc -> 409 with existing reference
- [ ] Document persisted with status `Uploaded`; `AnalysisJob` queued
- [ ] Returns 201 with document metadata + job id immediately (no AI wait)
- [ ] Oversize -> 413; unsupported type -> 415
- [ ] Unit tests: success + each `Error`; integration: upload-validation + 409 dedupe

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03 (Upload behavior), 02, 04, 05, 06, 12_
FILER_EOF
  )"

echo "  [18/45] Documents: download content"
gh issue create --repo "$REPO" \
  --title "Documents: download content" \
  --milestone "M3 — Upload pipeline" \
  --label "type:feature" --label "module:documents" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/documents/{id}/content` — stream the binary via `IFileStorageProvider`.

**Acceptance criteria**
- [ ] Owner streams bytes with correct ContentType
- [ ] Cross-owner / missing -> 404
- [ ] Soft-deleted document -> 404
- [ ] Integration test: ownership -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 05, 07, 12_
FILER_EOF
  )"

echo "  [19/45] Documents: get metadata"
gh issue create --repo "$REPO" \
  --title "Documents: get metadata" \
  --milestone "M3 — Upload pipeline" \
  --label "type:feature" --label "module:documents" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/documents/{id}` — return document metadata DTO.

**Acceptance criteria**
- [ ] Owner -> metadata DTO (no entity leakage)
- [ ] Cross-owner / missing -> 404
- [ ] Unit test for mapping; integration: ownership -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 05, 13, 12_
FILER_EOF
  )"

echo "  [20/45] Documents: list (filter + paginate)"
gh issue create --repo "$REPO" \
  --title "Documents: list (filter + paginate)" \
  --milestone "M3 — Upload pipeline" \
  --label "type:feature" --label "module:documents" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/documents` — list owned, non-deleted documents with `?folderId=`,`?tagId=`,`?q=` filters and pagination.

**Acceptance criteria**
- [ ] Returns paged envelope scoped to owner
- [ ] Filters folderId/tagId/q apply
- [ ] Excludes soft-deleted
- [ ] Pagination params validated (400 on bad input)
- [ ] Unit tests for filter/paging logic

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03 (List filters), 02, 12_
FILER_EOF
  )"

echo "  [21/45] Documents: update metadata (rename / move)"
gh issue create --repo "$REPO" \
  --title "Documents: update metadata (rename / move)" \
  --milestone "M3 — Upload pipeline" \
  --label "type:feature" --label "module:documents" \
  --body "$(cat <<'FILER_EOF'
`PATCH /api/v1/documents/{id}` — rename or move a document to another folder.

**Acceptance criteria**
- [ ] Owner can rename / change FolderId
- [ ] Target folder must be owned by caller else 404
- [ ] Cross-owner -> 404
- [ ] Validation -> 400
- [ ] Unit tests: success + each `Error`

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02, 05, 12_
FILER_EOF
  )"

echo "  [22/45] Documents: soft-delete (cancels jobs)"
gh issue create --repo "$REPO" \
  --title "Documents: soft-delete (cancels jobs)" \
  --milestone "M3 — Upload pipeline" \
  --label "type:feature" --label "module:documents" \
  --body "$(cat <<'FILER_EOF'
`DELETE /api/v1/documents/{id}` — soft-delete (set `DeletedAt`) and cancel any in-flight/queued analysis jobs.

**Acceptance criteria**
- [ ] Sets `DeletedAt`; document excluded from lists/downloads afterward
- [ ] Queued/running `AnalysisJob`s for the doc move to `Cancelled` (`06`)
- [ ] Cross-owner -> 404
- [ ] Integration test: delete cancels job; ownership -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02, 06, 05, 12_
FILER_EOF
  )"

echo "  [23/45] [EPIC] Folders & Tags"
gh issue create --repo "$REPO" \
  --title "[EPIC] Folders & Tags" \
  --milestone "M4 — Folders & Tags" \
  --label "epic" --label "module:folders" \
  --body "$(cat <<'FILER_EOF'
Folder hierarchy and tag management, plus document-tag association. Folders and Tags may merge later via ADR (`10` open item) — keep them separate for now.

**Acceptance criteria**
- [ ] All Phase-M4 issues closed
- [ ] Folder cycles prevented
- [ ] Tag uniqueness per owner enforced

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03 (Folders, Tags), 02_
FILER_EOF
  )"

echo "  [24/45] Folders: create"
gh issue create --repo "$REPO" \
  --title "Folders: create" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:folders" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/folders` — create a folder with optional `parentId`.

**Acceptance criteria**
- [ ] Owner creates folder; `parentId` (if given) must be owned else 404
- [ ] Unique `(OwnerId, ParentId, Name)` enforced -> 409 on clash
- [ ] Validation -> 400
- [ ] Unit tests: success + each `Error`

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02 (Folder), 12_
FILER_EOF
  )"

echo "  [25/45] Folders: list (tree / flat)"
gh issue create --repo "$REPO" \
  --title "Folders: list (tree / flat)" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:folders" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/folders` — list owned folders as tree or flat.

**Acceptance criteria**
- [ ] Returns owner-scoped folders
- [ ] Tree/flat representation per query
- [ ] Excludes soft-deleted
- [ ] Unit test for tree assembly

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02, 12_
FILER_EOF
  )"

echo "  [26/45] Folders: get"
gh issue create --repo "$REPO" \
  --title "Folders: get" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:folders" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/folders/{id}` — return a folder DTO.

**Acceptance criteria**
- [ ] Owner -> folder DTO
- [ ] Cross-owner / missing -> 404
- [ ] Integration: ownership -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 05, 12_
FILER_EOF
  )"

echo "  [27/45] Folders: rename / move (cycle-safe)"
gh issue create --repo "$REPO" \
  --title "Folders: rename / move (cycle-safe)" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:folders" \
  --body "$(cat <<'FILER_EOF'
`PATCH /api/v1/folders/{id}` — rename or re-parent a folder. Must prevent cycles in application logic (`02`).

**Acceptance criteria**
- [ ] Owner renames / re-parents
- [ ] Re-parenting that would create a cycle -> 400/409
- [ ] New parent must be owned else 404
- [ ] Unique `(OwnerId, ParentId, Name)` preserved
- [ ] Unit tests incl. cycle-prevention case

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02 (cycle prevention), 12_
FILER_EOF
  )"

echo "  [28/45] Folders: delete (non-empty semantics)"
gh issue create --repo "$REPO" \
  --title "Folders: delete (non-empty semantics)" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:folders" \
  --body "$(cat <<'FILER_EOF'
`DELETE /api/v1/folders/{id}` — soft-delete a folder. Resolve non-empty behavior (reject vs cascade vs move-to-parent) — open question in `02`/`03`.

**Acceptance criteria**
- [ ] Decision recorded (ADR or doc update) for non-empty folders before merge
- [ ] Chosen behavior implemented + tested
- [ ] Cross-owner -> 404

> Blocked-by decision: non-empty folder deletion semantics. Capture as an ADR entry in `09` first.

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02 (open questions)_
FILER_EOF
  )"

echo "  [29/45] Tags: create"
gh issue create --repo "$REPO" \
  --title "Tags: create" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:tags" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/tags` — create a per-owner tag.

**Acceptance criteria**
- [ ] Owner creates tag; unique `(OwnerId, Name)` -> 409 on clash
- [ ] Validation -> 400
- [ ] Unit tests: success + each `Error`

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02 (Tag), 12_
FILER_EOF
  )"

echo "  [30/45] Tags: list"
gh issue create --repo "$REPO" \
  --title "Tags: list" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:tags" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/tags` — list owned tags.

**Acceptance criteria**
- [ ] Returns owner-scoped tags
- [ ] Unit test for mapping

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02, 12_
FILER_EOF
  )"

echo "  [31/45] Tags: rename"
gh issue create --repo "$REPO" \
  --title "Tags: rename" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:tags" \
  --body "$(cat <<'FILER_EOF'
`PATCH /api/v1/tags/{id}` — rename a tag.

**Acceptance criteria**
- [ ] Owner renames; unique `(OwnerId, Name)` preserved -> 409 on clash
- [ ] Cross-owner -> 404
- [ ] Validation -> 400
- [ ] Unit tests: success + each `Error`

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02, 12_
FILER_EOF
  )"

echo "  [32/45] Tags: delete"
gh issue create --repo "$REPO" \
  --title "Tags: delete" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:tags" \
  --body "$(cat <<'FILER_EOF'
`DELETE /api/v1/tags/{id}` — delete a tag and its `DocumentTag` rows.

**Acceptance criteria**
- [ ] Owner deletes tag; associations removed
- [ ] Cross-owner -> 404
- [ ] Integration: ownership -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02 (DocumentTag), 12_
FILER_EOF
  )"

echo "  [33/45] Document-tags: replace set / add / remove"
gh issue create --repo "$REPO" \
  --title "Document-tags: replace set / add / remove" \
  --milestone "M4 — Folders & Tags" \
  --label "type:feature" --label "module:tags" \
  --body "$(cat <<'FILER_EOF'
`PUT /documents/{id}/tags` (replace), `POST /documents/{id}/tags/{tagId}` (add), `DELETE /documents/{id}/tags/{tagId}` (remove). Records `Source=User`.

**Acceptance criteria**
- [ ] Replace sets the document's full tag set; add/remove adjust single associations
- [ ] All referenced tags + document owned by caller else 404
- [ ] New associations recorded with `Source=User` (preserve AiSuggested rows per `02`)
- [ ] Cross-owner doc or tag -> 404
- [ ] Unit + integration tests

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03 (Tags), 02 (DocumentTag.Source), 05, 12_
FILER_EOF
  )"

echo "  [34/45] [EPIC] AI analysis pipeline"
gh issue create --repo "$REPO" \
  --title "[EPIC] AI analysis pipeline" \
  --milestone "M5 — AI analysis pipeline" \
  --label "epic" --label "module:ai" \
  --body "$(cat <<'FILER_EOF'
Asynchronous, provider-agnostic, advisory analysis. Worker produces folder/tag suggestions + duplicate findings; nothing is applied without user confirmation.

**Acceptance criteria**
- [ ] All Phase-M5 issues closed
- [ ] Default provider runs locally / no-egress (`05`)
- [ ] Suggestions applied only via the apply endpoint

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 06, 02 (AnalysisJob), 03 (analysis endpoints)_
FILER_EOF
  )"

echo "  [35/45] AI: IAIAnalysisProvider abstraction + neutral DTOs"
gh issue create --repo "$REPO" \
  --title "AI: IAIAnalysisProvider abstraction + neutral DTOs" \
  --milestone "M5 — AI analysis pipeline" \
  --label "type:infra" --label "module:ai" \
  --body "$(cat <<'FILER_EOF'
Define `IAIAnalysisProvider` and provider-neutral request/result DTOs in `AiAnalysis.Contracts`.

**Acceptance criteria**
- [ ] `AnalyzeAsync(request, ct)` interface in Contracts
- [ ] Request carries content/reference + existing folders/tags context
- [ ] Result DTO: suggested folder, suggested tags, duplicate signals, confidence — vendor-neutral
- [ ] No vendor type crosses the boundary

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 06 (Provider Abstraction), 08_
FILER_EOF
  )"

echo "  [36/45] AI: local provider adapter (no-egress default)"
gh issue create --repo "$REPO" \
  --title "AI: local provider adapter (no-egress default)" \
  --milestone "M5 — AI analysis pipeline" \
  --label "type:infra" --label "module:ai" \
  --body "$(cat <<'FILER_EOF'
Implement a local/no-egress `IAIAnalysisProvider` (e.g. Ollama) as the privacy-respecting default; provider chosen by configuration.

**Acceptance criteria**
- [ ] Local provider implementation behind the interface
- [ ] Selected via environment config; switching providers needs no domain change
- [ ] Credentials (if any) live with the worker only, never reach clients (`05`)
- [ ] Unit test with a fake provider

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 06 (Privacy & Provider Selection), 05, 12_
FILER_EOF
  )"

echo "  [37/45] AI: worker job processing (retry / idempotency / cancel)"
gh issue create --repo "$REPO" \
  --title "AI: worker job processing (retry / idempotency / cancel)" \
  --milestone "M5 — AI analysis pipeline" \
  --label "type:infra" --label "module:jobs" \
  --body "$(cat <<'FILER_EOF'
Worker consumes queued `AnalysisJob`s through the full lifecycle (`06`): Queued->Running->Succeeded/Failed/Cancelled.

**Acceptance criteria**
- [ ] Claims job, sets `StartedAt`, increments `AttemptCount`
- [ ] On success writes `Result` JSONB; document `Status`->`Ready`
- [ ] Retryable failures back off and requeue up to attempt limit, then terminal `Failed`
- [ ] Honors cancellation mid-flight (deleting doc cancels job)
- [ ] Idempotent: re-run produces consistent result, no duplicate suggestions
- [ ] Emits structured logs + metrics (queue depth, duration, success/failure)
- [ ] Tests: retry, cancellation, idempotency, concurrent single-claim

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 06 (Job Lifecycle, Reliability), 04, 12_
FILER_EOF
  )"

echo "  [38/45] AI: get analysis status + result"
gh issue create --repo "$REPO" \
  --title "AI: get analysis status + result" \
  --milestone "M5 — AI analysis pipeline" \
  --label "type:feature" --label "module:ai" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/documents/{id}/analysis` — return the job status and any suggestions.

**Acceptance criteria**
- [ ] Owner -> job status + `Result` suggestions
- [ ] Cross-owner / missing -> 404
- [ ] Terminal-failed surfaced as analysis-unavailable
- [ ] Unit + integration (ownership -> 404)

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 06 (Applying Suggestions), 05, 12_
FILER_EOF
  )"

echo "  [39/45] AI: apply confirmed suggestions"
gh issue create --repo "$REPO" \
  --title "AI: apply confirmed suggestions" \
  --milestone "M5 — AI analysis pipeline" \
  --label "type:feature" --label "module:ai" \
  --body "$(cat <<'FILER_EOF'
`POST /api/v1/documents/{id}/analysis/apply` — apply user-confirmed folder/tag suggestions; tags recorded with `Source=AiSuggested`.

**Acceptance criteria**
- [ ] User may accept all/some/none
- [ ] Applied tags create `DocumentTag` rows with `Source=AiSuggested` (`02`)
- [ ] Folder suggestion applied only if confirmed
- [ ] Cross-owner -> 404
- [ ] Unit tests: partial/none/all; integration: ownership -> 404

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 06, 02, 05, 12_
FILER_EOF
  )"

echo "  [40/45] [EPIC] Search"
gh issue create --repo "$REPO" \
  --title "[EPIC] Search" \
  --milestone "M6 — Search" \
  --label "epic" --label "module:search" \
  --body "$(cat <<'FILER_EOF'
V1 full-text search over owned documents via PostgreSQL `tsvector`/GIN. Contract designed to absorb semantic (pgvector) search later without breaking clients.

**Acceptance criteria**
- [ ] All Phase-M6 issues closed
- [ ] `?q=` returns owner-scoped ranked results

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03 (Search), 02 (Full-text)_
FILER_EOF
  )"

echo "  [41/45] Search: full-text endpoint (tsvector / GIN)"
gh issue create --repo "$REPO" \
  --title "Search: full-text endpoint (tsvector / GIN)" \
  --milestone "M6 — Search" \
  --label "type:feature" --label "module:search" \
  --body "$(cat <<'FILER_EOF'
`GET /api/v1/search?q=` — full-text across `FileName` + selected metadata using a generated `tsvector` column with a GIN index.

**Acceptance criteria**
- [ ] Generated `tsvector` over FileName + metadata with GIN index (migration)
- [ ] `?q=` returns owner-scoped, ranked, paginated results
- [ ] Excludes soft-deleted
- [ ] Endpoint contract leaves room for a future semantic sibling without breaking clients
- [ ] Unit + integration tests

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 03, 02 (PostgreSQL-Specific Notes), 12_
FILER_EOF
  )"

echo "  [42/45] [EPIC] Observability & CI"
gh issue create --repo "$REPO" \
  --title "[EPIC] Observability & CI" \
  --milestone "M7 — Observability & CI" \
  --label "epic" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
Cross-cutting structured logging, metrics, and the CI gate (build, tests, coverage, architecture tests).

**Acceptance criteria**
- [ ] All Phase-M7 issues closed
- [ ] CI runs build + tests + architecture tests + coverage gate on every PR

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 04, 11, 12, 10 (Boundary Enforcement)_
FILER_EOF
  )"

echo "  [43/45] Observability: structured logging + correlation ids"
gh issue create --repo "$REPO" \
  --title "Observability: structured logging + correlation ids" \
  --milestone "M7 — Observability & CI" \
  --label "type:infra" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
Solution-wide structured logging via `ILogger` message templates with correlation ids tying analysis back to the originating upload.

**Acceptance criteria**
- [ ] Message-template logging with correct levels
- [ ] Correlation id flows request -> queued job -> worker
- [ ] Never logs secrets/tokens/passwords/file contents (`05`)
- [ ] Log-assertion test for a critical path

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 04, 05, 08 (Logging), 13_
FILER_EOF
  )"

echo "  [44/45] Observability: metrics (queue depth, job duration, outcomes)"
gh issue create --repo "$REPO" \
  --title "Observability: metrics (queue depth, job duration, outcomes)" \
  --milestone "M7 — Observability & CI" \
  --label "type:infra" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
Emit metrics for the background pipeline and key API paths.

**Acceptance criteria**
- [ ] Metrics: queue depth, job duration, success/failure counts
- [ ] Exposed for scraping (endpoint/exporter)
- [ ] Correlated to uploads

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 06 (Observability), 04_
FILER_EOF
  )"

echo "  [45/45] CI: pipeline with coverage + architecture gate"
gh issue create --repo "$REPO" \
  --title "CI: pipeline with coverage + architecture gate" \
  --milestone "M7 — Observability & CI" \
  --label "type:test" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
CI pipeline running on every PR with branch protection (`11`).

**Acceptance criteria**
- [ ] Pipeline: restore -> build (warnings-as-errors) -> unit + integration tests (Testcontainers Postgres) -> architecture tests
- [ ] Coverage gate enforced per `12`
- [ ] Required status checks before merge (branch protection, `11`)
- [ ] Fails PR on any gate breach

**Definition of Done** (`13`): warnings-as-errors clean; `Result`/`Error` for business outcomes (no exceptions); `CancellationToken` propagated; structured logs, no secrets logged; required tests present (`12`).

_Refs: 11, 12, 10 (Boundary Enforcement)_
FILER_EOF
  )"

echo "Done. Now create a Project board — see README-backlog.md."
