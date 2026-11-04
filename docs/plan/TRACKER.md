# Blokky work tracker

One tracker for both repos (blokky-cli and blokky-specs). Agents keep it current; humans skim it. Rules are in [AGENTS.md](../../AGENTS.md#work-tracker); the process is brief §8.

Legend: [ ] todo · [~] in progress · [x] done · [-] dropped · [!] blocked/needs decision

## Now

<!-- the next 3–5 items, in order; agents pick work from here -->

1. T-0007 [both] Baseline ADR per repo
2. T-0008 [specs] Ask open-question batch 1 (language core)
3. T-0009 [specs] Ask open-question batch 2 (execution and defaults)
4. T-0010 [cli] Ask open-question batch 3 (CLI and run lifecycle)

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

Proposed batches; IDs are the brief's. Each batch ends in one grouped ADR in the repo it concerns.

- [ ] T-0007 [both] Baseline ADR per repo: record brief §2 baseline decisions (specs: language; cli: runtime, distribution, process)
- [ ] T-0008 [specs] Batch 1, language core: Q1, Q2, Q3, Q12, Q13, Q-m, Q-n, Q-s
- [ ] T-0009 [specs] Batch 2, execution and defaults: Q4, Q5, Q7, Q8, Q-k, Q-r
- [ ] T-0010 [cli] Batch 3, CLI and run lifecycle: Q6, Q15, Q-u, Q-f, Q-t
- [ ] T-0011 [both] Batch 4, validation, config and inputs: Q9, Q10, Q11, Q-a, Q-b, Q-c, Q-d, Q-g, Q-in1, Q-in2, Q-in3, Q-in4, Q-in5, Q-in6
- [ ] T-0012 [specs] Batch 5, containers, `become` and locks (language level): Q16, Q17, Q-j
- [ ] T-0013 [both] Batch 6, platform and repo split: Q-e, Q-h, Q-l, Q-o, Q-q

### M1.2 Draft spec PRs

Main agent, sequential, PRs marked **Draft spec** (brief §8.3).

- [ ] T-0014 [specs] `spec/glossary.md`
- [ ] T-0015 [specs] `spec/syntax.md` (surface rules, root document, inputs syntax, per-field table, pitfalls, targeted errors)
- [ ] T-0016 [specs] `spec/reference.md` (all planned features, milestone-tagged, validity matrix)
- [ ] T-0017 [specs] `spec/execution-model.md`
- [ ] T-0018 [specs] `spec/variables.md`
- [ ] T-0019 [specs] `spec/conditions.md`
- [ ] T-0020 [specs] `spec/shells.md`
- [ ] T-0021 [specs] `spec/containers-and-privilege.md`
- [ ] T-0022 [specs] `spec/errors.md`
- [ ] T-0023 [specs] `spec/output-contract.md`
- [ ] T-0024 [specs] `spec/leaves-and-validation.md`
- [ ] T-0025 [specs] All schemas: `blokky`, `report`, `events`, `leaves`, `validator`
- [ ] T-0026 [cli] Exit-code table in `docs/cli.md`

## Phase 2 — Spike plan and spikes (brief §8.4)

- [ ] T-0027 [cli] `docs/design/spike-plan.md`: every "Provisional — verify in spike" item (brief §8.4 list 1–12)
- [ ] T-0028 [cli] Maintainer approves the spike plan
- [ ] T-0029 [cli] Spikes on unmerged `spikes/*` branches; one task per spike is added once the plan is approved
- [ ] T-0030 [both] Fold spike findings back into the draft

## Phase 3 — Fan-out docs (brief §8.5)

Only after the language spec draft is merged. One subagent, worktree, branch and PR each.

- [ ] T-0031 [specs] (a) `examples/`, `conformance/README.md`, `conformance/testkit-interface.md`, `spec/overview.md`, `spec/prior-art.md`
- [ ] T-0032 [cli] (b) `configuration.md`, config schema (main agent owns; subagent proposes), `validation-and-permissions.md`, `security/threat-model.md`
- [ ] T-0033 [cli] (c) agent docs: `agent-guide.md`, `skill/SKILL.md`, `mcp-tool-description.md`
- [ ] T-0034 [both] (d) user/project docs: README, install, contributing, SECURITY (after (b)), roadmaps, CHANGELOG, NOTICE, CODEOWNERS, dev-setup, `mcp.md` (bootstrap versions exist; this pass completes them)
- [ ] T-0035 [cli] (e) design docs: architecture, tech-stack, process-control, validation-strategy, testkit, ci-cd, `design/notes/`

## Phase 4 — Consistency pass and finalized spec (brief §8.6–8.7)

- [ ] T-0036 [both] Consistency pass: reference entry and example per kind/modifier; examples use canonical forms; schemas match reference; error codes; glossary; every "later" feature in the specs ROADMAP; cross-repo links
- [ ] T-0037 [both] Finalize the spec: resolve or re-scope every provisional item

## Phase 5 — v0.1 implementation (planned after spec approval)

Placeholder; broken down after the spec is approved.

- [ ] T-0038 [cli] Enable CodeQL (C#) once C# code lands
- [ ] T-0039 [cli] Enable the Dependabot `nuget` block once the solution exists
- [ ] T-0040 [cli] Release workflow verifies the release tag's signature (`git verify-tag`): GitHub tag rulesets can't check tag-object signatures
- [ ] T-0041 [both] Protected `release` environment with manual approval (brief §7)

## Open questions

Seeded from brief §4, keeping its IDs. "Draft" = needed for the language spec draft; "later" = asked when the related spike or milestone comes up.

| ID | Question (short) | Status | Answer / ADR |
|---|---|---|---|
| Q1 | Confirm the modifier order (§3.1) | open (draft) | |
| Q2 | JSON path subset; `collect` milestone and syntax | open (draft) | |
| Q3 | Condition tables; `matches` semantics; trimming; number grammar; `probe_timeout` and probe capture limit | open (draft) | |
| Q4 | Shells: v0.1 set, detection lists, invocation table (pipefail/-e), Git Bash policy | open (draft) | |
| Q5 | Duration defaults: global `timeout`, `leaf_timeout`, `finally_timeout`, `drain_timeout`, `cleanup_reserve` | open (draft) | |
| Q6 | Exit codes of `blokky` | open (draft) | |
| Q7 | Output defaults: tail size, report byte budget, log caps | open (draft) | |
| Q8 | Default `max_items` for `for` over captured output | open (draft) | |
| Q9 | Validator plugin protocol | open (draft) | |
| Q10 | Strict-mode lint rule set: rules, IDs, severities, config, suppression | open (draft) | |
| Q11 | Config file names; full list of user-only keys | open (draft) | |
| Q12 | Must `name` values be unique? | open (draft) | |
| Q13 | Plan version scheme for `blokky:`; required? | open (draft) | |
| Q14 | Leaf descendants at leaf completion | answered | Brief §3.8: killed by default; `keep_descendants: true` opts out |
| Q15 | How `clean` reports partial deletion failure; exit code | open (draft) | |
| Q16 | Containers, language level: existing-mode scope, default mount policy | open (draft) | |
| Q16b | Container engine detection order and termination mechanism | open (later) | |
| Q17 | `become`, language level: `defaults.become`, root-run lint, `doas`, config under sudo, transport across `become` | open (draft) | |
| Q18 | Out-of-band consent: mechanisms, timeout, unattended detection | open (later) | |
| Q-a | Approval binding: records, `require_hook_approval`, hook input rewriting | open (draft) | |
| Q-b | Does `blokky run` call the validator itself without an approval record? | open (draft) | |
| Q-c | Runner-rules mode matching semantics and `ask` fallbacks | open (draft) | |
| Q-d | Trust of `XDG_CONFIG_HOME`/`APPDATA`/`HOME` vs passwd home | open (draft) | |
| Q-e | `setsid` via `posix_spawn` vs trampoline; per-leaf lifeline vs per-run watchdog | open (draft; spike) | |
| Q-f | Default run budget vs harness timeouts | open (draft) | |
| Q-g | Which dynamic constructs force `ask` by default | open (draft) | |
| Q-h | Windows agent invocation; pwsh grammar for `--input` | open (draft) | |
| Q-j | Nested locks: forbid or fixed order | open (draft) | |
| Q-k | Does the grace period count against `timeout`? | open (draft) | |
| Q-l | `.cmd` shims from pwsh with variable args: lint or document | open (draft) | |
| Q-m | `empty`/`not_empty` on an undefined variable | open (draft) | |
| Q-n | Condition error inside `assert`: user `code`? | open (draft) | |
| Q-o | Heredoc-expansion sentinel, or document the gap | open (draft) | |
| Q-p | How plan variables reach commands | answered | Brief §2, §3.1: only via explicit `env:`/`export:` |
| Q-q | Repo split: names, testkit interface vs implementation, submodule vs pinned checkout, spec text license | open (draft); partly settled by bootstrap | Repos created as `blokky-specs`/`blokky-cli`; both currently Apache-2.0 (spec-text license still open) |
| Q-r | Default/max `concurrency`; `any` error aggregation | open (draft) | |
| Q-s | May `finally` nodes `capture`? | open (draft) | |
| Q-t | May `hook check` auto-allow `clean`/`gc`? | open (draft) | |
| Q-u | Does `clean` remove report and logs too? | open (draft) | |
| Q-in1 | Native nested-plan invocation (`call`/`plan:` node) | open (draft; recommended: not now) | |
| Q-in2 | Template approval for constraint-satisfying inputs | open (draft; recommended: no for v0.1) | |
| Q-in3 | `--input-file` with `--expect-inputs-sha256` | open (later, if needed) | |
| Q-in4 | Secret-only input sourced from env | open (later, if needed) | |
| Q-in5 | References in today's literal fields | open (revisit after v0.1) | |
| Q-in6 | Hook verdict on input errors: `deny` (recommended) vs `ask` | open (draft) | |

## Findings and follow-ups

<!-- review findings, problems discovered during work, ideas parked for later; ID F-NNNN -->

- F-0001 [both] Tag rulesets accept `required_signatures` but GitHub only checks the commits a tag points at, not the tag object's signature. Release tags are therefore created only by the maintainer as signed annotated tags (CONTRIBUTING.md), and T-0040 adds a `git verify-tag` check.
- F-0002 [both] The CI `lint` job pins linter versions in `.github/workflows/lint.yml`. Dependabot only updates the actions, so linter versions are bumped by hand.
- F-0003 [both] With `just claude`, the added repo's `CLAUDE.md` loads but its `@AGENTS.md` import isn't expanded: Claude Code treats it as an external import that needs a per-project approval. Each `CLAUDE.md` tells Claude to read the sibling's `AGENTS.md` with the Read tool instead.
- F-0004 [both] The `main` rulesets' pull-request rule came back with GitHub's default `require_extra_approval_for_unattributed_changes: true`. Harmless for signed, attributed commits; revisit if it ever blocks a merge.

## Decisions

<!-- one line per decision with a link to its ADR -->
