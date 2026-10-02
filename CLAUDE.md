# Project Instructions

This repository follows the global Claude configuration.

## Source of Truth

Before making architectural or implementation decisions, review:

1. docs/architecture.md
2. docs/decisions.md
3. docs/TODO.md
4. knowledge/domain-model.md
5. knowledge/business-rules.md

## Planning

Use:

- plans/current.md for active work
- plans/backlog.md for future work

Update plans when major tasks are completed.

## Documentation

Project documentation lives in:

- docs/
- knowledge/

Keep documentation synchronized with code changes.

## Reports

Analysis results should be stored in:

- reports/reviews/
- reports/investigations/
- reports/audits/
- reports/summaries/

Reports are historical records and should not replace project documentation.

## Architecture Analysis

Use:

- graph/architecture.md
- graph/dependencies.md

for architecture and dependency analysis.

## Reusable Resources

Reference materials:

- snippets/
- examples/
- boilerplates/
- prompts/

Reuse existing templates whenever possible.

## Local Overrides

Additional project-specific instructions may exist in:

- .claude/CLAUDE.local.md

## Agent skills

### Issue tracker

Issues live in GitHub Issues of `mykola-blonskyi/taxes-ua` via `gh`, mirrored as markdown under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context. Glossary and rules in `knowledge/`, ADRs in `docs/decisions.md`. See `docs/agents/domain.md`.

### Verification

Run `pnpm test` in `web/` (Vitest, a few seconds) before calling any web change done; CI runs it and
the deploy waits for it. Add or update tests next to the code you change (ADR-020). Then prove any UI
change in a real browser: launch the stack and sign in through the Development seam. See
`.claude/skills/verify-taxes-ua/SKILL.md`.

`pnpm e2e e2e/layout.spec.ts` in `web/` is the phone-width check: every route and tab at 375 px in Ukrainian and Russian, with axe (serious and critical fail), controls of 44 px or more for a coarse pointer,
with no sideways scroll, the disclaimer present and the right `html lang`. A UI change passes it before it is
done; a strip that scrolls by design carries a `data-scroll-strip` name that is listed in `scrollingStrips` in `web/e2e/layout.spec.ts`.

`pnpm e2e` in `web/` runs the Playwright suite against its own Compose stack (Docker must be running, port 3000
is not touched) and removes the stack afterwards. A new owner flow adds a scenario in `web/e2e/` (ADR-020).
