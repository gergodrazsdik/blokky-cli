# 0009: Validator delivery and the 0.1 feature table

- Status: accepted
- Date: 2026-11-07
- Questions: T-0069 planning (validator)
- Builds on: [ADR 0007](0007-runtime-plan.md) (runtime plan), [ADR 0008](0008-spike-findings.md) (spike decisions)

## Context and problem statement

T-0068 left a stub `blokky` that rejects every plan, plus a conformance runner that measures it. T-0069 builds the validator. Planning it turned up four questions:

- **The 0.1 feature table.** ADR 0007 lists 0.1's kinds, modifiers and plan-level features, but 16 of the suite's feature ids (blokky-specs `conformance/README.md`, CF-12) are in neither its 0.1 list nor its later releases. 129 cases need them (49 validation, 80 run).
- **The gap between the validator and the run engine.** Every case runs through `blokky run`. Once the validator accepts a feature, the run cases that use it are no longer skipped, but nothing can execute them before T-0070.
- **Later features.** ADR 0007 ships the full validator in 0.1, but CF-12 skips every case whose features aren't supported, so 167 validation cases for later features would go unmeasured.
- **PR shape.** T-0069 covers about 480 validation cases and several layers.

The maintainer decided all four by questionnaire on 2026-11-07, each as recommended.

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| The 16 unlisted feature ids | **all into 0.1**; all but `keep-descendants(-detach)`, `external-stop`, `shell-zsh`; only `interpolation`, `from-env`, `defaults`, `ok-exit` |
| Validator before run engine | **two feature tables (validated, runnable)**; validation-only CI; run-case failures non-gating |
| Later-feature validation | **validate all and measure with an extra, gating pass**; unit tests only; validate 0.1 features only |
| PR shape | **a gh stack: front end and AST first, then rule families in parallel, then binding, gate and lint**; one PR; sequential PRs |

## Decision outcome

### The 0.1 feature table

0.1 adds these ids to ADR 0007's list:

- **leaf options:** `stdin`, `ok-exit`, `stderr-merge`, `encoding`, `keep-descendants`, `keep-descendants-detach`, `capture-file`, `secret`;
- **kinds and shells:** `sleep`, `shell-zsh`;
- **values and plan-level:** `interpolation`, `from-env`, `secret-ref`, `defaults`;
- **runner actions:** `external-stop`, `kill`.

They apply to kinds already in 0.1, and the Linux spikes covered the process-control ones (spikes 1, 5 and 8L). With them, 0.1's scope is 313 validation and 314 run cases (blokky-specs `main` on 2026-11-07, which includes the ADR 0027 cases added after `v1.0.0-draft.5`). CI installs zsh for the `shell-zsh` cases.

### Two feature tables

- Core keeps two tables: **validated** (features the validator fully checks) and **runnable** (features the run engine executes). Runnable is always a subset of validated.
- `validate` gates on validated. `run` gates on runnable: a plan that validates but uses a feature that isn't runnable is rejected in validation with `BK_PLAN_NOT_SUPPORTED`, as CF-12 requires, and no process starts.
- The conformance adapter declares validated for validation cases and runnable for run cases (CF-54). The runner's skips still come from the binary's own tables.
- The adapter runs validation cases (and their not-supported check) through `blokky validate` and run cases through `blokky run`, with the same inputs and stdin, so each case meets the gate of the table it is judged against.
- T-0069 grows validated to the 0.1 table. T-0070 grows runnable kind by kind. A release requires the two tables to be equal for its features.

### Later-feature validation

- The validator checks the whole language (ADR 0007). Features outside the release's table are then gated with `BK_PLAN_NOT_SUPPORTED`.
- The runner gets an extra, non-normative **later-feature validation pass**. A validation case whose features aren't all validated passes when the implementation reports the case's expected errors; `BK_PLAN_NOT_SUPPORTED` errors are allowed in addition. CF-12's skip and not-supported check still apply in the normative pass.
- The report shows it as a second number next to the CF-63 pass rate. CI gates on it: a fail there fails the job.

### PR shape (T-0080 to T-0086)

A gh stack, each PR with the reviewer agent and green CI:

1. **T-0080:** the plan reader (encoding pass, character lint), the YAML front end (YamlDotNet adapter, line map), this ADR, and the two tables.
2. **T-0081:** the AST, schema-level checks, `validate` output, and the runner's later-feature validation pass.
3. **T-0082 to T-0085, in parallel worktrees:** the static rule families: syntax, variables and interpolation, conditions, reference and modifiers.
4. **T-0086:** input binding, the feature gate, lint and strict mode.

### Consequences

- 0.1's feature list grows by 16 ids. ADR 0007's list stands with this addition, and ROADMAP.md lists them.
- `blokky run` on a valid plan returns `BK_PLAN_NOT_SUPPORTED` until T-0070 makes its features runnable. That is a development state only; no release ships with the tables unequal.
- The later-feature pass is a CLI measure, not part of the conformance contract, so it needs no spec change.
