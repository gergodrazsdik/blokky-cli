# Blokky work tracker

One tracker for both repos (blokky-cli and blokky-specs). Agents keep it current; humans skim it. Rules are in [AGENTS.md](../../AGENTS.md#work-tracker); the process is brief §8.

Legend: [ ] todo · [~] in progress · [x] done · [-] dropped · [!] blocked/needs decision

## Now

<!-- the next 3–5 items, in order; agents pick work from here -->

1. T-0043 [both] Move the JSON contracts and CLI-only error codes to blokky-cli
2. T-0044 [specs] Remove runtime mechanics from the spec text
3. T-0045 [specs] Abstract run-result model
4. T-0046 [specs] Drop milestone tags; `blokky: 1.0`

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

blokky-specs becomes language-only; the spec moves to `1.0.0-draft.N` without milestone tags ([cli ADR 0006](../adr/0006-phase-order-and-incremental-cli.md), [specs ADR 0013](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0013-scope-and-versioning.md)).

- [ ] T-0043 [both] Move `output-contract.md`, `leaves-and-validation.md`, the report/events/leaves/validator schemas and the CLI-only error codes to blokky-cli (`docs/contracts/`, `schema/`)
- [ ] T-0044 [specs] Remove runtime mechanics from the spec text (budget and config keys, hook, approval, lint rules, run dir, OS process control), keeping the observable rules and pointing to blokky-cli; update the glossary
- [ ] T-0045 [specs] Abstract run-result model (`spec/results.md`) for conformance cases
- [ ] T-0046 [specs] Drop the `v0.1`/`later` tags; plans write `blokky: 1.0`; README, ROADMAP, CHANGELOG and schema `$id`s for `1.0.0-draft.N`

### M1.4 Remaining language features (brief §8 step 5)

- [ ] T-0047 [specs] Ask the open questions for the features still sketched (batch 7)
- [ ] T-0048 [specs] Specify `race`, `lock`, `services`, `items`/`lines`/`matrix`, `collect`, `consent`, `zsh`/`fish` to full depth
- [ ] T-0049 [specs] Maintainer tags `1.0.0-draft.1`

## Phase 2 — Example suite (brief §8 step 6)

- [ ] T-0031 [specs] `examples/` (small per-feature and comprehensive examples covering every feature), `conformance/README.md`, `conformance/testkit-interface.md`, `spec/overview.md`, `spec/prior-art.md`

## Phase 3 — Review (brief §8 step 7)

- [ ] T-0050 [both] Agent council review: propose the council (lenses, prompts, output format), run it, verify and consolidate findings
- [ ] T-0051 [both] Maintainer review; accepted findings as ADRs; next draft (`1.0.0-draft.N`), including features cut or kept
- [-] T-0036 [both] Consistency pass — dropped 2026-11-05: replaced by the review phase (cli ADR 0006)

## Phase 4 — Conformance suite (brief §8 step 8)

- [ ] T-0052 [specs] Conformance cases for the features that survive the review, each citing its rule IDs (see F-0007 for schema checks)

## Phase 5 — Runtime planning, spikes and incremental CLI (brief §8 steps 9–11)

- [ ] T-0053 [cli] Runtime planning session: MVP feature set, feature order, CLI versioning, when each CLI doc is needed
- [ ] T-0027 [cli] `docs/design/spike-plan.md`: every "Provisional — verify in spike" item (brief §8.4 list 1–12)
- [ ] T-0028 [cli] Maintainer approves the spike plan
- [ ] T-0029 [cli] Spikes on unmerged `spikes/*` branches; one task per spike is added once the plan is approved
- [ ] T-0030 [both] Fold spike findings back into the draft
- [ ] T-0032 [cli] (b) `configuration.md`, config schema (main agent owns; subagent proposes), `validation-and-permissions.md`, `security/threat-model.md`
- [ ] T-0033 [cli] (c) agent docs: `agent-guide.md`, `skill/SKILL.md`, `mcp-tool-description.md`
- [ ] T-0034 [both] (d) user/project docs: README, install, contributing, SECURITY (after (b)), roadmaps, CHANGELOG, NOTICE, CODEOWNERS, dev-setup, `mcp.md` (bootstrap versions exist; this pass completes them)
- [ ] T-0035 [cli] (e) design docs: architecture, tech-stack, process-control, validation-strategy, testkit, ci-cd, `design/notes/`
- [ ] T-0037 [both] Spec `1.0.0` once the runtime has validated the draft (was: finalize the spec after the spikes)
- [ ] T-0038 [cli] Enable CodeQL (C#) once C# code lands
- [ ] T-0039 [cli] Enable the Dependabot `nuget` block once the solution exists
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
| Q18 | Out-of-band consent: mechanisms, timeout, unattended detection | open (later) | |
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
- F-0007 [specs] Nothing checks the JSON Schemas in CI yet. While drafting T-0025 they were checked by hand (meta-schema plus good and bad sample plans), which caught an inconsistent `phase` in the output-contract example. Add a schema check (meta-validation, `examples/` validated against the plan schema, fixture reports/leaves) to `just lint` and CI, with the conformance setup.

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
