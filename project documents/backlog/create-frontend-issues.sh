#!/usr/bin/env bash
# Auto-generated FRONTEND backlog creator for Filer.
# Separate from create-github-issues.sh (the 45 V1 backend tickets); this adds the
# web frontend epics/milestones/slices per ADR-001 (Blazor), ADR-011 (Kiota client
# generation), ADR-012 (start in parallel, web-first, frozen endpoints first).
# Requires: gh (authenticated via `gh auth login`) run from inside the repo.
# Usage:  bash create-frontend-issues.sh   (override target with REPO=owner/name)
# Safe to re-run for labels/milestones (idempotent); issues are NOT deduplicated — run once.
set -euo pipefail
REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
echo "Target repo: $REPO"

echo "== Labels =="
# Reuse the existing epic/type:* labels; add the web module label.
gh label create "epic" --color "6f42c1" --description "Epic / tracking issue" --repo "$REPO" --force
gh label create "type:feature" --color "0e8a16" --description "User-facing feature slice" --repo "$REPO" --force
gh label create "type:infra" --color "1d76db" --description "Platform / infrastructure work" --repo "$REPO" --force
gh label create "type:test" --color "fbca04" --description "Testing / quality gate" --repo "$REPO" --force
gh label create "type:chore" --color "c2e0c6" --description "Scaffolding / tooling" --repo "$REPO" --force
gh label create "module:web" --color "0a7ea4" --description "Blazor WebAssembly frontend + shared RCL" --repo "$REPO" --force

echo "== Milestones =="
existing_ms=$(gh api "repos/$REPO/milestones?state=all" --jq ".[].title" 2>/dev/null || true)
if ! grep -qxF "FE-M1 — Frontend foundation" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="FE-M1 — Frontend foundation" -f description="Blazor WASM app + shared Razor Class Library, Kiota client generation, app shell, auth plumbing, and the frontend CI/test harness. Proves the client boots, authenticates, and calls the API through the generated client (ADR-001/011/012)." >/dev/null && echo "  + FE-M1 — Frontend foundation"; else echo "  = FE-M1 — Frontend foundation (exists)"; fi
if ! grep -qxF "FE-M2 — Core document workflow (web)" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="FE-M2 — Core document workflow (web)" -f description="The usable web app against the frozen core endpoints: auth UI, document list/upload/detail/download/delete, folders, and tags. No AI/search UI yet (ADR-012)." >/dev/null && echo "  + FE-M2 — Core document workflow (web)"; else echo "  = FE-M2 — Core document workflow (web) (exists)"; fi
if ! grep -qxF "FE-M3 — AI suggestions & search UI" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="FE-M3 — AI suggestions & search UI" -f description="Analysis-suggestions panel (status polling + apply) and search UI. Blocked on the backend M5/M6 contracts settling (ADR-012); start once #38/#39 and the M6 search endpoint are frozen." >/dev/null && echo "  + FE-M3 — AI suggestions & search UI"; else echo "  = FE-M3 — AI suggestions & search UI (exists)"; fi

echo "== Issues =="

echo "  [1/16] [EPIC] Frontend foundation (web)"
gh issue create --repo "$REPO" \
  --title "[EPIC] Frontend foundation (web)" \
  --milestone "FE-M1 — Frontend foundation" \
  --label "epic" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Stand up the Blazor WebAssembly app and shared Razor Class Library, wire the Kiota-generated API client, the app shell, and auth plumbing so the client boots, logs in, and calls the API through the generated client before any feature screens (ADR-001, ADR-011, ADR-012).

**Acceptance criteria**
- [ ] All FE-M1 issues closed
- [ ] `dotnet run` serves the WASM app; it reaches the API and authenticates end to end
- [ ] All API calls go through the generated client (no hand-rolled `HttpClient`)
- [ ] Frontend builds + component tests run green in CI

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: ADR-001, ADR-011, ADR-012, 10, 03_
FILER_EOF
  )"

echo "  [2/16] Scaffold Blazor WASM app + shared Razor Class Library"
gh issue create --repo "$REPO" \
  --title "Scaffold Blazor WASM app + shared Razor Class Library" \
  --milestone "FE-M1 — Frontend foundation" \
  --label "type:chore" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Create the Blazor WebAssembly host project and the shared Razor Class Library (UI components reused by web now, MAUI later — ADR-001), wired into `Filer.sln` under the layout in `10`.

**Acceptance criteria**
- [ ] WASM app project + shared RCL added to `Filer.sln`
- [ ] Component/page conventions follow `10`; file-scoped namespaces, warnings-as-errors honored
- [ ] Reusable UI lives in the RCL; the WASM host stays a thin shell
- [ ] Baseline styling/design tokens in place (no per-page ad-hoc CSS)
- [ ] App builds and serves a placeholder page

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: ADR-001, 10, 13_
FILER_EOF
  )"

echo "  [3/16] Generate typed API client from OpenAPI with Kiota"
gh issue create --repo "$REPO" \
  --title "Generate typed API client from OpenAPI with Kiota" \
  --milestone "FE-M1 — Frontend foundation" \
  --label "type:infra" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Generate a typed C# client from the published OpenAPI document (`/openapi/v1.json`) with Kiota and register it for DI; wire generation into the build/workflow so the client tracks the contract (ADR-011).

**Acceptance criteria**
- [ ] Kiota generation wired (build step or documented regen command); checked-in vs generated-on-build chosen and documented
- [ ] Generated client registered in DI and injectable into components
- [ ] A contract change that breaks the client fails the build
- [ ] No hand-written endpoint/DTO code; no server DTOs shared into the client
- [ ] base address configurable per environment

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: ADR-011, 03_
FILER_EOF
  )"

echo "  [4/16] App shell: layout, routing, navigation, shared UI states"
gh issue create --repo "$REPO" \
  --title "App shell: layout, routing, navigation, shared UI states" \
  --milestone "FE-M1 — Frontend foundation" \
  --label "type:infra" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
The application shell: layout, navigation, routing, and the shared building blocks every screen reuses — loading/empty/error patterns and RFC7807 `ProblemDetails` rendering (`03`).

**Acceptance criteria**
- [ ] Layout + navigation + client-side routing in place
- [ ] Reusable loading / empty / error components in the RCL
- [ ] `ProblemDetails` from the API rendered uniformly (validation + 4xx/5xx)
- [ ] 404 ownership responses surface as not-found, not as errors (`05`)
- [ ] Component tests for the error/empty/loading components

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (error shape), 05, 10_
FILER_EOF
  )"

echo "  [5/16] Auth plumbing: token store, auth state, bearer handler, 401 refresh"
gh issue create --repo "$REPO" \
  --title "Auth plumbing: token store, auth state, bearer handler, 401 refresh" \
  --milestone "FE-M1 — Frontend foundation" \
  --label "type:infra" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
The cross-cutting auth machinery every protected screen relies on: token storage, an `AuthenticationStateProvider`, a delegating handler that attaches the bearer token, and refresh-on-401 with rotation (`05`, ADR-012). Consumes the existing `/auth/*` endpoints.

**Acceptance criteria**
- [ ] Access + refresh tokens stored client-side; never logged (`05`)
- [ ] `AuthenticationStateProvider` exposes auth state to components
- [ ] Delegating handler attaches the bearer token to client requests
- [ ] 401 triggers a refresh + retry; failed refresh routes to login (rotation per `05`)
- [ ] Tests for the handler + refresh path

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 05 (tokens/refresh), 03 (Authentication), ADR-012, 12_
FILER_EOF
  )"

echo "  [6/16] Frontend CI + component test harness (bUnit)"
gh issue create --repo "$REPO" \
  --title "Frontend CI + component test harness (bUnit)" \
  --milestone "FE-M1 — Frontend foundation" \
  --label "type:test" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Add a bUnit component-test project and extend CI to build and test the frontend on every PR, consistent with the gate in `11`/`12`.

**Acceptance criteria**
- [ ] bUnit test project added to `Filer.sln`
- [ ] CI builds the WASM app (warnings-as-errors) and runs frontend tests
- [ ] Coverage reported per `12`
- [ ] Frontend failures fail the PR

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 11, 12_
FILER_EOF
  )"

echo "  [7/16] [EPIC] Core document workflow (web)"
gh issue create --repo "$REPO" \
  --title "[EPIC] Core document workflow (web)" \
  --milestone "FE-M2 — Core document workflow (web)" \
  --label "epic" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
The usable web app: authenticate, upload, browse, organize into folders, and tag — all against the frozen core endpoints (auth/documents/folders/tags). No AI or search UI yet (ADR-012).

**Acceptance criteria**
- [ ] All FE-M2 issues closed
- [ ] A user can register/login, upload a document, see it become Ready, browse/filter, organize into folders, and tag it
- [ ] Cross-owner / missing resources surface as not-found (`05`)

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (Documents/Folders/Tags), ADR-012_
FILER_EOF
  )"

echo "  [8/16] Auth UI: register, login, logout, profile + route guards"
gh issue create --repo "$REPO" \
  --title "Auth UI: register, login, logout, profile + route guards" \
  --milestone "FE-M2 — Core document workflow (web)" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Screens for register, login, logout, and profile (`/auth/me`), with route guards redirecting unauthenticated users to login. Builds on the FE-M1 auth plumbing.

**Acceptance criteria**
- [ ] Register / login / logout flows work end to end
- [ ] Profile screen shows the current user (no entity leakage)
- [ ] Protected routes redirect anonymous users to login; post-login returns to the target
- [ ] Server validation errors rendered from `ProblemDetails`
- [ ] Component tests for the forms + guard

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (Authentication), 05, 12_
FILER_EOF
  )"

echo "  [9/16] Documents: list with folder/tag/q filters + pagination"
gh issue create --repo "$REPO" \
  --title "Documents: list with folder/tag/q filters + pagination" \
  --milestone "FE-M2 — Core document workflow (web)" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
The document list screen: owner-scoped documents with `folderId`/`tagId`/`q` filters and pagination, with loading/empty/error states.

**Acceptance criteria**
- [ ] Paged list bound to the list endpoint's envelope
- [ ] Folder / tag / text filters apply and combine
- [ ] Empty, loading, and error states handled
- [ ] Page-size / page controls validated
- [ ] Component tests for filter + paging behavior

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (List filters), 12_
FILER_EOF
  )"

echo "  [10/16] Documents: upload with async status UX"
gh issue create --repo "$REPO" \
  --title "Documents: upload with async status UX" \
  --milestone "FE-M2 — Core document workflow (web)" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Upload UI for `multipart/form-data`, modelling the async pipeline: upload returns immediately, the document shows `Uploaded` then transitions to `Ready` as analysis completes (poll status). This is the screen that validates the async contract (`06`).

**Acceptance criteria**
- [ ] Multipart upload with client-side size/type pre-checks mirroring server limits (`04`/`05`)
- [ ] Upload progress shown; returns immediately (no AI wait)
- [ ] Document reflects `Uploaded` -> `Ready` via status polling/refresh
- [ ] 409 duplicate surfaced with the existing-document reference; 413/415 handled
- [ ] Component tests for the status-transition + error handling

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (Upload), 06 (async pipeline), 04, 05, 12_
FILER_EOF
  )"

echo "  [11/16] Documents: detail, rename/move, download, delete"
gh issue create --repo "$REPO" \
  --title "Documents: detail, rename/move, download, delete" \
  --milestone "FE-M2 — Core document workflow (web)" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
The document detail screen: view metadata, rename, move to another folder, download the content, and soft-delete.

**Acceptance criteria**
- [ ] Metadata view; rename + move (target folder must be owned)
- [ ] Download streams content with correct type
- [ ] Delete soft-deletes and removes the doc from lists; confirms with the user
- [ ] Cross-owner / missing -> not-found UI (`05`)
- [ ] Component tests for the actions

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (Documents), 05, 07 (download), 12_
FILER_EOF
  )"

echo "  [12/16] Folders UI: tree, create, rename/move (cycle-safe), delete"
gh issue create --repo "$REPO" \
  --title "Folders UI: tree, create, rename/move (cycle-safe), delete" \
  --milestone "FE-M2 — Core document workflow (web)" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Folder management: a tree view plus create, rename, re-parent (cycle-safe feedback), and delete honoring the non-empty semantics decided in ADR-007.

**Acceptance criteria**
- [ ] Folder tree rendered; create / rename / re-parent
- [ ] Re-parent that would create a cycle is prevented with clear feedback (server 400/409)
- [ ] Delete follows ADR-007 non-empty behavior (reject by default; explicit opt-in cascade)
- [ ] Cross-owner / missing -> not-found UI
- [ ] Component tests for tree assembly + cycle/delete feedback

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (Folders), 02 (cycle prevention), ADR-007, 12_
FILER_EOF
  )"

echo "  [13/16] Tags UI: tag CRUD + document tag assignment"
gh issue create --repo "$REPO" \
  --title "Tags UI: tag CRUD + document tag assignment" \
  --milestone "FE-M2 — Core document workflow (web)" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
Tag management and applying tags to documents (replace set / add / remove), surfacing the `Source` distinction (user vs AI-suggested) per `02`.

**Acceptance criteria**
- [ ] Create / rename / delete tags (per-owner uniqueness errors surfaced)
- [ ] Assign / remove / replace a document's tags
- [ ] User-applied tags recorded as `Source=User`; AI-suggested tags visually distinguished (`02`)
- [ ] Cross-owner doc or tag -> not-found UI
- [ ] Component tests for assignment behavior

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (Tags), 02 (DocumentTag.Source), 12_
FILER_EOF
  )"

echo "  [14/16] [EPIC] AI suggestions & search UI"
gh issue create --repo "$REPO" \
  --title "[EPIC] AI suggestions & search UI" \
  --milestone "FE-M3 — AI suggestions & search UI" \
  --label "epic" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
The advisory + discovery layer on top of the core app: AI analysis suggestions and full-text search UI. Additive to existing screens (ADR-012).

> Blocked-by backend contracts: M5 analysis endpoints (#38/#39) and the M6 search endpoint must be frozen before building these screens.

**Acceptance criteria**
- [ ] All FE-M3 issues closed
- [ ] Suggestions can be reviewed and applied; search returns ranked results
- [ ] Built only against settled M5/M6 contracts

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 06, 03 (analysis + search), ADR-012_
FILER_EOF
  )"

echo "  [15/16] AI suggestions: status, review, apply (all/some/none)"
gh issue create --repo "$REPO" \
  --title "AI suggestions: status, review, apply (all/some/none)" \
  --milestone "FE-M3 — AI suggestions & search UI" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
On the document detail screen, show analysis status and suggestions and let the user apply folder/tag suggestions (accept all, some, or none); applied tags carry `Source=AiSuggested` (`06`/`02`).

> Blocked-by: backend #38 (get analysis) and #39 (apply) contracts frozen.

**Acceptance criteria**
- [ ] Analysis status surfaced (queued/running/ready/failed); failure shown as analysis-unavailable, never blocking the document
- [ ] Suggestions listed; user accepts all/some/none
- [ ] Apply calls the apply endpoint; applied tags shown as AI-suggested (`02`)
- [ ] Cross-owner / missing -> not-found UI
- [ ] Component tests for the apply selection logic

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 06 (Applying Suggestions), 03, 02, 12_
FILER_EOF
  )"

echo "  [16/16] Search UI: query + ranked results"
gh issue create --repo "$REPO" \
  --title "Search UI: query + ranked results" \
  --milestone "FE-M3 — AI suggestions & search UI" \
  --label "type:feature" --label "module:web" \
  --body "$(cat <<'FILER_EOF'
A search box returning owner-scoped, ranked, paginated results from the full-text endpoint, integrated with the document list.

> Blocked-by: backend M6 search endpoint frozen. Contract is designed to absorb a semantic sibling later (`14`, RM-04) — keep the UI agnostic to which backing search is used.

**Acceptance criteria**
- [ ] Search box issues `?q=`; results ranked + paginated, owner-scoped
- [ ] Empty / loading / error states handled
- [ ] UI does not assume full-text vs semantic backing (forward-compatible with RM-04)
- [ ] Component tests for query + results rendering

**Definition of Done**: warnings-as-errors clean; server calls only via the generated Kiota client (ADR-011); loading/empty/error states handled and `ProblemDetails` surfaced (`03`); tokens handled per `05` (never logged); accessible (labelled controls, keyboard reachable); component tests present (bUnit, `12`).

_Refs: 03 (Search), 14 (RM-04), 12_
FILER_EOF
  )"

echo "Done. 3 milestones, 3 epics, 13 slices. Add them to the Project board — see README-backlog.md."
