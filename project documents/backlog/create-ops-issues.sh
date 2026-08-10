#!/usr/bin/env bash
# OPS backlog creator for Filer — the deployment/operations track.
#
# Third track alongside create-github-issues.sh (45 V1 backend tickets) and
# create-frontend-issues.sh (16 web tickets). It is a parallel track, not a
# sequel: OPS work blocks no feature milestone and is blocked by none, which is
# why it carries its own prefix rather than continuing the M* numbering.
#
# Source: ADR-018 (deployment artifact, repo-owned deploy assets, tag-triggered
# CD) and the gaps the first real deployment surfaced — see the "LLM runtime"
# note in 09-decision-log.md.
#
# Requires: gh (authenticated via `gh auth login`) run from inside the repo.
# Usage:  bash create-ops-issues.sh   (override target with REPO=owner/name)
# Safe to re-run for labels/milestones (idempotent); issues are NOT deduplicated — run once.
#
# Note: this script does NOT create the "Milestone review" issues. Creating a
# milestone fires .github/workflows/milestone-review.yml, which opens one from
# .github/ISSUE_TEMPLATE/milestone-review.md — so every milestone gets the same
# review checklist. Creating them here produced duplicates (#255, #262).
set -euo pipefail
REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
echo "Target repo: $REPO"

echo "== Labels =="
# All reused from the backend script; nothing new is needed for this track.
gh label create "epic" --color "6f42c1" --description "Epic / tracking issue" --repo "$REPO" --force
gh label create "type:feature" --color "0e8a16" --description "User-facing feature slice" --repo "$REPO" --force
gh label create "type:infra" --color "1d76db" --description "Platform / infrastructure work" --repo "$REPO" --force
gh label create "type:test" --color "fbca04" --description "Testing / quality gate" --repo "$REPO" --force
gh label create "type:chore" --color "c2e0c6" --description "Scaffolding / tooling" --repo "$REPO" --force
gh label create "milestone-review" --color "0E8A16" --description "End-of-milestone cross-slice review" --repo "$REPO" --force

echo "== Milestones =="
existing_ms=$(gh api "repos/$REPO/milestones?state=all" --jq ".[].title" 2>/dev/null || true)
if ! grep -qxF "OPS-M1 — Self-hosted deployment & CD" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="OPS-M1 — Self-hosted deployment & CD" -f description="Turn the hand-run deployment into a pipeline (ADR-018): the API image is built once by CI and published to GHCR, the deploy assets live in deploy/, and a release tag deploys to the self-hosted node over the tailnet with a readiness gate. Includes the two AI-adapter defects the first deployment surfaced, without which the deployed system is not usable." >/dev/null && echo "  + OPS-M1"; else echo "  = OPS-M1 (exists)"; fi
if ! grep -qxF "OPS-M2 — Operability: backups & runtime hardening" <<< "$existing_ms"; then gh api "repos/$REPO/milestones" -f title="OPS-M2 — Operability: backups & runtime hardening" -f description="Make the running deployment survivable: scheduled backups in the mandated order, a restore actually exercised, a copy outside the machine's failure domain, and a decision on where production telemetry lands (04, 07 open questions)." >/dev/null && echo "  + OPS-M2"; else echo "  = OPS-M2 (exists)"; fi

M1="OPS-M1 — Self-hosted deployment & CD"
M2="OPS-M2 — Operability: backups & runtime hardening"

echo "== Issues =="

echo "  [1/13] [EPIC] Self-hosted deployment & CD"
gh issue create --repo "$REPO" \
  --title "[EPIC] Self-hosted deployment & CD" \
  --milestone "$M1" \
  --label "epic" --label "module:platform" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
The first real deployment happened by hand and worked end to end (register → upload → `AnalysisJob` → suggestions). This epic turns that act into a pipeline.

Three gaps it closes, per ADR-018:
- the server **compiled the application**, so "what is running" was whatever the working tree held, and a .NET SDK build competed with the LLM runtime on the same machine;
- the deployment assets lived **outside the repository**, which cannot work once a pipeline needs a versioned compose file moving in lockstep with the image contract;
- there was **no delivery mechanism** — and the node has no inbound ports, while a self-hosted runner is unavailable because this repository is public.

**Acceptance criteria**
- [ ] All OPS-M1 issues closed
- [ ] Pushing a `v*` tag publishes an image and deploys it, with no manual step
- [ ] The deployed version is identifiable by tag, and rolling back is re-running the workflow with an earlier tag
- [ ] A document analysed on the node completes in seconds, not minutes

_Refs: ADR-018, 07, 11, 04_
FILER_EOF
  )"

echo "  [2/13] ADR-018: deployment artifact + repo-owned deploy assets"
gh issue create --repo "$REPO" \
  --title "ADR-018: deployment artifact + repo-owned deploy assets" \
  --milestone "$M1" \
  --label "type:chore" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Record the deployment decision before building the pipeline: what the deployable artifact is, where the deploy assets live, and how a version reaches the node.

It reverses an earlier deliberate choice (deploy files kept out of the repo), so it needs the rationale written down rather than inferred from a diff.

**Acceptance criteria**
- [ ] ADR-018 in `09-decision-log.md`: context, decision, rationale, trade-offs accepted
- [ ] Covers the published-image artifact, the pinned tag, the repo-owned `deploy/`, and the delivery path
- [ ] States what the public repository implies: a public image, no registry credential on the host, and what a move to private would break
- [ ] `07` production section points at it; `11` CI/release sections updated

_Refs: 09, 07, 11_
FILER_EOF
  )"

echo "  [3/13] Deploy assets into the repo: prod compose, env template, runbook"
gh issue create --repo "$REPO" \
  --title "Deploy assets into the repo: prod compose, env template, runbook" \
  --milestone "$M1" \
  --label "type:infra" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Bring the deployment contract into version control under `deploy/`: production compose, `.env` template, backup script, and a runbook.

The runbook must be **machine-agnostic**. The host build log — BIOS, partitioning, driver install, network — stays out: it describes one machine, carries NIC addresses and LAN topology, and this repository is public. Enforce the split in `.gitignore` rather than relying on discipline; note that the blanket `.env*` rule would otherwise swallow the tracked template.

**Acceptance criteria**
- [ ] `deploy/` holds the compose file, `.env.example`, backup script, runbook
- [ ] Runbook covers: what the host must supply, install, deploy, roll back, back up, routine checks
- [ ] Runbook records the traps that cost real time: container UID on the blob root, the two-part reachability of a host-native AI runtime, the firewall rule, the auto-loaded Visual Studio compose override, and never combining dev+prod compose files
- [ ] `.gitignore` excludes the host build log and re-includes `.env.example`
- [ ] No hostname, address, credential, or MAC lands in a tracked file

_Refs: ADR-018, 07, 11_
FILER_EOF
  )"

echo "  [4/13] Prod compose: consume the published image + api healthcheck"
gh issue create --repo "$REPO" \
  --title "Prod compose: consume the published image + api healthcheck" \
  --milestone "$M1" \
  --label "type:infra" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Replace the on-host build with a pinned image reference, and give the api service a real health signal.

Only `postgres` has a healthcheck today, so `up -d` succeeding says nothing about the application: a container looping on a failed migration reports `Up`. `/health/ready` already proves PostgreSQL reachability and a writable blob root — the healthcheck just makes that visible to an orchestrator. The runtime image ships no HTTP client, so one has to be added for the probe.

**Acceptance criteria**
- [ ] `filer.api` uses `image:` with a **required** tag variable — startup fails loudly when unset, rather than defaulting to something floating
- [ ] Container `HEALTHCHECK` against `/health/ready`, with a start period covering startup migrations
- [ ] `docker compose config` renders; the missing-tag guard fires with a usable message
- [ ] Loopback-only publishing and the absent PostgreSQL port are preserved
- [ ] Deploying an unchanged tag is a no-op; deploying a new one replaces the container

_Refs: ADR-018, 07, 04_
FILER_EOF
  )"

echo "  [5/13] CI: publish the API image to GHCR"
gh issue create --repo "$REPO" \
  --title "CI: publish the API image to GHCR" \
  --milestone "$M1" \
  --label "type:infra" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
Extend the existing `docker-build` job: keep build-only on pull requests, push to GHCR on `main` and on release tags.

**Keep the job id `docker-build`.** It is a required status check in branch protection (`11`); renaming it leaves `main` unprotected until the rule is re-pointed, and a rule naming a check that no longer exists blocks nothing.

Publish **no floating tag**. `latest` in circulation invites an unpinned deploy that cannot be reasoned about afterwards — the deployed version must always name an exact build.

**Acceptance criteria**
- [ ] Pull requests build without pushing; fork PRs are unaffected
- [ ] `main` publishes `edge` + a short-sha tag; `v*` publishes the semver tags
- [ ] `latest` is explicitly disabled
- [ ] Layer caching between runs
- [ ] CI triggers include tag pushes — otherwise a release tag never produces its image
- [ ] The published package is pullable without credentials while the repo is public

_Refs: ADR-018, 11_
FILER_EOF
  )"

echo "  [6/13] CD: deploy release tags to the node with a readiness gate"
gh issue create --repo "$REPO" \
  --title "CD: deploy release tags to the node with a readiness gate" \
  --milestone "$M1" \
  --label "type:infra" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
A `cd.yml` that deploys a published tag to the self-hosted node, plus a manual trigger — because the case that matters most is redeploying a tag that already exists, i.e. rolling back.

The node has no inbound ports and will not get any, and a self-hosted runner is off the table on a public repository. The runner therefore joins the node's private network as a short-lived, tagged member and leaves; access is scoped by ACL to one host and one port, and no long-lived SSH key is stored in this repository's secrets.

The API publishes on loopback only — deliberately — so the readiness check runs **on the host**, through the session, not from the runner.

**Acceptance criteria**
- [ ] Triggered by `v*`; manual dispatch takes an explicit tag
- [ ] Fails before touching the host if the image is not published
- [ ] Repins the tag in the host `.env`, pulls, brings the stack up
- [ ] Waits for the container to report **healthy**, not merely started; dumps recent logs on failure
- [ ] Deploy host and user come from repository variables, not the workflow file
- [ ] A job timeout — the node is a home server that is sometimes off, and must fail fast and legibly
- [ ] Concurrency: one deploy at a time, never cancelled midway
- [ ] Rollback exercised at least once end to end

_Refs: ADR-018, 11, 07_
FILER_EOF
  )"

echo "  [7/13] Ollama adapter: disable model reasoning"
gh issue create --repo "$REPO" \
  --title "Ollama adapter: disable model reasoning" \
  --milestone "$M1" \
  --label "type:feature" --label "module:ai" \
  --body "$(cat <<'FILER_EOF'
`OllamaChatRequest` sends no `think` field, so a hybrid-reasoning model emits a long chain of thought before the JSON. Measured on the deployment node: **~3 s per document with reasoning off, 30–104 s with it on** — a 10–30× regression that makes the analysis queue unusable, for output the classifier discards.

This is a defect in the shipped adapter and is independent of any runtime choice (see the "LLM runtime" note in `09`).

Not every model understands the field, so it must be configurable rather than hardcoded — with a default that protects the common case.

**Acceptance criteria**
- [ ] The chat request carries the reasoning switch, off by default
- [ ] Configurable through `AiAnalysisOptions`, validated like its siblings
- [ ] Models that ignore the field still work (no request rejected for sending it)
- [ ] The agentic variant gets the same treatment — it runs two passes, so it pays the cost twice
- [ ] Unit tests assert the field is on the wire; existing provider tests stay green
- [ ] `06` model expectations reflect the shipped behaviour

_Refs: 06, 09 (note "LLM runtime"), deploy/choix-runtime-llm.md_
FILER_EOF
  )"

echo "  [8/13] Ollama adapter: send an explicit context window"
gh issue create --repo "$REPO" \
  --title "Ollama adapter: send an explicit context window" \
  --milestone "$M1" \
  --label "type:feature" --label "module:ai" \
  --body "$(cat <<'FILER_EOF'
The adapter never sends `num_ctx`, so the runtime's default (4096 tokens) applies. `MaxPromptChars` allows 8000 characters (~2500 tokens) **plus** the rendered folder tree, so a large document with a deep folder tree can exceed it.

The failure is silent: no error, no log, just a truncated prompt and quietly worse suggestions. That makes it more dangerous than a loud failure and harder to attribute.

**Acceptance criteria**
- [ ] The request carries an explicit context window, configurable and validated
- [ ] The default is consistent with `MaxPromptChars` plus folder-tree headroom — the two are documented as related, so neither can be raised alone by accident
- [ ] A prompt near the cap is covered by a test
- [ ] Trade-off noted where it will be read: a larger window costs KV-cache memory on a GPU that is already the constraint

_Refs: 06, 04_
FILER_EOF
  )"


echo "  [9/13] [EPIC] Operability: backups & runtime hardening"
gh issue create --repo "$REPO" \
  --title "[EPIC] Operability: backups & runtime hardening" \
  --milestone "$M2" \
  --label "epic" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
OPS-M1 makes deployment repeatable. This epic makes the running deployment survivable: backups that are scheduled, ordered correctly, proven by restore, and stored outside the machine's failure domain — plus a decision on where production telemetry goes.

`04` requires a documented backup procedure. A procedure that has never been restored, and whose only copy shares a power supply with the source, satisfies the letter and not the intent.

**Acceptance criteria**
- [ ] All OPS-M2 issues closed
- [ ] A backup runs unattended and is verifiable without reading logs
- [ ] A restore has actually been performed
- [ ] Losing the machine entirely does not lose the data

_Refs: 04, 07_
FILER_EOF
  )"

echo "  [10/13] Backup: scheduled dump + blobs, in the mandated order"
gh issue create --repo "$REPO" \
  --title "Backup: scheduled dump + blobs, in the mandated order" \
  --milestone "$M2" \
  --label "type:infra" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Install the backup script on a timer and make its failures visible.

**Order is a correctness requirement, not a preference: dump first, blobs second.** A document created between the two leaves an orphaned blob — harmless. The reverse order produces a dump referencing a `StorageKey` whose bytes were never copied: a restore that fails only when it is needed.

**Acceptance criteria**
- [ ] Scheduled, unattended, surviving reboot
- [ ] Ordering enforced by the script, not by the operator remembering it
- [ ] An interrupted run cannot leave a partial dump under a name that looks valid
- [ ] A failed dump fails the run — a compressed empty file is not a backup
- [ ] Retention applied; disk growth bounded and observable
- [ ] A failed scheduled run is noticed without anyone going looking

_Refs: 04, deploy/backup-filer.sh_
FILER_EOF
  )"

echo "  [11/13] Restore drill: prove a backup restores end to end"
gh issue create --repo "$REPO" \
  --title "Restore drill: prove a backup restores end to end" \
  --milestone "$M2" \
  --label "type:test" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Restore a real backup into a throwaway environment and confirm the system works from it. Until this is done, the backup is an assumption.

The specific thing under test is the coupling that ordering protects: every restored `Document` must resolve to bytes that exist. A dump that restores cleanly while its blobs are missing looks like success.

**Acceptance criteria**
- [ ] Restore performed from an untouched scheduled backup, not one made for the occasion
- [ ] Application starts against the restored database
- [ ] A document uploaded before the backup downloads intact afterwards — metadata *and* bytes
- [ ] Elapsed restore time recorded; that number is the real recovery objective
- [ ] Procedure written into `deploy/README.md`, including whatever went wrong the first time

_Refs: 04, 07_
FILER_EOF
  )"

echo "  [12/13] Off-node backup copy"
gh issue create --repo "$REPO" \
  --title "Off-node backup copy" \
  --milestone "$M2" \
  --label "type:infra" --label "module:platform" \
  --body "$(cat <<'FILER_EOF'
Backups currently land on a second disk in the same chassis. That survives a disk failure and nothing else: not theft, not fire, not a power event that takes the disks together.

**Acceptance criteria**
- [ ] Copy reaches a destination in a different failure domain
- [ ] Automated and monitored — a copy that silently stops is worse than none, because it is still believed
- [ ] Encrypted at rest if the destination is not fully trusted; documents are personal (`05`)
- [ ] Restore from the off-node copy tested, not just the local one
- [ ] The `07` open question is closed by the choice made

_Refs: 04, 05, 07 (open questions)_
FILER_EOF
  )"

echo "  [13/13] Production observability sink"
gh issue create --repo "$REPO" \
  --title "Production observability sink" \
  --milestone "$M2" \
  --label "type:infra" --label "module:ci" \
  --body "$(cat <<'FILER_EOF'
The API exports OTLP unconditionally (ADR-013), but the Aspire dashboard is an anonymous local viewer that no real environment ships. On the deployment node the export currently goes nowhere and is dropped silently — by design, and therefore easy not to notice.

Decide where production telemetry lands, or decide deliberately that it lands nowhere and record why.

**Acceptance criteria**
- [ ] Decision recorded; `07`'s open question closed either way
- [ ] If a sink is adopted: it survives restart, its retention is bounded, and it is not exposed beyond the node
- [ ] A failed analysis job is diagnosable from the sink alone, without shell access
- [ ] Resource cost measured — this node shares a GPU and 62 GB of RAM with an LLM runtime
- [ ] No document content or secret reaches the sink (`05`)

_Refs: ADR-013, 04, 07 (open questions), 05_
FILER_EOF
  )"


echo "Done."
