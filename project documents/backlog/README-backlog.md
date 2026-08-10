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
| `create-ops-issues.sh` | Creates 2 `OPS-M*` milestones and 13 **deployment/operations** issues. |
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

## Ops backlog (`create-ops-issues.sh`)

Deployment and operations of the self-hosted node, per ADR-018.
`create-ops-issues.sh` creates:

- **2 milestones** — `OPS-M1 Self-hosted deployment & CD`,
  `OPS-M2 Operability: backups & runtime hardening`.
- **0 new labels** — reuses `epic`, `type:*`, `module:platform`, `module:ci`,
  `module:ai`.
- **13 issues** — 2 epics + 11 slices. Per milestone: OPS-M1 8, OPS-M2 5.

**No script creates the milestone-review issues**, this one included. Creating a
milestone fires `.github/workflows/milestone-review.yml`, which opens one from
`.github/ISSUE_TEMPLATE/milestone-review.md` — so every milestone gets the same
checklist. Adding them to a generator produces duplicates.

`OPS-` is its own prefix rather than a continuation of `M*` because this is a
**parallel track**, not a sequel: it blocks no feature milestone and is blocked
by none. Numbering it `M12` would falsely imply it queues behind semantic search.

Two OPS-M1 tickets are not infrastructure at all — they are defects in the Ollama
adapter that the first real deployment exposed (reasoning left enabled, context
window left to the runtime default). They sit here because the deployed system is
not usable without them, and they are the reason the milestone is not purely
`type:infra`.

## Run it

Prerequisites: [`gh`](https://cli.github.com/) installed and authenticated (`gh auth login`), run from inside the repo.

```bash
# from the repo root (auto-detects the repo)
bash "project documents/backlog/create-github-issues.sh"      # 45 backend issues
bash "project documents/backlog/create-frontend-issues.sh"    # 16 frontend issues
bash "project documents/backlog/create-ops-issues.sh"         # 13 ops issues

# or target an explicit repo
REPO=GuillaumeBodson/Filer bash "project documents/backlog/create-github-issues.sh"
```

All three scripts are safe to re-run for labels/milestones (labels use `--force`,
milestones are skipped if they already exist). **Issues are *not* deduplicated** —
running a script twice creates duplicates. Run each once.

## Turn it into a kanban board (GitHub Projects)

`gh` creates the issues; GitHub **Projects** gives you the board. Two ways:

**UI (simplest):** repo → *Projects* → *New project* → *Board* template. Add a
**Status** field with columns `Todo / In Progress / In Review / Done`, then bulk-add
issues (the board's *+ Add item* accepts `#` search, or filter by `is:issue is:open`).
Group the board by **Milestone** to see the phases, or by **module:** label to see workstreams.

**Keep it filled automatically:** in the project, *⋯ → Workflows → Auto-add to
project* with filter `is:issue,is:open`. Every issue created afterwards lands on
the board on its own — which is what a generator script should rely on instead of
wiring the board itself. It applies only to items created *after* it is enabled,
so an existing backlog still needs one bulk add.

**CLI (optional):** Projects v2 lives at the user/org level, so it needs a token
scope the repo work does not: `gh auth refresh -s project`.

> ⚠️ **`gh project item-add` can exit 0 without adding anything** (observed
> 2026-08-11 against this project: the command succeeded silently, the item count
> never moved). Do not trust its exit code in a loop — verify with
> `gh project item-list`, or call the API, which reports real errors:

```bash
OWNER=GuillaumeBodson
PID=$(gh api graphql -f query="{user(login:\"$OWNER\"){projectV2(number:11){id}}}" \
        --jq '.data.user.projectV2.id')

gh issue list --milestone "<milestone title or number>" --state open --limit 100 \
     --json number --jq '.[].number' |
while read -r n; do
  IID=$(gh api "repos/$OWNER/Filer/issues/$n" --jq .node_id)
  gh api graphql -f query="mutation{addProjectV2ItemById(input:{projectId:\"$PID\" contentId:\"$IID\"}){item{id}}}"
done
```

Recommended board setup: **group by Milestone** — the `M*`, `FE-M*` and `OPS-M*`
tracks become visible as separate swimlanes — filter chips per `module:` label,
and let the epics act as tracking issues, ticking their checklist as the child
slices close.

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
