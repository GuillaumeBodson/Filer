---
name: Milestone review
about: Cross-slice consistency review — open as the last issue of each epic/milestone.
title: "Milestone review: <milestone>"
---

<!--
The per-slice Definition of Done (13) already gates each PR. This review catches
what only emerges ACROSS slices: drift, duplication, stale comments/docs.
Scope it to what the milestone touched, not the whole repo:

    git diff --stat <previous-milestone-tag>..HEAD

Fix trivia inline in one cleanup PR; open separate issues for anything bigger.
-->

## Scope

- Milestone: <!-- e.g. M4 — Jobs module -->
- Diff range: <!-- e.g. v0.3.0..HEAD -->
- Modules touched: <!-- e.g. Files, Jobs -->

## Consistency across slices

- [ ] Same problem solved the same way everywhere (validation, mapping, pagination, error construction) — no two slices reinventing a pattern differently
- [ ] Naming and conventions uniform across the touched modules (endpoints, DTOs, `Error` codes, file/folder layout per `10`)
- [ ] Error vocabulary coherent: `Error` codes/messages follow one style, problem-details shape per `03`

## Duplication & dead weight

- [ ] No logic duplicated across slices that should be shared (or deliberately kept duplicated — note why)
- [ ] Dead code, unused usings/members, leftover scaffolding removed
- [ ] No premature abstraction crept in: interfaces with one implementation, no test double, no planned second implementation (designed seams in `13` excepted)

## Comments & docs vs. implementation

- [ ] Comments and XML docs still describe what the code actually does; stale ones fixed or deleted
- [ ] TODO/HACK markers triaged: done, ticketed, or removed
- [ ] `project documents/` still match reality for everything this milestone changed; ADRs in `09` added for decisions made along the way
- [ ] `CLAUDE.md` / `README.md` commands and pointers still work

## Test suite health

- [ ] Coverage didn't silently degrade in touched modules (`12`)
- [ ] No flaky or disproportionately slow tests introduced
- [ ] Security-critical integration tests exist for every new protected surface (ownership → 404, auth, upload validation)

## Outcome

<!-- Link the cleanup PR and any follow-up issues opened. -->
- Cleanup PR:
- Follow-up issues:
