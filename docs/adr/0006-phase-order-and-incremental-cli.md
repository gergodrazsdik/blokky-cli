# 0006: Phase order, spec scope and an incremental CLI

- Status: accepted
- Date: 2026-11-05
- Supersedes: the "Process" section of [ADR 0001](0001-baseline.md) (phase order) and the contract ownership in its "Repositories and licensing" section
- Companion: [blokky-specs ADR 0013](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0013-scope-and-versioning.md)

## Context and problem statement

The language spec draft is complete (glossary through schemas, specs PRs #9–#21; the `cli.md` exit codes, cli PR #24). Before starting the spikes, the maintainer revised the project's direction:

- blokky-specs should hold only the language; CLI- and runtime-specific material belongs in blokky-cli;
- the spec should be finished first, with all planned features, as a `1.0` draft, and refined while the runtime is built, because many decisions can't be confirmed before then;
- the CLI should be versioned separately and grow incrementally from a minimal MVP, steered by its conformance pass rate;
- a review (by the maintainer and an agent council) should also ask whether each feature is needed, against the project's principles.

The questions were asked as multiple choices with a recommendation, on 2026-11-05.

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| Where the specs/cli line is | **plan meaning stays in specs** (syntax, semantics, plan-visible errors, an abstract run-result model); keep the JSON contracts in specs; strictest (runtime-dependent semantics move too) |
| Versioning and milestone tags | **`1.0.0-draft.N`, plans write `blokky: 1.0`, no milestone tags in the spec**; keep core/extended tags; plans name the draft |
| When to write the conformance suite | format and testkit interface now; **cases after the review, before the MVP**; together with the examples; alongside the MVP |
| How to record the change | **edit the brief and add ADRs**; ADRs only; the maintainer edits the brief |

## Decision outcome

All answers follow the recommendation.

### Phase order (brief §8, revised)

1. Scope cleanup and versioning (move CLI/runtime material to blokky-cli; drop milestone tags; `blokky: 1.0`).
2. Specify the remaining language features to the same depth; the maintainer tags `1.0.0-draft.1`.
3. A user-facing example suite covering every feature, small and comprehensive examples; plus the conformance case format, testkit interface, overview and prior art.
4. Review by the maintainer and an agent council (independent lenses: principles and minimalism, the agent as user, security posture, implementability, spec quality). Accepted findings become ADRs and a new draft. This replaces the former consistency pass.
5. The conformance suite, for the features that survive the review.
6. A runtime planning session: the CLI MVP, the feature order, CLI versioning, and when the remaining CLI docs are needed.
7. The spike plan and spikes.
8. Incremental CLI implementation, guided by the conformance pass rate. The spec is refined through ADRs and becomes `1.0.0` once the runtime has validated it.

### Ownership

blokky-cli now owns everything runtime-specific: the JSON contracts (report, pre-run envelope, events, `leaves`, validator protocol) and their schemas, exit codes and CLI-only error codes, the run dir and cleanup, configuration, the run budget, the hook, approval, lint rules and OS process control. They move from blokky-specs in step 1 (blokky-specs ADR 0013 lists what stays).

### An incremental CLI

- The CLI keeps its own semver (0.x) and may support only part of the language. Each release declares the spec version and a feature matrix in its ROADMAP; unsupported features fail with "not supported by this blokky version".
- The MVP's scope and the feature order are decided in the runtime planning session, using the conformance pass rate as the measure.
- The CLI fan-out docs (configuration, validation and permissions, threat model, agent docs, design docs) move from before the spikes to after the planning session, written when the implementation needs them.

### Consequences

- Good: the language is reviewed as a whole before any runtime work, including whether each feature earns its place.
- Good: the CLI can ship early with a small, well-tested core.
- Neutral: the earlier ADRs keep their `v0.1`/`later` wording; it now feeds the planning session rather than the spec.
- Bad: moving the contracts is a sizeable reshuffle of merged draft text across both repos.

## More information

- Brief: header revision note, §0.1, §1.5, §1.6, §5, §8 (revised in the same PR).
- Tracker: phases restructured in the same PR.
