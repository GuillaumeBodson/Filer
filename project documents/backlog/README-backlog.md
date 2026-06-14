# Filer — GitHub backlog

Auto-generated from the canonical docs in `project documents/`. Tickets are
**vertical slices** (endpoint + service + DTOs + validation + tests, per `10`),
grouped into **epics** and sequenced by the build order in `08`
(Auth → Upload → Folders/Tags → AI → Search → Observability).

Each ticket carries acceptance criteria drawn from the docs — ownership→404
(`05`), async upload + job lifecycle (`06`), and the Definition of Done (`12`/`13`).

## Files

| File | What it is |
|------|------------|
| `create-github-issues.sh` | Creates all labels, milestones, and 45 **backend** issues via the `gh` CLI. |
| `issues.csv` | The same 45 backend tickets as a spreadsheet (Title, Milestone, Labels, Body). |
| `create-frontend-issues.sh` | Creates the `module:web` label, 3 frontend milestones, and 16 **frontend** issues. |
| `README-backlog.md` | This file. |

## What gets created

- **7 milestones** = the build phases (M1 Foundation … M7 Observability & CI).
- **15 labels** — `epic`, `type:*` (feature/infra/test/chore), `module:*` (auth, documents, storage, jobs, folders, tags, ai, search, platform, ci).
- **45 issues** — 7 epics + 38 slice/infra tickets. Counts per phase: M1 6, M2 7, M3 9, M4 11, M5 6, M6 2, M7 4.

## Frontend backlog (`create-frontend-issues.sh`)

The web frontend is planned separately, per ADR-001 (Blazor), ADR-011 (Kiota
client generation), and ADR-012 (start in parallel, web-first, against the frozen
core endpoints; mobile deferred to RM-02). `create-frontend-issues.sh` creates:

- **3 milestones** — `FE-M1 Frontend foundation`, `FE-M2 Core document workflow
  (web)`, `FE-M3 AI suggestions & search UI`.
- **1 new label** — `module:web` (reuses the existing `epic`/`type:*` labels).
- **16 issues** — 3 epics + 13 slices. Per milestone: FE-M1 6, FE-M2 7, FE-M3 3.

Sequencing baked into the tickets (ADR-012): FE-M1/FE-M2 run now against the
frozen auth/documents/folders/tags endpoints; **FE-M3 is blocked** until the
backend M5 analysis (#54/#55) and M6 search contracts freeze. The search UI is
kept agnostic to full-text vs semantic backing so RM-04 (`14`) can slot in later.
The MAUI mobile shell is **not** in this backlog — it belongs to RM-02.

## Run it

Prerequisites: [`gh`](https://cli.github.com/) installed and authenticated (`gh auth login`), run from inside the repo.

```bash
# from the repo root (auto-detects the repo)
bash "project documents/backlog/create-github-issues.sh"      # 45 backend issues
bash "project documents/backlog/create-frontend-issues.sh"    # 16 frontend issues

# or target an explicit repo
REPO=GuillaumeBodson/Filer bash "project documents/backlog/create-github-issues.sh"
```

Both scripts are safe to re-run for labels/milestones (labels use `--force`,
milestones are skipped if they already exist). **Issues are *not* deduplicated** —
running a script twice creates duplicates. Run each once.

## Turn it into a kanban board (GitHub Projects)

`gh` creates the issues; GitHub **Projects** gives you the board. Two ways:

**UI (simplest):** repo → *Projects* → *New project* → *Board* template. Add a
**Status** field with columns `Todo / In Progress / In Review / Done`, then bulk-add
issues (the board's *+ Add item* accepts `#` search, or filter by `is:issue is:open`).
Group the board by **Milestone** to see the phases, or by **module:** label to see workstreams.

**CLI (optional):** Projects v2 lives at the user/org level.

```bash
OWNER=GuillaumeBodson
gh project create --owner "$OWNER" --title "Filer V1"
# note the returned project number, then add issues:
gh project item-add <NUMBER> --owner "$OWNER" --url https://github.com/$OWNER/Filer/issues/1
# ...repeat per issue, or script a loop over `gh issue list --json url -q '.[].url'`
```

Recommended board setup: **group by Milestone** (your phases become swimlanes),
filter chips per `module:` label, and let the epics act as tracking issues —
tick their checklist as the child slices close.

## Notes / decisions to make first

- **`Folders: delete (non-empty semantics)`** is intentionally blocked on a
  decision (reject vs cascade vs move-to-parent). Record an ADR in `09` before
  implementing it.
- Build order puts the **storage abstraction** and the **job queue/worker** at the
  top of M3 because the upload slice depends on both — that's a slight reordering
  of the `08` list (which names "Upload pipeline" before "File storage
  abstraction"), kept dependency-correct here.
- Folders and Tags are separate modules per `10`; revisit a possible merge after
  the first slices land.
