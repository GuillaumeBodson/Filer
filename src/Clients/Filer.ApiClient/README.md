# Filer.ApiClient

Typed C# client for the Filer API, **generated with [Kiota](https://learn.microsoft.com/openapi/kiota/)**
from the API's OpenAPI document (ADR-011). The browser and the future MAUI shell
(RM-02) call the server **only** through this client — no hand-rolled `HttpClient`,
no server DTOs shared into clients.

## Layout

| Path | What |
|------|------|
| `openapi/v1.json` | Committed snapshot of the API contract — the generation input. |
| `Generated/` | Kiota output (`FilerApiClient` + request builders + models). **Do not edit by hand.** |
| `Generated/kiota-lock.json` | Records the spec hash, Kiota version and generation options — used to detect drift. |
| `.editorconfig` | Excludes `Generated/**` from the solution-wide warnings-as-errors (kept outside `Generated/` so `--clean-output` can't delete it). |
| `FilerApiClientServiceCollectionExtensions.cs` | `AddFilerApiClient(baseAddress)` — hand-written DI registration (stays strict). |

## Generation is checked in

The generated client **and** its input snapshot are committed (chosen over
generate-on-build so CI needs neither a running API nor PostgreSQL). A contract change
reaches the client in two steps: refresh the snapshot, then regenerate. CI fails if a
contract change lands without regenerating (see **Drift gate** below), satisfying
ADR-011 / #126 — *"a contract change that breaks the client fails the build."*

## Regenerate

Prerequisite (once per clone): `dotnet tool restore` (installs the pinned Kiota version
from `.config/dotnet-tools.json`).

**1. Refresh the contract snapshot** (only when the API changed). Run the API and save
its OpenAPI document:

```bash
docker compose up -d postgres
dotnet run --project src/Filer.Api          # http profile → http://localhost:5232
curl -s http://localhost:5232/openapi/v1.json -o src/Clients/Filer.ApiClient/openapi/v1.json
```

**2. Regenerate the client** from the snapshot (offline — no API needed):

```bash
dotnet tool run kiota generate \
  -l CSharp \
  -d src/Clients/Filer.ApiClient/openapi/v1.json \
  -c FilerApiClient \
  -n Filer.ApiClient.Generated \
  -o src/Clients/Filer.ApiClient/Generated \
  --clean-output
```

Commit the resulting changes under `openapi/` and `Generated/`.

## Drift gate (CI, wired in #129)

CI runs `dotnet tool restore` then the **step 2** command above and fails if the working
tree changed:

```bash
git diff --exit-code -- src/Clients/Filer.ApiClient/Generated
```

A non-empty diff means the committed client no longer matches the snapshot — regenerate
and commit. (`Generated/.kiota.log` is git-ignored because it is timestamped per run.)

## Usage

Registered by the host with a per-environment base address; inject `FilerApiClient`:

```csharp
// host startup (e.g. Filer.Web/Program.cs)
builder.Services.AddFilerApiClient(new Uri(builder.Configuration["FilerApi:BaseAddress"]!));

// in a component / service
public sealed class Example(FilerApiClient api)
{
    public Task RegisterAsync(RegisterRequest body, CancellationToken ct) =>
        api.Api.V1.Auth.Register.PostAsync(body, cancellationToken: ct);
}
```

Authentication is currently anonymous; the bearer-token provider, token store and 401
refresh are added in **#128**.
