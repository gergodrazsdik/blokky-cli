# Architecture decision records (blokky-cli)

Decisions about the implementation, configuration, validation and hook integration, distribution and process. Language decisions live in [blokky-specs/adr](https://github.com/gergodrazsdik/blokky-specs/tree/main/adr).

ADRs are **grouped**: a baseline ADR, then one ADR per batch of answered open questions (brief §4, §8 step 2), rather than one file per tiny decision. Answers keep the brief's question IDs.

## Index

| ADR | Title | Status | Questions |
|---|---|---|---|
| [0001](0001-baseline.md) | Implementation, distribution and process baseline | accepted | — (partly Q-q) |

## Format

Files are named `NNNN-short-title.md` (four digits, never reused) and follow a [MADR](https://adr.github.io/madr/)-style template:

```markdown
# NNNN: Title

- Status: proposed | accepted | superseded by NNNN | deprecated
- Date: YYYY-MM-DD
- Questions: Q6, Q-a (brief §4 IDs this ADR answers)

## Context and problem statement
## Considered options
## Decision outcome
Chosen option: "…", because …
### Consequences
## More information
```

Everything can still change until v1.0.0 (brief §1.5): a later ADR supersedes an earlier one instead of editing it.
