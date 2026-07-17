# /docs/15-frontend-architecture.md

# Frontend Architecture & Conventions

## Purpose

Captures the frontend conventions as built by FE-M1/FE-M2 — the client project
layout, the API-access seam, auth plumbing, page/component patterns, the
design-token contract, and the frontend testing idioms. ADR-012 deliberately deferred
this document until real components existed ("just-in-time capture, not
pre-authored"); FE-M2 shipped ~13 pages and components, so these conventions are
now real and this document records them. Where `10` defines the backend's
physical shape, this document does the same for `src/Clients/`.

Related documents: `03-api-specification.md` (problem-details contract, `code`
extension), `05-security.md` (JWT, ownership → 404), `06-ai-analysis-pipeline.md`
(async upload contract the UI renders), `10-solution-structure.md` (client
boundary rule), `12-testing-strategy.md` (component-test tier),
`13-code-quality-and-design.md` (DoD, anti-anticipation). Related decisions:
ADR-001 (Blazor), ADR-011 (Kiota typed client), ADR-012 (web-first, parallel
start), ADR-016 (hand-rolled design system on tokens).

---

## Decisions This Document Encodes

* **Clients consume the REST API only, through `Filer.ApiClient`.** No client
  project references a module, a `*.Contracts` project, or a kernel (`10`).
* **Pages talk to seams, not to the wire.** Every API surface the UI consumes
  is wrapped in a small `IXxxService` interface returning result records; the
  Kiota request builders never appear in a `.razor` file.
* **One error shape everywhere.** Every failure becomes a `ProblemDetailsView`
  rendered by shared components; UI logic that must branch on a failure keys
  off the machine `code`, never off title/detail text (`03`).
* **Tokens are the only source of visual values** (ADR-016). Components
  reference `tokens.css` custom properties; no raw colour, spacing, or font
  anywhere else.
* **Tests drive the real client against a stubbed transport** for services,
  and bUnit with scriptable fakes for components — no mocking frameworks.

---

## Client Projects

```
src/Clients/
├── Filer.ApiClient/            # typed API client (netstandard-agnostic of UI)
│   ├── Generated/              # Kiota output — never edited by hand (ADR-011)
│   ├── openapi/v1.json         # committed OpenAPI snapshot (CI drift gate)
│   ├── Auth/                   # token store + bearer/refresh handlers
│   └── FilerApiClientServiceCollectionExtensions.cs   # AddFilerApiClient(baseUri)
├── Filer.Ui/                   # Razor Class Library — everything reusable
│   ├── Auth/                   # IAuthSession seam, AuthenticationStateProvider
│   ├── Components/             # shared components (states, upload, badges…)
│   ├── Documents/ Folders/ Tags/   # one folder per API surface: seam + records
│   ├── Formatting/             # pure display helpers (ByteSize)
│   ├── Layout/                 # MainLayout, NavMenu
│   ├── Models/                 # ProblemDetailsView + ApiException mapping
│   ├── Pages/                  # routable pages
│   └── wwwroot/                # tokens.css, app.css, filer-ui.js
└── Filer.Web/                  # Blazor WASM host — composition root
    ├── Program.cs              # DI wiring only, no logic
    ├── App.razor               # Router + AuthorizeRouteView + NotFoundPage
    └── Auth/                   # LocalStorageTokenStore (browser-specific)
```

Dependency direction: `Filer.Web ──▶ Filer.Ui ──▶ Filer.ApiClient`. The RCL
holds everything a future MAUI Blazor Hybrid shell (RM-02) would reuse;
`Filer.Web` contains only what is browser-specific (localStorage token store,
the WASM host, `index.html`). This boundary is compiler-enforced only —
`Filer.Architecture.Tests` does not load client assemblies (`10`, open item).

Platform notes that cost a debugging session each:

* **.NET 10 requires `Router.NotFoundPage` to be routable** — `NotFound.razor`
  carries `@page "/not-found"` even though it is only reached via the router.
* **Browser file streams are async-only.** `IBrowserFile.OpenReadStream`
  returns a stream whose synchronous `Read` throws. Never hand it to a consumer
  that may read synchronously (Kiota's `MultipartBody` does); buffer it with
  `CopyToAsync` first. Upload sizes are capped (`UploadRules`), so buffering is
  bounded.

---

## The Typed API Client (ADR-011 in practice)

The generated client under `Filer.ApiClient/Generated` is regenerated from the
committed OpenAPI snapshot — commands and the drift-gate workflow live in
`src/Clients/Filer.ApiClient/README.md`. Conventions on top of the generator:

* **Generated models are the client's DTOs.** Pages and services use response
  models (`DocumentMetadataResponse`, …) freely; request *builders* stay inside
  services.
* **Merge-patch nulls go through `AdditionalData`.** Kiota's writer skips null
  typed properties, so an explicit `"folderId": null` (move to root, `03`) is
  sent as `body.AdditionalData["folderId"] = null!`.
* **Extension members may arrive boxed.** Kiota's JSON parser boxes GUID-shaped
  extension strings as `Guid`; `ApiExceptionProblemExtensions.ReadString`
  handles `UntypedString`/`string`/`Guid`. Add cases there rather than parsing
  at call sites.
* The API serializes numbers strictly (ADR-011 addendum), so generated numeric
  properties are typed — if a regeneration produces `UntypedNode` members, the
  OpenAPI snapshot has drifted back to union types; fix the API, not the client.

### Auth handlers (`Filer.ApiClient/Auth`)

`AddFilerApiClient(baseUri)` wires an `HttpClient` pipeline in front of Kiota:

* **`BearerTokenHandler`** attaches the access token **only to requests
  targeting the API origin** (#168) — a redirect or absolute URL elsewhere never
  leaks the bearer. On a 401 it asks the refresher once, then replays.
* **`TokenRefresher`** returns a `TokenRefreshResult` (#167): `Refreshed`,
  `Rejected` (the server said 401/403 — the session is dead, clear tokens), or
  `TransientFailure` (network/5xx — keep the session, surface the error). Only
  a rejection logs the user out; flaky Wi-Fi must not.
* **`ITokenStore` and `ITokenRefresher` are singletons** (#166):
  `IHttpClientFactory` builds handler chains in its own DI scope, so a scoped
  store would give the handler a different instance than the one the UI's
  `AuthenticationStateProvider` subscribes to, and its `Changed` event would
  never reach the UI.

---

## The UI Service Seam

Every API surface gets a small interface in `Filer.Ui` plus result records.
This is the load-bearing frontend pattern — pages stay renderable with a fake,
and the wire details (Kiota quirks, problem parsing) live in exactly one class
per surface.

```csharp
public interface IDocumentsService
{
    Task<DocumentsPageResult> ListAsync(DocumentsQuery query, CancellationToken ct = default);
    // Upload / GetMetadata / Update / Download / Delete / tag operations…
}

/// <summary>Exactly one side is set.</summary>
public sealed record DocumentsPageResult(
    PagedResultOfDocumentListItemResponse? Page, ProblemDetailsView? Problem);
```

Rules:

* **Result records carry either the payload or a `ProblemDetailsView`, never
  both, never neither.** Operations with no payload return
  `Task<ProblemDetailsView?>` (`null` = success).
* The implementation wraps every call in `try { … } catch (ApiException ex)
  { … ex.ToProblemView() … }`. A null body on a 2xx becomes a problem too
  ("The server returned an empty response") — callers never null-check payloads
  on the success path.
* Seams are **scoped** DI registrations in `Filer.Web/Program.cs`; the token
  store and refresher are the only singletons (see above).
* Pure client-side logic that supports a seam lives beside it as plain classes
  (`UploadRules`, `FolderTree`) and is unit-tested directly.
* `IAuthSession` is the same pattern for auth flows: login, register (then
  auto-login), best-effort logout, profile. Pages never touch the token store.

---

## Errors on the Client

`ProblemDetailsView` (`Filer.Ui/Models`) is the client-side view of the RFC 7807
contract (`03`) — deliberately *not* a server DTO shared into the client:

* `Code` carries the machine-readable `code` extension (#169). **Branching on a
  failure keys off `Code`**; title and detail are display-only.
* `IsNotFound` folds the ownership rule (`05`): a 404 means "missing or not
  yours" and renders as a calm not-found (`ErrorState` with
  `NotFoundTitle`/`NotFoundDescription`), never as a scary error.
* Field-level validation errors (`Errors`) render under the matching inputs via
  `ProblemDetailsDisplay`.

Three shared components cover every load lifecycle: `LoadingIndicator`,
`ErrorState` (optionally with an `OnRetry` callback), `EmptyState`. Pages
compose them in the same order — loading → problem → empty → content — so
every screen degrades identically.

---

## Page & Component Conventions

* **URL-driven list state.** Filters and pagination are query parameters
  (`[Parameter, SupplyParameterFromQuery]`); changing them navigates via
  `Navigation.GetUriWithQueryParameters`, and `OnParametersSetAsync` reloads
  (deduped by comparing a composite key of the current query). Deep links and
  the back button work for free; folder and tag screens link into the filtered
  documents list this way.
* **Action scaffolding.** Mutations run through a page-local
  `RunActionAsync(Func<Task>)` that sets a `_busy` flag (disables buttons),
  clears the previous notice/problem, and restores `_busy` in `finally`.
  No-op guards (unchanged name, same folder) live *inside* the lambda.
* **Destructive actions confirm in place, in two steps.** The first click swaps
  the button for a confirm/cancel pair — no browser `confirm()`, no dialog yet.
  The confirming button carries a stable `*-confirm-yes` CSS class, which is
  also the bUnit selector convention.
* **Outcome notices** are `<p class="action-notice" role="status"
  aria-live="polite">` — announced by screen readers, styled once in `app.css`.
* **Async upload UX** (`06`): `DocumentUpload` posts, renders the returned
  status immediately, then polls metadata (`PollInterval`/`MaxPollAttempts`
  parameters — tests shrink them) to surface Uploaded → Analyzing →
  Ready/Failed on `DocumentStatusBadge`. `UploadRules` mirrors the server's
  allow-list and size cap client-side and reuses the server's error codes
  (`file_too_large`, `unsupported_file_type`) so pre-checks and server refusals
  render identically. The server stays authoritative.
* **Auth-gated pages** carry `[Authorize]`; `AuthorizeRouteView` sends
  anonymous visitors through `RedirectToLogin`, which preserves the intended
  URL as `returnUrl`. `Login` only honours local, absolute-path return URLs —
  never protocol-relative or cross-origin values (open-redirect safety).
* **Accessibility is per-screen DoD** (ADR-016): every control labelled,
  keyboard reachable, `:focus-visible` ring from tokens, status changes
  announced via `role="status"`.

---

## Design System (ADR-016 operational rules)

* **`Filer.Ui/wwwroot/css/tokens.css` is the single source of visual
  primitives** — colour (surfaces/text, primary, semantic success/warning/
  danger with `-soft` tinted pairs, focus ring), typography (`--font-sans`,
  `--font-brand`), spacing scale, radius, elevation, layout. The identity is
  "Registre" (#178): warm paper neutrals, bottle-green primary.
* **Both themes ship.** Dark ("warm ink") follows the OS via
  `prefers-color-scheme`; an explicit `data-theme="light|dark"` on `<html>`
  overrides it both ways (future in-app toggle). `color-scheme` flips with the
  theme so native controls follow. Components never put a colour inside a media
  query — they consume tokens, and only `tokens.css` redefines them.
* **Changing the identity = editing token values only.** If a restyle requires
  touching a component, that component broke the contract.
* **`app.css` (in the RCL) styles shared *elements* once**: inputs, selects,
  labels, `.btn`/`.btn-primary`/`.btn-danger`, validation messages,
  `.action-notice`, the auth card. Scoped `.razor.css` files hold layout and
  composition for their component only — when the same rule appears in a second
  scoped file, it moves to `app.css`.
* **No component library; overlays use native primitives** — `<dialog>` and the
  popover API when dialogs/menus/toasts arrive (ADR-016).
* **JS interop is a last resort.** One namespace (`filerUi` in
  `wwwroot/js/filer-ui.js`), currently a single function: `downloadFile`
  (blob + anchor, because a bearer-authenticated download can't be a plain
  `<a href>`). Anything expressible in Blazor stays in Blazor.

---

## Frontend Testing Idioms

`Filer.Ui.Tests` complements `12` (component tier). The patterns:

* **Services: real Kiota client, stubbed transport.** `StubHttpMessageHandler`
  enqueues canned responses and records requests; tests assert at the wire —
  query strings, `PATCH` bodies (including the literal `"folderId":null`),
  multipart content, problem-details parsing. This catches serializer quirks a
  mocked client would hide; the async-only-stream upload bug is the canonical
  example (regression-tested with a stream whose sync `Read` throws).
* **Components and pages: bUnit (`BunitContext`) + scriptable fakes.** Fakes
  implement the seam with result queues and recorded calls
  (`FakeDocumentsService`, `FakeAuthSession`, …) — no mocking framework.
* **Query-string parameters are set by navigating**, not by
  `ComponentParameter`: `Services.GetRequiredService<BunitNavigationManager>()`
  → `NavigateTo("documents?page=2")` *before* `Render<T>()` —
  `SupplyParameterFromQuery` binds from the URI.
* **Async settles are awaited with `WaitForAssertion`**; polling components
  take short `PollInterval`s as parameters instead of tests sleeping.
* **JS interop runs loose** (`JSInterop.Mode = JSRuntimeMode.Loose`) with
  `VerifyInvoke` where the call *is* the behaviour (download).
* **Selectors are stable CSS classes**, not markup structure — `.action-notice`,
  `*-confirm-yes`, component-scoped classes. Renaming a class means updating
  its tests, which is the point: the class is a contract.

Every UI slice ships with: seam tests (success + each problem path), component
tests for each visual state, and page tests for the load lifecycle and each
action (`12`).

---

## Open Items

* `Filer.Architecture.Tests` does not yet load client assemblies; the
  REST-only client boundary is compiler-enforced only (`10`).
* The in-app theme toggle that stamps `data-theme` is not built; dark mode is
  OS-driven only.
* Test-helper records (`UploadResult`, `TagDto` shapes) are duplicated across
  test files — consolidation tracked in #195.
* FE-M3 (AI-suggestions review UI, search UI) is deferred until the M5/M6
  contracts settle (ADR-012); both are additive panels on existing screens.
* The MAUI Blazor Hybrid shell (RM-02) will consume `Filer.Ui` as-is; anything
  browser-specific added to the RCL by mistake will surface then.
