# Blokky work tracker

One tracker for both repos (blokky-cli and blokky-specs). Agents keep it current; humans skim it. Rules are in [AGENTS.md](../../AGENTS.md#work-tracker); the process is brief §8.

Legend: [ ] todo · [~] in progress · [x] done · [-] dropped · [!] blocked/needs decision

## Now

<!-- the next 3–5 items, in order; agents pick work from here -->

1. T-0027 [cli] Spike plan, ordered by release (MVP spikes first)
2. T-0028 [cli] Maintainer approves the spike plan
3. T-0067 [cli] Refresh the design docs before code

## Phase 0 — Bootstrap

### M0.1 Repositories and tooling

- [x] T-0001 [both] Create local repos and skeleton: community docs, layout from brief §5, placeholders — 2026-11-04, cli 5136918, specs 3434b5f
- [x] T-0002 [both] Lint tooling: justfile, lefthook hooks (DCO, Conventional Commits, gitleaks), `lint` workflow with DCO job — 2026-11-04, cli 5136918, specs 3434b5f
- [x] T-0003 [both] Create GitHub repos; general, security and Actions settings; `main` and `v*` tag rulesets; labels, topics — 2026-11-04 (settings read back; see F-0004)
- [x] T-0004 [cli] Add the `lint` and `dco` jobs as required status checks on `main` — 2026-11-04, after smoke-test PR #1 (closed unmerged)
- [x] T-0042 [specs] Add `lint` and `dco` as required status checks on `main` (GitHub Actions app, id 15368) — 2026-11-04, after specs PR #1
- [x] T-0005 [both] Code of Conduct: replace the `TODO(maintainer)` contact with a real address — 2026-11-04, specs PR #1, cli PR #3
- [-] T-0006 [both] Optional: enable GitHub Discussions for questions — dropped 2026-11-04: maintainer keeps Discussions off; questions go to issues

## Phase 1 — Language spec draft (brief §8.2–8.3)

### M1.1 Open questions answered (brief §4)

Done 2026-11-04: every draft-batch question answered in six batches (specs ADRs 0002–0006, cli ADRs 0002–0004); Q16b and Q18 stay `later`.

Proposed batches; IDs are the brief's. Each batch ends in one grouped ADR in the repo it concerns.

- [x] T-0007 [both] Baseline ADR per repo: record brief §2 baseline decisions (specs: language; cli: runtime, distribution, process) — 2026-11-04, specs PR #3 ([ADR 0001](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0001-language-baseline.md)), cli PR #5 ([ADR 0001](../adr/0001-baseline.md))
- [x] T-0008 [specs] Batch 1, language core: Q1, Q2, Q3, Q12, Q13, Q-m, Q-n, Q-s — 2026-11-04, specs PR #4 ([specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md)), cli PR #6
- [x] T-0009 [specs] Batch 2, execution and defaults: Q4, Q5, Q7, Q8, Q-k, Q-r — 2026-11-04, specs PR #5 ([specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md)), cli PR #7
- [x] T-0010 [cli] Batch 3, CLI and run lifecycle: Q6, Q15, Q-u, Q-f, Q-t — 2026-11-04, cli PR #8 ([cli ADR 0002](../adr/0002-cli-run-lifecycle.md))
- [x] T-0011 [both] Batch 4, validation, config and inputs: Q9, Q10, Q11, Q-a, Q-b, Q-c, Q-d, Q-g, Q-in1, Q-in2, Q-in3, Q-in4, Q-in5, Q-in6 — 2026-11-04, specs PR #6 ([specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md)), cli PR #9 ([cli ADR 0003](../adr/0003-validation-config-lint.md))
- [x] T-0012 [specs] Batch 5, containers, `become` and locks (language level): Q16, Q17, Q-j — 2026-11-04, specs PR #7 ([specs ADR 0005](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0005-containers-become-locks.md)), cli PR #10
- [x] T-0013 [both] Batch 6, platform and repo split: Q-e, Q-h, Q-l, Q-o, Q-q — 2026-11-04, specs PR #8 ([specs ADR 0006](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0006-shims-and-license.md)), cli PR #11 ([cli ADR 0004](../adr/0004-platform-invocation-conformance.md))

### M1.2 Draft spec PRs

Main agent, sequential, PRs marked **Draft spec** (brief §8.3).

Done 2026-11-05: glossary, syntax, reference, execution model, variables, conditions, shells, containers and privilege, errors, output contract, leaves and validation, all schemas (specs PRs #9–#21) and the `cli.md` exit codes (cli PR #24); drafting decisions in specs ADRs 0007–0012 and cli ADR 0005.

- [x] T-0014 [specs] `spec/glossary.md` — 2026-11-04, specs PR #9 (draft), cli PR #12
- [x] T-0015 [specs] `spec/syntax.md` (surface rules, root document, inputs syntax, per-field table, pitfalls, targeted errors) — 2026-11-04, specs PR #10 (draft; [specs ADR 0007](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0007-syntax-details.md)), cli PR #13
- [x] T-0016 [specs] `spec/reference.md` (all planned features, milestone-tagged, validity matrix) — 2026-11-04, specs PR #11 (draft; [specs ADR 0008](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0008-repetition-details.md)), cli PR #14
- [x] T-0017 [specs] `spec/execution-model.md` — 2026-11-05, specs PR #12 (draft; [specs ADR 0009](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0009-status-propagation.md)), cli PR #15
- [x] T-0018 [specs] `spec/variables.md` — 2026-11-05, specs PR #13 (draft; [specs ADR 0010](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0010-variables-details.md)), cli PR #16
- [x] T-0019 [specs] `spec/conditions.md` — 2026-11-05, specs PR #14 (draft), cli PR #17
- [x] T-0020 [specs] `spec/shells.md` — 2026-11-05, specs PR #15 (draft; [specs ADR 0011](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0011-pwsh-prologue.md)), cli PR #18
- [x] T-0021 [specs] `spec/containers-and-privilege.md` — 2026-11-05, specs PR #16 (draft; [specs ADR 0012](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0012-container-details.md)), cli PR #19
- [x] T-0022 [specs] `spec/errors.md` — 2026-11-05, specs PR #17 (draft), cli PR #20
- [x] T-0023 [specs] `spec/output-contract.md` — 2026-11-05, specs PR #18 (draft), cli PR #21
- [x] T-0024 [specs] `spec/leaves-and-validation.md` — 2026-11-05, specs PR #19 (draft), cli PR #22
- [x] T-0025 [specs] All schemas: `blokky`, `report`, `events`, `leaves`, `validator` — 2026-11-05, specs PR #20 (draft), cli PR #23
- [x] T-0026 [cli] Exit-code table in `docs/cli.md` — 2026-11-05, cli PR #24 ([cli ADR 0005](../adr/0005-exit-code-details.md)), specs PR #21

### M1.3 Scope cleanup and versioning (brief §8 step 4)

Done 2026-11-05: contracts moved (T-0043), runtime prose removed and `results.md` added (T-0044, T-0045), milestone tags dropped (T-0046).

blokky-specs becomes language-only; the spec moves to `1.0.0-draft.N` without milestone tags ([cli ADR 0006](../adr/0006-phase-order-and-incremental-cli.md), [specs ADR 0013](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0013-scope-and-versioning.md)).

- [x] T-0043 [both] Move `output-contract.md`, `leaves-and-validation.md`, the report/events/leaves/validator schemas and the CLI-only error codes to blokky-cli (`docs/contracts/`, `schema/`) — 2026-11-05, specs PR #23, cli PR #26
- [x] T-0044 [specs] Remove runtime mechanics from the spec text (budget and config keys, hook, approval, lint rules, run dir, OS process control), keeping the observable rules and pointing to blokky-cli; update the glossary — 2026-11-05, specs PR #24, cli PR #27
- [x] T-0045 [specs] Abstract run-result model (`spec/results.md`) for conformance cases — 2026-11-05, specs PR #24, cli PR #27
- [x] T-0046 [specs] Drop the `v0.1`/`later` tags; plans write `blokky: 1.0`; README, ROADMAP, CHANGELOG and schema `$id`s for `1.0.0-draft.N` — 2026-11-05, specs PR #25, cli PR #28

### M1.4 Remaining language features (brief §8 step 5)

- [x] T-0047 [specs] Ask the open questions for the features still sketched (batch 7) — 2026-11-05, specs PR #26 ([specs ADR 0014](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0014-remaining-features.md)), cli PR #29
- [x] T-0048 [specs] Specify `race`, `lock`, `services`, `items`/`lines`/`matrix`, `collect`, `consent`, `zsh`/`fish` to full depth — 2026-11-05, specs PR #26 ([specs ADR 0014](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0014-remaining-features.md)), cli PR #29
- [x] T-0049 [specs] Maintainer tags `1.0.0-draft.1` — 2026-11-05, tag `v1.0.0-draft.1`
- [x] T-0055 [specs] `set` node kind and root `vars:` (plan constants), with `${}` templates and single assignment — 2026-11-06, specs PR #28 ([specs ADR 0015](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0015-set-and-vars.md)), cli PR #31
- [x] T-0056 [specs] Maintainer tags `v1.0.0-draft.2` — 2026-11-06, tag `v1.0.0-draft.2`
- [x] T-0057 [specs] Fixes found while writing the examples: group errors, `any` + `when`, the `command:` subject, the `race` example, `${` in scripts, clarifications — 2026-11-06, specs PR #34 ([specs ADR 0016](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0016-fixes-from-examples.md)), cli PR #32
- [x] T-0058 [specs] Maintainer tags `v1.0.0-draft.3` — 2026-11-06, tag `v1.0.0-draft.3`

## Phase 2 — Example suite (brief §8 step 6)

The example suite is done: 45 basic examples, seven scenarios and an index, plus the conformance case format, the testkit interface, the overview and prior art.

- [x] T-0031 [specs] `examples/` (small per-feature and comprehensive examples covering every feature), `conformance/README.md`, `conformance/testkit-interface.md`, `spec/overview.md`, `spec/prior-art.md` — 2026-11-06, specs PRs #29, #30, #31, #33, #35

## Phase 3 — Review (brief §8 step 7)

The review is done: an agent council of five lenses (76 findings, all verified), the maintainer's decisions on every item, and `1.0.0-draft.4` with specs ADRs 0017–0019.

- [x] T-0050 [both] Agent council review: propose the council (lenses, prompts, output format), run it, verify and consolidate findings — 2026-11-06, [council report](../review/council-draft.3.md) (76 findings, 0 refuted)
- [x] T-0051 [both] Maintainer review; accepted findings as ADRs; next draft (`1.0.0-draft.N`), including features cut or kept — 2026-11-07, specs PRs #36, #39, #38, cli PR #35
  - Review done 2026-11-07: every decision recorded in the [council report](../review/council-draft.3.md) §6 (D-1 to D-6, S-1 to S-14, C-1 to C-8, spikes).
  - Applied 2026-11-07 as specs [ADR 0017](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0017-review-features.md) (features, specs PR #36), [ADR 0018](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0018-review-semantics.md) (semantics, #39) and [ADR 0019](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0019-review-corrections.md) (corrections, #38); spike input recorded in [spike-plan.md](../design/spike-plan.md).
- [x] T-0059 [specs] Maintainer tags `v1.0.0-draft.4` — 2026-11-07, tag `v1.0.0-draft.4`
- [-] T-0036 [both] Consistency pass — dropped 2026-11-05: replaced by the review phase ([cli ADR 0006](../adr/0006-phase-order-and-incremental-cli.md))

## Phase 4 — Conformance suite (brief §8 step 8)

Conformance suite complete 2026-11-07: 1,147 cases cover all 380 rules (with specs #70/#71); F-0024 has small open points.

- [x] T-0052 [specs] Conformance cases for the features that survive the review, each citing its rule IDs (see F-0007 for schema checks) — 2026-11-07: waves A to E merged, 1,135 cases, rule coverage 374/374 (ADRs 0020 to 0025)
  - Plan agreed 2026-11-07: a static checker first, then validation cases before run semantics, in waves. Each area is its own PR from its own worktree, with a second agent checking every case against the spec text (cases can't run until the CLI has a runner). Spec gaps found while writing get a finding at once; blocking ones are fixed in a separate specs PR first.
- [x] T-0060 [specs] Case checker (`scripts/check.py`, `conformance/case.schema.json`, `just check` and `just coverage`, in `just lint` and CI) and a first batch of 8 cases — 2026-11-07, [specs PR #42](https://github.com/gergodrazsdik/blokky-specs/pull/42)
- [x] T-0061 [specs] Wave A, validation: syntax and the static reference rules (SY, RF), the generated validity-matrix and sibling-table cases (CF-45, `scripts/generate-matrix.py`), inputs and variables at validation (VA), errors (ER) — specs PRs [#45](https://github.com/gergodrazsdik/blokky-specs/pull/45), [#46](https://github.com/gergodrazsdik/blokky-specs/pull/46), [#47](https://github.com/gergodrazsdik/blokky-specs/pull/47), [#48](https://github.com/gergodrazsdik/blokky-specs/pull/48), [#49](https://github.com/gergodrazsdik/blokky-specs/pull/49) (301 hand-written cases in four areas, 398 generated matrix cases) — 2026-11-07; coverage 188/365 rules (syntax.md, reference.md and errors.md complete)
- [x] T-0062 [specs] Wave B, run semantics: node kinds, statuses, group errors, `return`, `when` (EM core, RS); leaf processes (EM-14 to EM-24, EM-68, EM-69); captures, scopes, the environment and masking at run time (VA) — 2026-11-07, specs stack [#50](https://github.com/gergodrazsdik/blokky-specs/pull/50) to [#53](https://github.com/gergodrazsdik/blokky-specs/pull/53) (ADR 0022 and 127 cases); coverage 250/365 rules (variables.md and results.md complete)
- [x] T-0063 [specs] Wave C: conditions and shells (CO, SH) — 2026-11-07, specs stack [#55](https://github.com/gergodrazsdik/blokky-specs/pull/55) to [#58](https://github.com/gergodrazsdik/blokky-specs/pull/58) (ADR 0023, checker position check, 166 cases); coverage 329/373 rules (shells.md complete, conditions.md 48/50)
- [x] T-0064 [specs] Wave D, timing and process control: `timeout` and grace, `finally`, `lock`, `services`, external stop and kill, repetition with `carry`, `collect` and `index` (EM) — 2026-11-07, specs stack [#60](https://github.com/gergodrazsdik/blokky-specs/pull/60) to [#63](https://github.com/gergodrazsdik/blokky-specs/pull/63) (ADR 0024, 78 cases); coverage 350/374 (execution-model.md complete)
- [x] T-0065 [specs] Wave E: modifier combinations (CF-46, CF-47) and containers (CP) — specs stack [#65](https://github.com/gergodrazsdik/blokky-specs/pull/65) to [#68](https://github.com/gergodrazsdik/blokky-specs/pull/68): ADR 0025, the ADR 0025 sweep, 36 container cases and 13 combination cases; with it coverage is 374/374 rules across 1,135 cases — merged 2026-11-07
- [x] T-0066 [specs] Maintainer tags `v1.0.0-draft.5` with the spec fixes found while writing the cases — 2026-11-07, tag `v1.0.0-draft.5` (roadmap: [specs PR #73](https://github.com/gergodrazsdik/blokky-specs/pull/73))

## Phase 5 — Runtime planning, spikes and incremental CLI (brief §8 steps 9–11)

- [x] T-0053 [cli] Runtime planning session: MVP feature set, feature order, CLI versioning, when each CLI doc is needed — 2026-11-07, [ADR 0007](../adr/0007-runtime-plan.md) (questionnaire): full validator + sequential run on Linux in 0.1, value-first release order, test-first build
- [ ] T-0027 [cli] `docs/design/spike-plan.md`: every "Provisional — verify in spike" item (brief §8 step 10), ordered by release: the MVP spikes (1, 3, 4, 5, 6, 7, 9) first, the rest before their release (ADR 0007)
- [ ] T-0028 [cli] Maintainer approves the spike plan
- [ ] T-0029 [cli] Spikes on unmerged `spikes/*` branches; one task per spike is added once the plan is approved
- [ ] T-0030 [both] Fold spike findings back into the draft
- [ ] T-0067 [cli] Refresh the design docs before code: architecture, process control (launcher abstraction designed for all three OSes), tech stack, testkit, validation strategy, CI/CD
- [ ] T-0068 [cli] `blokky-testkit` (Native AOT, testkit-interface.md) and the conformance runner with its adapter; CI runs the suite on Linux and reports the pass rate
- [ ] T-0069 [cli] Validator: parse and validate until the suite's validation cases pass (unsupported features → `BK_PLAN_NOT_SUPPORTED`)
- [ ] T-0070 [cli] Run engine: sequential execution on Linux until the 0.1 conformance subset passes
- [ ] T-0071 [cli] 0.1 docs: `configuration.md`, `cli.md`, contracts, agent docs (`agent-guide.md`, `SKILL.md`), README and install
- [ ] T-0072 [cli] Release 0.1: Linux AOT binaries with checksums; the maintainer tags `cli-v0.1.0`
- [ ] T-0073 [cli] 0.2: repetition and probes
- [ ] T-0074 [cli] 0.3: `all`, `any`, `race`, `tempdir`, `lock`
- [ ] T-0075 [cli] 0.4: Claude Code hook and approval (after `validation-and-permissions.md` and the threat model, T-0032)
- [ ] T-0076 [cli] 0.5: `services`
- [ ] T-0077 [cli] 0.6: macOS and Windows (Windows spikes first)
- [ ] T-0078 [cli] 0.7: `container` (container and engine spikes first)
- [ ] T-0032 [cli] (b) `configuration.md`, config schema (main agent owns; subagent proposes), `validation-and-permissions.md`, `security/threat-model.md`
- [ ] T-0033 [cli] (c) agent docs: `agent-guide.md`, `skill/SKILL.md`, `mcp-tool-description.md`
- [ ] T-0034 [both] (d) user/project docs: README, install, contributing, SECURITY (after (b)), roadmaps, CHANGELOG, NOTICE, CODEOWNERS, dev-setup, `mcp.md` (bootstrap versions exist; this pass completes them)
- [ ] T-0035 [cli] (e) design docs: architecture, tech-stack, process-control, validation-strategy, testkit, ci-cd, `design/notes/`
- [ ] T-0037 [both] Spec `1.0.0` once the runtime has validated the draft (was: finalize the spec after the spikes)
- [ ] T-0038 [cli] Enable CodeQL (C#) once C# code lands
- [ ] T-0039 [cli] Enable the Dependabot `nuget` block once the solution exists
- [ ] T-0054 [cli] Add .NET `.editorconfig` rules (C# style, naming, `*.csproj` indentation) once the solution exists (removed from the bootstrap version on 2026-11-06)
- [ ] T-0040 [cli] Release workflow verifies the release tag's signature (`git verify-tag`): GitHub tag rulesets can't check tag-object signatures
- [ ] T-0041 [both] Protected `release` environment with manual approval (brief §7)

## Open questions

Seeded from brief §4, keeping its IDs. "Draft" = needed for the language spec draft; "later" = asked when the related spike or milestone comes up.

| ID | Question (short) | Status | Answer / ADR |
|---|---|---|---|
| Q1 | Confirm the modifier order (§3.1) | answered | Confirmed as written; [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q2 | JSON path subset; `collect` milestone and syntax | answered | Minimal jq-like paths (`.a`, `.["k"]`, `[N]`); `collect` later, brief syntax; [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q3 | Condition tables; `matches` semantics; trimming; number grammar; `probe_timeout` and probe capture limit | answered | Tables as written; unanchored whole-value search; strip all trailing CR/LF; plain decimal; 30s / 64 KiB; [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q4 | Shells: v0.1 set, detection lists, invocation table (pipefail/-e), Git Bash policy | answered | Set confirmed; detection bash→sh / pwsh→powershell; bash `-o pipefail`, no `-e`; `sh <file>`; pwsh `$ErrorActionPreference = 'Stop'`; Git Bash only via a config path; [specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md) |
| Q5 | Duration defaults: global `timeout`, `leaf_timeout`, `finally_timeout`, `drain_timeout`, `cleanup_reserve` | answered | No global `timeout` or `leaf_timeout`; `finally_timeout` 5m; grace 5s, `drain_timeout` 2s, `cleanup_reserve` 30s; [specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md) |
| Q6 | Exit codes of `blokky` | answered | 0 success, 1 plan failed, 2 usage, 3 plan invalid, 4 config, 5 ask, 6 deny, 7 sha mismatch, 8 budget, 9 cleanup incomplete, 70 internal, 128+N signal; [cli ADR 0002](../adr/0002-cli-run-lifecycle.md) |
| Q7 | Output defaults: tail size, report byte budget, log caps | answered | Tails 50 lines ≤ 4 KiB; report 32 KiB; logs 100 MiB per leaf, 1 GiB per run; `max_capture_bytes` 1 MiB; [specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md) |
| Q8 | Default `max_items` for `for` over captured output | answered | Optional, default 1000; exceeding it is an error; [specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md) |
| Q9 | Validator plugin protocol | answered | Spawn per request, JSON on stdin/stdout; allow/deny/ask with optional per-unit verdicts; any failure → ask; 10s timeout; [specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md) |
| Q10 | Strict-mode lint rule set: rules, IDs, severities, config, suppression | answered | Kebab-case IDs; off/info/warning/error; `strict` preset; per-rule config, no in-plan suppression; initial set in the ADR; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q11 | Config file names; full list of user-only keys | answered | File names as in the brief; user-only: executables, locations, enabling switches, approval and rules settings; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q12 | Must `name` values be unique? | answered | No; lint flags duplicate sibling names; [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q13 | Plan version scheme for `blokky:`; required? | answered | Required, `MAJOR.MINOR`; [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q14 | Leaf descendants at leaf completion | answered | Brief §3.8: killed by default; `keep_descendants: true` opts out; [specs ADR 0001](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0001-language-baseline.md) |
| Q15 | How `clean` reports partial deletion failure; exit code | answered | JSON list of removed and failed items, exit 9; rerun is idempotent; `run --cleanup` keeps the plan's code and reports `cleaned: partial`; [cli ADR 0002](../adr/0002-cli-run-lifecycle.md) |
| Q16 | Containers, language level: existing-mode scope, default mount policy | answered | Ephemeral mode only (existing mode dropped); cwd always mounted read-write by default; `mounts` as `{ host, path, read_only }` mappings; [specs ADR 0005](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0005-containers-become-locks.md) |
| Q16b | Container engine detection order and termination mechanism | open (later) | |
| Q17 | `become`, language level: `defaults.become`, root-run lint, `doas`, config under sudo, transport across `become` | answered | `become`/`defaults.become`: user name or `invoker`; root-run lint warning; `sudo` and `doas` from the start; root-owned root config as euid 0; trampoline pipe transport; [specs ADR 0005](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0005-containers-become-locks.md) |
| Q18 | Out-of-band consent: mechanisms, timeout, unattended detection | answered | Dropped from 1.0; ROADMAP candidate; [specs ADR 0014](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0014-remaining-features.md) |
| Q-a | Approval binding: records, `require_hook_approval`, hook input rewriting | answered | Single-use approval records (15m TTL), hook rewrites to `blokky run --approval <id>`; `require_hook_approval` off by default; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q-b | Does `blokky run` call the validator itself without an approval record? | answered | No; only `hook check` and `validate` call it; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q-c | Runner-rules mode matching semantics and `ask` fallbacks | answered | `exec` units only, argv matched with the runner's semantics; `<dynamic>` only in a `:*` tail; script and pwsh units ask; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q-d | Trust of `XDG_CONFIG_HOME`/`APPDATA`/`HOME` vs passwd home | answered | Honour XDG/HOME/APPDATA; approval records store the resolved locations and `run --approval` fails on drift; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q-e | `setsid` via `posix_spawn` vs trampoline; per-leaf lifeline vs per-run watchdog | answered | Spike #1 decides (trampoline everywhere tested first); acceptance criteria fixed; [cli ADR 0004](../adr/0004-platform-invocation-conformance.md) |
| Q-f | Default run budget vs harness timeouts | answered | 1m50s fallback default; explicit sources may exceed it (smallest wins); hook margin 10s; [cli ADR 0002](../adr/0002-cli-run-lifecycle.md) |
| Q-g | Which dynamic constructs force `ask` by default | answered | `dynamic_exec`, `exec_env_risk`, dynamic nested plans, dynamic stdin into an interpreter, dynamic cwd, missing inputs; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q-h | Windows agent invocation; pwsh grammar for `--input` | answered | Heredoc where the shell tool is bash (incl. Git Bash); file + `--expect-sha256` in PowerShell; pwsh `--input`: bare or `'…'` with `''`; [cli ADR 0004](../adr/0004-platform-invocation-conformance.md) |
| Q-j | Nested locks: forbid or fixed order | answered | Forbidden; one lock per node, static error on nesting; [specs ADR 0005](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0005-containers-become-locks.md) |
| Q-k | Does the grace period count against `timeout`? | answered | No: TERM when `timeout` fires, KILL after grace; [specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md) |
| Q-l | `.cmd` shims from pwsh with variable args: lint or document | answered | Lint `pwsh-cmd-shim-args` (warning, strict: error) plus docs; [specs ADR 0006](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0006-shims-and-license.md) |
| Q-m | `empty`/`not_empty` on an undefined variable | answered | Condition error; [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q-n | Condition error inside `assert`: user `code`? | answered | No: built-in condition-error code, user code as context; [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q-o | Heredoc-expansion sentinel, or document the gap | answered | Document the gap; no sentinel; [cli ADR 0004](../adr/0004-platform-invocation-conformance.md) |
| Q-p | How plan variables reach commands | answered | Brief §2, §3.1: only via explicit `env:`/`export:`; [specs ADR 0001](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0001-language-baseline.md) |
| Q-q | Repo split: names, testkit interface vs implementation, submodule vs pinned checkout, spec text license | answered | Names `blokky-specs`/`blokky-cli`; testkit interface in specs, implementation in cli; Apache-2.0 for everything ([specs ADR 0006](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0006-shims-and-license.md)); pinned explicit checkout via `conformance.lock` ([cli ADR 0004](../adr/0004-platform-invocation-conformance.md)) |
| Q-r | Default/max `concurrency`; `any` error aggregation | answered | Defaults unchanged; `max_concurrency` 16 per node, clamp and warn; `any` fails with a built-in code and every cause; [specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md) |
| Q-s | May `finally` nodes `capture`? | answered | Yes, finally-local (later `finally` siblings only); [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md) |
| Q-t | May `hook check` auto-allow `clean`/`gc`? | answered | Auto-allow exactly `blokky clean <run-id>`; `clean --all` and `gc` ask; [cli ADR 0002](../adr/0002-cli-run-lifecycle.md) |
| Q-u | Does `clean` remove report and logs too? | answered | Everything except `report.json` and `events.jsonl`; `clean --all` removes those too; `gc` removes everything; [cli ADR 0002](../adr/0002-cli-run-lifecycle.md) |
| Q-in1 | Native nested-plan invocation (`call`/`plan:` node) | answered | Not planned; nested runs stay `exec` of `blokky run`; [specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md) |
| Q-in2 | Template approval for constraint-satisfying inputs | answered | No template approval in v0.1; [cli ADR 0003](../adr/0003-validation-config-lint.md) |
| Q-in3 | `--input-file` with `--expect-inputs-sha256` | answered | Later, if needed (ROADMAP candidate); [specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md) |
| Q-in4 | Secret-only input sourced from env | answered | Later, if needed (ROADMAP candidate); [specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md) |
| Q-in5 | References in today's literal fields | answered | Revisit after v0.1 (ROADMAP candidate; additive); [specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md) |
| Q-in6 | Hook verdict on input errors: `deny` (recommended) vs `ask` | answered | `deny` with the corrected command; [cli ADR 0003](../adr/0003-validation-config-lint.md) |

## Findings and follow-ups

<!-- review findings, problems discovered during work, ideas parked for later; ID F-NNNN -->

- F-0001 [both] Tag rulesets accept `required_signatures` but GitHub only checks the commits a tag points at, not the tag object's signature. Release tags are therefore created only by the maintainer as signed annotated tags (CONTRIBUTING.md), and T-0040 adds a `git verify-tag` check.
- F-0002 [both] The CI `lint` job pins linter versions in `.github/workflows/lint.yml`. Dependabot only updates the actions, so linter versions are bumped by hand.
- F-0003 [both] With `just claude`, the added repo's `CLAUDE.md` loads but its `@AGENTS.md` import isn't expanded: Claude Code treats it as an external import that needs a per-project approval. Each `CLAUDE.md` tells Claude to read the sibling's `AGENTS.md` with the Read tool instead.
- F-0004 [both] The `main` rulesets' pull-request rule came back with GitHub's default `require_extra_approval_for_unattributed_changes: true`. Harmless for signed, attributed commits; revisit if it ever blocks a merge.
- F-0005 [both] `just lint-md` failed inside Claude Code's Bash sandbox (`EACCES` on placeholder files such as `.claude/loop.md`): `.markdownlint-cli2.yaml` globbed the whole work tree, and markdownlint-cli2 adds config `globs` to command-line files, so the pre-commit hook also linted every file instead of the staged ones. Fixed 2026-11-04 (branch `chore/lint-md-tracked-files` in both repos): no `globs` in the config; `lint-md` passes `git ls-files '*.md'`. Untracked Markdown is linted once staged.
- F-0006 [both] A run budget at or below `cleanup_reserve` (default 30s), e.g. `--max-duration 20s`, would enter budget mode at once. Open: reject it as a usage error, shrink the reserve proportionally, or run without a reserve. Raised in cli ADR 0002's open items and `spec/execution-model.md` §9; decide before `cli.md`. Resolved 2026-11-05: run with no reserve (EM-54, [specs ADR 0010](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0010-variables-details.md)).
- F-0007 [specs] Nothing checks the JSON Schemas in CI yet. While drafting T-0025 they were checked by hand (meta-schema plus good and bad sample plans), which caught an inconsistent `phase` in the output-contract example. Add a schema check (meta-validation, `examples/` validated against the plan schema, fixture reports/leaves) to `just lint` and CI, with the conformance setup. Resolved 2026-11-07 by T-0060 (`just check`); fixture reports and leaves wait for the CLI's output-contract fixtures.
- F-0008 [specs] Review: a `while` body can't see the previous iteration's captures (VA-33), so cursor pagination ("fetch the next page with the last cursor") can't be written. Decide in the review whether to allow it. Raised by the examples agent ([specs ADR 0016](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0016-fixes-from-examples.md)). Folded into [council report](../review/council-draft.3.md) D-4. Decided 2026-11-06: pagination in 1.0 through `carry`, `collect` and `index` on loops.
- F-0009 [specs] Review: EM-22 stops every descendant at run end, so a plan can't start a long-lived daemon (for example Docker Desktop on Windows); `keep_descendants` only lasts until run end. Decide whether that is intended or needs an escape hatch ([specs ADR 0016](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0016-fixes-from-examples.md)). Folded into [council report](../review/council-draft.3.md) S-5. Decided 2026-11-07: `keep_descendants: detach`.
- F-0010 [specs] Add rule IDs (`SY-n`) to `spec/syntax.md` and pin the conformance feature-id vocabulary used by `BK_PLAN_NOT_SUPPORTED`'s `detail.feature`; both needed before the conformance cases (T-0052). Folded into [council report](../review/council-draft.3.md) S-11. Decided 2026-11-07: `SY-n` IDs, `detail.rule` is a rule ID.
- F-0011 [specs] Review (maintainer): no way to end a sequence early, so "nothing to do" checks wrap the rest of the plan in an `else` ([changed-files-lint](https://github.com/gergodrazsdik/blokky-specs/blob/main/examples/scenarios/changed-files-lint.blokky.yaml) lines 24–26). Decide whether to add an early-exit kind (`return`, ending the enclosing `do` list successfully; later siblings `skipped`) and its scope: plan, nearest `do` (which makes it `continue` in a `for` body), or a named node; `break` for loops; validity in `all`/`any`/`race`, `finally` and `while`/`until` bodies; effect on `outputs` and on the undefined-variable check (S-7). Decided 2026-11-06: add `return`, see [council report](../review/council-draft.3.md) D-6.
- F-0012 [specs] Review (maintainer): `{ command: /bin/sh, exists: true }` is valid (CO-45 treats a name with a separator as a path), but CO-45 doesn't say whether a path that exists and isn't executable counts as found; align it with the spawn-error rule (reference.md §5, "not executable"). `NAME` stays a literal, like argv[0] (CO-46, RF-9). Decided 2026-11-06: it must also be executable, see [council report](../review/council-draft.3.md) C-8.
- F-0013 [specs] Review features added in draft.4 without an example: `return` in a loop body and an `if` branch, `carry`/`index` on `while`, `max_items` on a non-`items` source, a JSON poll that survives a non-JSON response; the pagination example's README row didn't mention the cursor or `carry`. Fixed 2026-11-07 in [specs PR #41](https://github.com/gergodrazsdik/blokky-specs/pull/41).
- F-0014 [specs] Gaps in `conformance/README.md` found while writing the case checker: `after_run` (CF-64) was missing from the closed key list (CF-9), and its entry shape was undefined; CF-64's example called `alive --pids` expecting 1, but TK-16 is `alive FILE`, exiting 0 when nothing is alive; the result document's `skip_reason` lacked `returned` and `pruned` (results.md); a validation example asserted `invocations`, which isn't a validation-outcome key. Fixed 2026-11-07 in [specs PR #42](https://github.com/gergodrazsdik/blokky-specs/pull/42) (T-0060).
- F-0015 [specs] Spec gaps found while checking the first cases (T-0060); the cases avoid asserting on them until decided. (1) SY-1 and CF-24 define no position for an error about a missing key (RF-5's missing `max`/`timeout`, `if` without `then`): the triggering key (`until`), or no position. (2) `BK_INPUT_UNKNOWN`'s `detail.suggestion` has no closeness metric (variables.md §4.3 shows only case folding; SY-30 uses edit distance for keys), and isn't said to be required when a candidate exists. (3) EM-15 gives Windows leaves a console only as a SHOULD, so whether a timed-out leaf gets the grace period there (EM-26, `graceful: none`) is implementation-defined; cases allow both until EM-15 is a MUST or the cases branch on `graceful`. Decided 2026-11-07 ([specs ADR 0020](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0020-conformance-gaps.md), [specs PR #43](https://github.com/gergodrazsdik/blokky-specs/pull/43)): the triggering key; edit distance ≤ 2 after case mapping, required when one exists; a console per Windows leaf is a MUST.
- F-0016 [specs] Spec gaps found by the wave A cases (T-0061): one mistake with several rule IDs, overlapping plan-error codes, error positions, invalid-text handling, grammar details, input and name codes, masking, binding rules, editorial and schema fixes ([review notes](../review/conformance-wave-a.md)). Decided 2026-11-07 and applied in [specs ADR 0021](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0021-wave-a-gaps.md), [specs PR #44](https://github.com/gergodrazsdik/blokky-specs/pull/44); D-3 was withdrawn as a misreading of reference.md §4.3, now reworded.
- F-0017 [specs] Smaller points left open after ADR 0021; the wave A cases accept either answer (`$one_of`) or don't assert them. Decide before tagging draft.5 (T-0066): Decided 2026-11-07 (questionnaire) and applied in [specs ADR 0025](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0025-open-points.md).
  - Which ID a plan error reports when no RF rule applies and two SY/VA/ER rules say the same thing (SY-52 vs SY-42/VA-4/VA-6/ER-1; SY-53/SY-54/SY-57 vs SY-24/SY-25; SY-20 vs SY-41), and whether a targeted error (SY-91) reports SY-91 or the generic rule.
  - Overlaps D-1's precedence doesn't name: RF-16 vs RF-23, RF-6 vs RF-23, RF-27 vs RF-23 in `vars` values; `timeout: { var: X }` (RF-9 or a type mismatch); a regex that doesn't compile (RF-22 or its own rule); a root that is a list (VALUE or INVALID); the code of the targeted `secret:` on an input.
  - Positions: a scope error at the name inside `{ var: X }` or at the reference; a secret literal at `value` or `secret`; the depth error's column; content after `...`; an unset `from_env` in `vars` at its entry's key; an `env`/`export` name conflict; `for` without `as`.
  - Cases still missing: the 32 KiB total input limit; a valid RF-21 near-miss (`mounts: []` needs a container engine); the 8 valid `container` cells of the matrix (wave E).
- F-0018 [specs] Spec gaps found by the wave B cases (T-0062): unrun `if` branches in the result, failures inside `then`/`else`/`finally` lists, capture checks on decoded text, compact JSON, result fields (RS-6, run id, `children`, `left_running`), leaf details, `stdin` bytes, the drain bound, conformance timing ([review notes](../review/conformance-wave-b.md)). Decided 2026-11-07 and applied in [specs ADR 0022](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0022-wave-b-gaps.md).
- F-0019 [specs] Spec gaps found by the wave C cases (T-0063): glob semantics, symlinks in resolution, overlapping rule IDs and codes in conditions, positions, condition-error reasons, probe details, `host:` pruning, `.bat`/`.cmd`, backslash escapes, Windows quoting and prologue, conformance configuration ([review notes](../review/conformance-wave-c.md)). Decided 2026-11-07 and applied in [specs ADR 0023](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0023-wave-c-gaps.md).
- F-0020 [specs] Spec gaps found by the wave D cases (T-0064): the status after an own `attempt_timeout`, `finally_timeout` results, timeout codes for per-attempt `leaf_timeout`, `ended_by` and nesting on cancellation, the stopped run's error, node durations, `while` timing, services results, lock contention inside one run, and kill cases that couldn't be written ([review notes](../review/conformance-wave-d.md)). Decided 2026-11-07 and applied in [specs ADR 0024](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0024-wave-d-gaps.md).
- F-0021 [specs] Points left open after waves C and D; cases don't assert them. Decide with F-0017 before draft.5 (T-0066): when an external stop arrives during `finally` after the body already failed, is the root's error the body's error (EM-51) or `BK_CANCELLED` (EM-57); whether `?` in a glob can match a leading dot (SY-100 names only `*` and `**`); CO-46 has no observable case (only its `container` warning remains). Decided 2026-11-07 (questionnaire) and applied in [specs ADR 0025](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0025-open-points.md).
- F-0022 [specs] Points left open by the wave E combination cases; no case asserts them: Decided 2026-11-07 (questionnaire) and applied in [specs ADR 0026](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0026-runtime-edge-cases.md), [specs PR #70](https://github.com/gergodrazsdik/blokky-specs/pull/70).
  - does `finally` run when an outer layer fails the node (lock wait, `BK_CWD_INVALID`, `BK_TEMPDIR_FAILED`, a timeout during startup)?
  - are services stopped at once when the node's own timeout fires (EM-26 vs EM-7/EM-65)?
  - what `repetition` a node records that fails before its first attempt;
  - the status of an entry interrupted by the node's own timeout;
  - whether the `finally` and child nodes of a node skipped by `when` appear in the result (RS-3 vs RS-6), and what a node that failed in `when` records;
  - how soon a waiter gets a released lock (no polling bound, unlike EM-62).
- F-0023 [specs] Points left open by the wave E container cases; no case asserts them: Decided 2026-11-07 (questionnaire) and applied in [specs ADR 0026](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0026-runtime-edge-cases.md), [specs PR #70](https://github.com/gergodrazsdik/blokky-specs/pull/70).
  - an image-reference grammar, and rule IDs for a missing `image` or mount `path`;
  - the subkeys of the result's `container` field;
  - error codes when argv[0] or the shell is missing from the image (CP-15);
  - shell detection under `container`;
  - CP-36's `detail.missing` form;
  - the default cwd mount for an absolute host `cwd` (CP-9 vs CP-14);
  - CP-10's wording;
  - `env_pass` vs `env` overlaps;
  - a container leaf's `invocation`;
  - absolute container paths on Windows hosts.
- F-0024 [specs] Small points left open after ADR 0026 (cases don't assert them): `ended_by` for a node that times out before its first attempt; the `image_digest` value form, engine name and container id form; a when-probe's `result` on a condition error; how "under a mount's host path" is compared (symlinks, letter case on Windows).

## Decisions

<!-- one line per decision with a link to its ADR -->

- 2026-11-04 [specs] Language baseline from brief §1–§3 (scope, YAML surface, nodes, leaves and shells, variables, inputs, conditions, contracts): [specs ADR 0001](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0001-language-baseline.md)
- 2026-11-04 [cli] Implementation, distribution and process baseline from brief §0–§2 (repo split, .NET 11 AOT, architecture, CLI rules, config trust classes, process control, artefacts, distribution, process): [cli ADR 0001](../adr/0001-baseline.md)
- 2026-11-04 [specs] Language core, batch 1 (Q1, Q2, Q3, Q12, Q13, Q-m, Q-n, Q-s): modifier order confirmed, jq-like JSON paths, `collect` later, whole-value regex search, trailing-newline trimming, decimal numbers, `blokky:` required as `MAJOR.MINOR`, finally-local captures: [specs ADR 0002](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0002-language-core.md)
- 2026-11-04 [specs] Execution and defaults, batch 2 (Q4, Q5, Q7, Q8, Q-k, Q-r): shell set and detection confirmed, bash `pipefail` without `-e`, `sh` without options, PowerShell stops on cmdlet errors, Git Bash only by config path, no implicit leaf or group timeouts, grace outside `timeout`, per-node concurrency cap 16, output and capture limits, `max_items` default 1000: [specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md)
- 2026-11-04 [cli] CLI and run lifecycle, batch 3 (Q6, Q15, Q-f, Q-t, Q-u): dense exit-code table, partial cleanup exits 9 with a JSON list, 1m50s fallback run budget with a 10s hook margin, `clean` keeps report and events, hook auto-allows `clean <run-id>` only: [cli ADR 0002](../adr/0002-cli-run-lifecycle.md)
- 2026-11-04 [specs] Validator protocol and input scope, batch 4 (Q9, Q-in1, Q-in3, Q-in4, Q-in5): spawn-per-request JSON validator, failures → ask, per-unit verdicts; no nested-plan node; input conveniences later: [specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md)
- 2026-11-04 [cli] Validation, hook, config and lint, batch 4 (Q10, Q11, Q-a, Q-b, Q-c, Q-d, Q-g, Q-in2, Q-in6): single-use approval records with rewriting, rules mode for `exec` units, code-like dynamics force ask, input errors deny, env-honouring config with a drift check, user-only key list, kebab-case lint rules with a `strict` preset: [cli ADR 0003](../adr/0003-validation-config-lint.md)
- 2026-11-04 [specs] Containers, `become` and locks, batch 5 (Q16, Q17, Q-j): ephemeral containers only with the cwd mounted by default and mapping-form `mounts`, `become`/`defaults.become` with `invoker`, `sudo` and `doas`, root-owned config as root, trampoline transport, no nested locks: [specs ADR 0005](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0005-containers-become-locks.md)
- 2026-11-04 [specs] `.cmd` shims and the spec license, batch 6 (Q-l, Q-q part): `pwsh-cmd-shim-args` lint, Apache-2.0 for all of blokky-specs, testkit split confirmed: [specs ADR 0006](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0006-shims-and-license.md)
- 2026-11-04 [cli] Launcher, Windows invocation and conformance, batch 6 (Q-e, Q-h, Q-o, Q-q part): spike #1 decides the launcher against fixed criteria, heredoc in bash and file form in PowerShell, no heredoc sentinel, pinned explicit conformance checkout: [cli ADR 0004](../adr/0004-platform-invocation-conformance.md)
- 2026-11-04 [specs] Syntax details raised while drafting (T-0015): plan caps 4 MiB / depth 128, input caps 4 KiB / 32 KiB, strict integer-unit durations, empty lists only where a field allows them (`mounts`): [specs ADR 0007](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0007-syntax-details.md)
- 2026-11-04 [specs] Repetition details raised while drafting (T-0016): `every` is a delay after the end (default 1s), mapping backoff `{ factor, max }` and fractional jitter, `retry: true` retries process failures only (default `max` 3), zero-iteration `while` succeeds, 0-based `count` items: [specs ADR 0008](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0008-repetition-details.md)
- 2026-11-05 [specs] Status propagation raised while drafting (T-0017): all-skipped groups succeed, ignored failures don't win `any`, `timed_out`/`cancelled` only for the node's own cause, root `when` false exits 0, `ignore_errors` covers `when` condition errors, reap timeout 5s: [specs ADR 0009](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0009-status-propagation.md)
- 2026-11-05 [specs] Short budgets and variable details (F-0006, T-0018): budgets at or below `cleanup_reserve` run without a reserve, JSON `null` binds `''`, masking from 6 characters, undecodable captures are errors: [specs ADR 0010](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0010-variables-details.md)
- 2026-11-05 [specs] PowerShell prologue placement (T-0020): `$ErrorActionPreference = 'Stop';` (followed by a space) prefixes the first statement line (after blanks, comments and `using`), `param(` there is a plan error: [specs ADR 0011](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0011-pwsh-prologue.md)
- 2026-11-05 [specs] Container details (T-0021): on Windows hosts the cwd mounts at its drive-letter path (`/c/src/app`); containers run as the image's default user, with a `container-root-write` lint warning: [specs ADR 0012](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0012-container-details.md)
- 2026-11-05 [cli] Exit-code details (T-0026): pre-run errors exit with the first failing step in check order (args, sha, config, plan, inputs, approval); Windows session events exit 129: [cli ADR 0005](../adr/0005-exit-code-details.md)
- 2026-11-05 [both] Direction revised: blokky-specs is language-only; the spec is completed as `1.0.0-draft.N` with all features and no milestone tags; phase order spec → examples → review (maintainer + agent council) → conformance suite → runtime planning, spikes, incremental CLI: [cli ADR 0006](../adr/0006-phase-order-and-incremental-cli.md), [specs ADR 0013](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0013-scope-and-versioning.md)
- 2026-11-05 [specs] Remaining features, batch 7 (T-0047): `race` like `do`/`any`; `lines` drops blank lines (`keep_blank`); plain `matrix`; `items` with `fields`; `collect` as a JSON array; per-user exclusive locks waiting within `timeout`; `services` in parallel with `depends_on`, required `ready:` (30s/250ms), per-service `on_exit`, status only; `consent` dropped; `zsh`/`fish` rows: [specs ADR 0014](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0014-remaining-features.md)
- 2026-11-06 [specs] Setting variables: a `set` node kind, root `vars:` constants, `${}` templates allowed only in `set`/`vars` values (no functions), single assignment with definite assignment through branches: [specs ADR 0015](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0015-set-and-vars.md)
- 2026-11-06 [specs] Fixes from the example suite: a failed group carries its child's error, ordered `any` skips `when`-skipped children, new `command:` condition subject, `${` allowed in script text, fixed `race` example: [specs ADR 0016](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0016-fixes-from-examples.md)
- 2026-11-07 [specs] Council review, features: `become` and `fish` cut, smaller `container`, `services` in list order, `return`, `carry`/`index`/`collect` on loops (pagination), `keep_descendants: detach`, `tempdir.remove`: [specs ADR 0017](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0017-review-features.md)
- 2026-11-07 [specs] Council review, semantics S-1 to S-14 (JSON polls, retry by origin, joins wait for `finally`, leaf completion order, EM-22 split, syntactic scope check, portable regex and glob, `from_env` like inputs, `SY-n` IDs): [specs ADR 0018](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0018-review-semantics.md)
- 2026-11-07 [specs] Council review, corrections C-1 to C-8 (schema errors at their key, canonical forms, security wording, conformance kill action, `command:` requires an executable): [specs ADR 0019](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0019-review-corrections.md)
