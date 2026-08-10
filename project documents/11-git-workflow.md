# /docs/11-git-workflow.md

# Git Workflow

This document is the canonical source for how source control is run on Filer.
The project is developed by a single maintainer and hosted on GitHub
(`github.com/GuillaumeBodson/Filer`). The workflow is deliberately lightweight:
just enough process to keep `main` releasable and history readable, without the
ceremony of team-oriented models like GitFlow.

---

## Principles

* **`main` is always green.** It must build (warnings-as-errors) and pass
  `dotnet test` at every commit. Nothing merges that breaks it.
* **`main` is always deployable.** Any commit on `main` can ship. Work in
  progress lives on branches, never on `main`.
* **History tells a story.** One readable commit per logical change. The *why*
  belongs in the commit body and the PR, not just the diff.

---

## Branching — trunk-based

Short-lived feature branches off `main`, merged back fast. No long-running
`develop` or `release` branches.

* Branch naming: `<type>/<issue#>-<short-description>` when the work implements a
  tracked issue, e.g. `feat/40-folders-create`, `fix/96-ownership-stub`;
  `<type>/<short-description>` only for untracked work (e.g. `chore/bump-efcore`).
* `<type>` mirrors the Conventional Commit types below.
* Keep branches small and short-lived (hours to a few days). Rebase on `main`
  rather than letting a branch drift for weeks.

---

## Commits — Conventional Commits

Format: `<type>(<optional scope>): <summary>`. Already in use in the history
(e.g. `feat(auth): implement JWT authentication`).

| Type       | Use for |
|------------|---------|
| `feat`     | A user-facing feature |
| `fix`      | A bug fix |
| `refactor` | Code change that neither fixes a bug nor adds a feature |
| `perf`     | Performance improvement |
| `test`     | Adding or fixing tests |
| `docs`     | Documentation only (incl. `project documents/`) |
| `chore`    | Tooling, deps, CI, build — no production code change |
| `build`    | Build system / packaging |

Scope is the module or area when useful: `auth`, `storage`, `ai`, `api`, `ci`.
Summary in imperative mood, ≤ ~72 chars, no trailing period. Put the *why* and
any context in the body. Reference issues with `Closes #N` when applicable.

---

## Pull Requests — self-review, even solo

Every change lands through a PR, even though there is one developer.

* **Why bother solo:** the PR diff is the review surface, CI gates the merge, and
  the PR becomes the searchable record of *why* a change happened.
* Use the PR template (`.github/pull_request_template.md`) as the checklist.
* **Link the issue — not optional.** Every PR that implements a tracked issue
  carries a closing keyword (`Closes #N`, `Fixes #N`) **in the PR description**.
  Keywords in commit messages are not reliable with squash merge; the PR body is
  what GitHub uses to auto-close. One PR can close several issues
  (`Closes #45, closes #46`).
* **Verify the link landed.** After merge, confirm the issue actually closed —
  a missing keyword leaves it silently open (this happened with #96/PR #97 and
  was only caught by the M4 milestone review). The milestone review checks for
  open issues whose PRs already merged.
* **Squash merge** so each feature is one clean commit on `main` instead of
  "wip / fix typo / actually fix" noise. The PR title becomes the squash commit
  message — keep it a valid Conventional Commit.
* Delete the branch after merge.

---

## Continuous Integration

`.github/workflows/ci.yml` runs on every push to `main` and every PR:
`dotnet tool restore` → **Kiota client drift gate** → `dotnet restore` →
`dotnet build` (Release, warnings-as-errors) → `dotnet test` with coverage →
coverage report + **coverage gate** (80% line / 70% branch per module,
`build/coverage-gate.ps1` — thresholds and scope in `12`). A PostgreSQL 17
service mirrors `docker-compose.yml` so integration tests can run against a real
database; `Filer.Architecture.Tests` enforce module boundaries in the same run.
The build/test pass also covers the frontend — the Blazor WASM host and shared
RCL build under warnings-as-errors and the bUnit component tests run with the
rest (`12`). The drift gate regenerates the typed API client from its committed
OpenAPI snapshot and fails the build if it no longer matches the checked-in
`Generated/` output (ADR-011; see `src/Clients/Filer.ApiClient/README.md`).
A second job, `docker-build`, builds the deployable API image. On a pull request
that is the whole point — proof it still builds. On `main` and on release tags
the job also **pushes the image to GHCR** (ADR-018), and that published image is
what the deployment node runs; it is never rebuilt on the host. No floating
`latest` tag is published, so a deployment always names an exact version.

The job keeps the id `docker-build` deliberately: it is a required status check
below, and renaming it would leave `main` unprotected until the rule is
re-pointed — silently, since a rule naming a check that no longer exists blocks
nothing.

The `build-test` and `docker-build` checks are the gates referenced by branch
protection.

---

## Branch protection (configure on GitHub)

On `github.com/GuillaumeBodson/Filer` → **Settings → Branches → Add rule** for
`main`:

* Require a pull request before merging.
* Require status checks to pass before merging → select **`build-test`** and
  **`docker-build`**.
* Require branches to be up to date before merging.
* (Optional) Allow the admin to bypass for emergencies — the value is the
  required green CI, not blocking yourself.

This makes it impossible to push broken code straight to `main`.

---

## Releases & tagging

Policy adopted 2026-07-31; baseline **`v0.10.0`** = V1 scope complete
(M1–M7 + FE-M1–FE-M3).

* **One annotated tag per closed milestone**, on the merge commit of that
  milestone's review/cleanup PR — the moment the milestone's code is fully on
  `main`. Bump **minor** per milestone (`v0.11.0`, `v0.12.0`, …), whichever
  track (M-x, FE-M-x or OPS-M-x) closes; tagging is a checkbox on the
  milestone-review issue so it can't be forgotten.
* Tag numbers do **not** mirror milestone numbers (the tracks close in
  arbitrary order). The mapping lives in the tag message:
  `git tag -a v0.11.0 -m "M8 — Bulk operations"`.
* **Patch** bumps only for a fix on top of an already-tagged state.
* Tags are the milestone-review diff anchor (`git diff <previous-tag>..HEAD`)
  and deployment/rollback references for the API.
* **`v1.0.0` is a product decision, not a milestone counter** — cut it when
  Filer is functionally a 1.0. Until then everything stays 0.x.
* **GitHub Releases are deliberately not used** while the project has no
  external consumers (solo development; deployment runs off the tag, not off a
  Release). Revisit when versions gain an audience.

### Tags now deploy (ADR-018)

Since ADR-018 the tag is not only a reference — **pushing it is the
deployment**. Publishing `v*` runs the full CI pipeline, publishes the image
under that version, and triggers `cd.yml`, which points the node at it and waits
for the container to report healthy.

Two consequences of combining that with the per-milestone policy above:

* **Deployment cadence is milestone cadence.** The node advances when a
  milestone closes, not when a PR merges — a deliberate consequence of tagging
  per milestone, not an accident of the pipeline. A fix that must reach the node
  sooner is exactly the **patch bump** the policy already allows.
* **The tagging checkbox on the milestone-review issue is now also a deploy
  trigger.** Ticking it ships. That is the intended coupling — a milestone is
  not closed until its code is running — but it means the tag is no longer a
  bookkeeping act that can be done absent-mindedly.

Rolling back is re-running `cd.yml` with an earlier tag; the deployed version is
pinned in the node's `.env`, which is why no floating `latest` is published.
Migrations apply at container startup, so a release carrying a bad migration
needs a **restore**, not a re-pin — back up before deploying one (`04`).

---

## Secrets & supply chain

* **Never commit secrets.** `Jwt__SigningKey` (≥32 chars), real connection
  strings, and keys come from env or a secret store (`05-security.md`). Only the
  dev key in `appsettings.Development.json` is allowed in source control.
* **This repository is public**, which constrains what may live in `deploy/`.
  The deployment contract is committed; host build logs are not — they name a
  specific machine (NIC addresses, LAN topology, hostnames) and buy an attacker
  reconnaissance for no benefit. `.gitignore` enforces the split rather than
  leaving it to discipline.
* **Deployment credentials are scoped, not shared.** The CD workflow holds a
  private-network credential that can reach exactly one host on exactly one port,
  and no long-lived SSH key (ADR-018). Repository *variables* carry the deploy
  host and user so the workflow file names neither.
* `.gitignore` excludes `appsettings.*.local.json`, `secrets.json`, `.env*`, and
  IDE/user files.
* **Enable on GitHub:** Settings → Code security → secret scanning + push
  protection, and Dependabot alerts.
* `.github/dependabot.yml` opens weekly dependency PRs against the centralised
  `Directory.Packages.props` and keeps the CI Actions patched.

---

## Quick reference

```bash
# Start work (issue number in the branch name)
git switch -c feat/77-file-upload-endpoint

# Commit (conventional)
git commit -m "feat(storage): add async upload endpoint"

# Publish & open PR — "Closes #77" goes in the PR body, not just commits
git push -u origin feat/77-file-upload-endpoint
gh pr create --title "feat(storage): add async upload endpoint" \
  --body "... Closes #77"
# let CI run, squash-merge, delete branch, VERIFY #77 closed

# Tag a milestone close (minor bump; message names the milestone)
git tag -a v0.11.0 -m "M8 — Bulk operations" && git push origin v0.11.0
```
