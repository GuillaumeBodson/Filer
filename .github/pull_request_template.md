<!-- Solo self-review checklist. Open a PR even when working alone: the diff is your review. -->

## What & why
<!-- One or two lines. The *why* matters more than the *what*. -->

## Changes
-

## Checklist
- [ ] Builds clean (warnings-as-errors) and `dotnet test` passes
- [ ] Respects ADRs and module boundaries (architecture tests green)
- [ ] No secrets committed (`Jwt__SigningKey`, connection strings, keys)
- [ ] Upload/IA paths stay async; ownership checks return 404 cross-owner (if touched)
- [ ] Canonical docs in `project documents/` updated if a fact changed

## Notes
<!-- Follow-ups, known gaps, anything future-you should remember. -->
