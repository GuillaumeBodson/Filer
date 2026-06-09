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
`dotnet restore` → `dotnet build` (Release, warnings-as-errors) →
`dotnet test`. A PostgreSQL 17 service mirrors `docker-compose.yml` so
integration tests can run against a real database; `Filer.Architecture.Tests`
enforce module boundaries in the same run.

The `build-test` check is the gate referenced by branch protection.

---

## Branch protection (configure on GitHub)

On `github.com/GuillaumeBodson/Filer` → **Settings → Branches → Add rule** for
`main`:

* Require a pull request before merging.
* Require status checks to pass before merging → select **`build-test`**.
* Require branches to be up to date before merging.
* (Optional) Allow the admin to bypass for emergencies — the value is the
  required green CI, not blocking yourself.

This makes it impossible to push broken code straight to `main`.

---

## Releases & tagging

* Tag meaningful points with SemVer: `v0.1.0`, `v0.2.0`, …
* Tags give deployment references and rollback points for the API.
* Optionally use GitHub Releases with auto-generated notes (Conventional Commit
  history makes these clean).

---

## Secrets & supply chain

* **Never commit secrets.** `Jwt__SigningKey` (≥32 chars), real connection
  strings, and keys come from env or a secret store (`05-security.md`). Only the
  dev key in `appsettings.Development.json` is allowed in source control.
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

# Tag a release
git tag -a v0.1.0 -m "v0.1.0" && git push origin v0.1.0
```
