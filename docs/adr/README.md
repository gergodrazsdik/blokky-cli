# Architecture decision records (blokky-cli)

Decisions about the implementation, configuration, validation and hook integration, distribution and process. Language decisions live in [blokky-specs/adr](https://github.com/gergodrazsdik/blokky-specs/tree/main/adr).

ADRs are **grouped**: a baseline ADR, then one ADR per batch of answered open questions (brief §4, §8 step 2), rather than one file per tiny decision. Answers keep the brief's question IDs.

## Index

| ADR | Title | Status | Questions |
|---|---|---|---|
| [0001](0001-baseline.md) | Implementation, distribution and process baseline | accepted | — (partly Q-q) |
| [0002](0002-cli-run-lifecycle.md) | CLI and run lifecycle (open-question batch 3) | accepted | Q6, Q15, Q-f, Q-t, Q-u |
| [0003](0003-validation-config-lint.md) | Validation, hook, configuration and lint (open-question batch 4) | accepted | Q10, Q11, Q-a, Q-b, Q-c, Q-d, Q-g, Q-in2, Q-in6 |
| [0004](0004-platform-invocation-conformance.md) | Process launcher, Windows invocation and conformance checkout (open-question batch 6) | accepted | Q-e, Q-h, Q-o, Q-q (checkout) |
| [0005](0005-exit-code-details.md) | Exit-code precedence and Windows events | accepted | — (open items of 0002, raised in T-0026) |
| [0006](0006-phase-order-and-incremental-cli.md) | Phase order, spec scope and an incremental CLI | accepted | — (direction revision; supersedes parts of 0001) |
| [0007](0007-runtime-plan.md) | Runtime plan: MVP, release order, docs and test-first build | accepted | T-0053 (runtime planning session) |
| [0008](0008-spike-findings.md) | Decisions from the MVP spikes | accepted | T-0030 (MVP spike findings) |
| [0009](0009-validator-delivery.md) | Validator delivery and the 0.1 feature table | accepted | T-0069 (validator planning) |

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
