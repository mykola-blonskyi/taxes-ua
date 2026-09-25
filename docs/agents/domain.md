# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

This repo is single-context. Its domain docs already exist under `knowledge/` and `docs/`, so the
generic `CONTEXT.md` and `docs/adr/` layout is not used. Read instead:

- **`knowledge/glossary.md`**: the terminology. Plays the role of `CONTEXT.md`.
- **`knowledge/domain-model.md`** and **`knowledge/business-rules.md`**: entities, fields and the tax rules.
- **`docs/decisions.md`**: all ADRs in one file, numbered `ADR-NNN`. Plays the role of `docs/adr/`. Read the ADRs that touch the area you're about to work in.
- **`docs/architecture.md`**: components, data flow, deployment.

New terms go into `knowledge/glossary.md`, new decisions into `docs/decisions.md`. Do not create
`CONTEXT.md`, `CONTEXT-MAP.md` or `docs/adr/`.

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test
name), use the term as defined in `knowledge/glossary.md`: EP / `SingleTax`, VZ / `MilitaryLevy`,
ESV / `Esv`, `Obligation`, `Kop`, `RateE4`, `Bp`. Don't drift to synonyms the glossary avoids.

If the concept you need isn't in the glossary yet, that's a signal: either you're inventing language
the project doesn't use (reconsider) or there's a real gap (add it to the glossary).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-003 (integer money), but worth reopening because…_
