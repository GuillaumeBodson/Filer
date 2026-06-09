<!-- Solo self-review checklist. Open a PR even when working alone: the diff is your review. -->

## What & why
<!-- One or two lines. The *why* matters more than the *what*. -->

## Linked issue
<!-- Required for tracked work (11-git-workflow.md): a closing keyword HERE in the
     PR body — "Closes #N" — so squash merge auto-closes the issue. Verify the
     issue closed after merge. Write "untracked" if no issue exists. -->
Closes #

## Changes
-

## Checklist (Definition of Done — see `project documents/13-code-quality-and-design.md`)
- [ ] Builds clean in Release (warnings-as-errors + analyzers, code style enforced)
- [ ] Tests per `12-testing-strategy.md`: feature-service unit tests (success + every `Error`) and security-critical integration tests pass; coverage meets the module threshold
- [ ] Respects ADRs and module boundaries (architecture tests green); no premature mediator/repository/CQRS
- [ ] Expected outcomes use `Result`/`Error` (no exceptions for business flow); `CancellationToken` threaded through new async paths
- [ ] No secrets committed (`Jwt__SigningKey`, connection strings, keys); nothing sensitive logged
- [ ] Upload/AI paths stay async; ownership checks return 404 cross-owner (if touched)
- [ ] Structured logs at the right levels; new background work emits required metrics/correlation
- [ ] Canonical docs in `project documents/` updated if a fact changed; ADR added to `09` for non-trivial design/pattern decisions

## Notes
<!-- Follow-ups, known gaps, anything future-you should remember. -->
