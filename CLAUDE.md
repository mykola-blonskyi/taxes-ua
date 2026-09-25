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
