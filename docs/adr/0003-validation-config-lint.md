# 0003: Validation, hook, configuration and lint (open-question batch 4)

- Status: accepted
- Date: 2026-11-04
- Questions: Q10, Q11, Q-a, Q-b, Q-c, Q-d, Q-g, Q-in2, Q-in6

## Context and problem statement

The [specification-phase brief](../design/brief.md) §4 lists questions that must be answered before the language spec draft and the validation and configuration docs are written. Batch 4 covers validation, configuration and inputs. This ADR records the implementation-side answers: approval binding, which commands call the validator, runner-rules matching, which constructs force `ask`, the hook verdict on input errors, how the user config is located, config file names and user-only keys, and the strict-mode lint scheme. The validator protocol and the input-scope answers (Q9, Q-in1, Q-in3, Q-in4, Q-in5) are in [blokky-specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md).

Each question was asked as a multiple choice with a recommendation, on 2026-11-04. The options considered are listed per decision below; the chosen one is marked.

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| Q-a approval binding | **single-use approval records with input rewriting**; defer records; reusable records |
| Q-in2 template approval | **not in v0.1**; later, opt-in |
| Q-b `run` calls the validator | **no; `require_hook_approval` enforces**; yes |
| Q-c rules-mode matching | **`exec` units only**; also simple bash scripts; rules mode later |
| Q-g constructs that force `ask` | **code-like dynamics**; every dynamic value; only `dynamic_exec` and `exec_env_risk` |
| Q-in6 hook on input errors | **`deny` with fix-it**; `ask` |
| Q-d locating the user config | OS account dirs, env ignored; **honour env, with a drift check** |
| Q11a file names | **as in the brief**; `.jsonc` extension; root-level repo files |
| Q11b user-only keys | **executables, locations, enabling switches, approval and rules settings**; also lint and strict mode |
| Q10a lint scheme | **kebab-case names, four severities, `strict` preset, per-rule config**; codes plus names; fixed severities |
| Q10b in-plan suppression | **none, config only**; node-level key; comment directives |
| Q10c initial rule set | **the set below**; a smaller start |

## Decision outcome

All answers follow the recommendation except Q-d, where the maintainer chose to honour `XDG_CONFIG_HOME`, `HOME` and `APPDATA`, with a drift check, over ignoring them.

### Q-a and Q-in2: approval binding

- **Records.** When `hook check` answers `allow` or `ask` for `blokky run`, it writes an approval record to the state dir (0600). The record holds the plan bytes, the effective inputs, the cwd, the spec version, the budget, the resolved config and state locations (Q-d), and the invocation digest `{ plan_sha256, spec_version, inputs, cwd }`.
- **Rewriting.** The hook rewrites the tool input to `<abs>/blokky run --approval <id> --max-duration <t>`, where `<abs>` is the hook's own binary. On `ask`, the reason shows the leaves summary (`rendered` lines and plan-dependent effects), so the human approves what will run.
- **Execution.** `blokky run --approval <id>` executes the stored plan bytes with the stored inputs. It fails with `BK_APPROVAL_MISMATCH` when the cwd or the resolved locations differ, and with a distinct expiry error after the TTL.
- **Single use.** A record is consumed by the run that uses it; a retry goes through the hook again.
- **TTL:** 15m by default (`approval_ttl`, user-only). An `ask` left unanswered for longer than the TTL produces a run that fails with the expiry error, and the agent retries.
- **`require_hook_approval`** (user-only, default off): `blokky run` without `--approval` refuses to start and exits 5 ("approval needed").
- **Spike:** confirm that Claude Code shows and runs the rewritten input on `ask` as well as on `allow`.
- **Q-in2:** there is no template approval in v0.1. Every record binds concrete inputs; input constraints are author hygiene and never widen an approval.

### Q-b: which commands call the validator

Only `hook check` and `validate`. `blokky run` never calls it; enforcement on `run` is `require_hook_approval`. Running a plan by hand in a terminal stays like running bash.

### Q-c: runner-rules mode

`hook check --rules` matches units against the runner's own Bash allow and deny rules:

- **`exec` leaves and `exec` probes** are rendered as a POSIX command line from the argv alone and matched with the runner's semantics (exact and `prefix:*` rules). Env assignments aren't part of the matched text; they are checked separately (`exec_env_risk` → `ask`).
- A `<dynamic>` argv element matches only inside a rule's `:*` wildcard tail, never in the matched prefix.
- **Always `ask`:** `script` units, PowerShell units, units with `exec_env_risk`, and every construct in Q-g.
- **Result:** all units allowed → `allow`; any unit denied → `deny` (deny rules win); otherwise `ask`.
- Where the hook reads the runner's rules from (Claude Code settings files) is set by the user-only `hook.rules` settings and specified in `validation-and-permissions.md`.

### Q-g: constructs that force `ask`

Unless a validator explicitly allows the unit (per-unit verdict, blokky-specs ADR 0004), these give `ask`:

- `dynamic_exec`: a variable used as code (`sh -c` with a variable, `eval`, `source`, `iex`, a variable in command position);
- `exec_env_risk`: execution-influencing environment variables;
- dynamic nested plans;
- dynamic stdin into a known interpreter;
- a dynamic cwd;
- missing required inputs (as the brief says).

Dynamic argv elements and ordinary dynamic env values (from captures) don't force `ask` by themselves; rules matching (Q-c) or the validator decides.

### Q-in6: input errors in the hook

`hook check` answers `deny` with the exact corrected command when `--input` values are invalid: an unknown name, a missing required input, a constraint violation or non-canonical quoting. The call would fail with exit 2 anyway, so the agent retries without bothering the human.

### Q-d: locating the user config and state dir

Blokky honours `XDG_CONFIG_HOME`, `XDG_STATE_HOME` and `HOME` on Unix and `APPDATA`/`LOCALAPPDATA` on Windows, like other tools, with a **drift check**:

- the approval record stores the config and state locations the hook resolved, and the environment values they came from;
- `blokky run --approval` resolves them again and fails with `BK_APPROVAL_MISMATCH` on any difference, so an agent can't point the approved run at a config it wrote;
- `blokky config show --effective` and the report show the resolved locations and which variable chose them.

Without an approval record (a run by hand, or `require_hook_approval` off), the resolved locations are reported but not checked. That matches plain bash, where the caller's environment is trusted.

### Q11: configuration files and user-only keys

**File names confirmed** (JSONC, `.json` extension; comments allowed, as the schema states):

1. user: `$XDG_CONFIG_HOME/blokky/config.json` (default `~/.config/blokky/config.json`); Windows `%APPDATA%\blokky\config.json`;
2. repo: `.blokky/config.json` at the repo root, committed;
3. repo-local: `.blokky/config.local.json`, gitignored by convention.

**User-only keys** (published as `x-blokky-trust: user-only`; set in a repo, repo-local or `--config` file → config error):

| Group | Keys |
|---|---|
| Executables | `validator.path`, `validator.timeout`; `shells.<name>.path` (including Git Bash on Windows); the container engine path (later) |
| Locations | `state_dir`, `temp_root` |
| Enabling switches | `become` (later), the consent mechanism (later) |
| Approval and hook | `require_hook_approval`, `approval_ttl`, `hook.rules` (rules-mode switch and rule sources) |

Everything else is a free key: defaults, timers, caps, tails, the report size, lint severities, `strict`, `max_concurrency`, probe limits and the run budget. Exact key names and the schema are in `configuration.md` and `schema/blokky-config.schema.json`.

### Q10: strict-mode lint

**Scheme.**

- Rule IDs are stable kebab-case names (`posix-unquoted-var`); there are no numeric codes.
- Severities: `off`, `info`, `warning`, `error`. Only `error` blocks `run` (exit 3, plan invalid). Warnings go to stderr and the report.
- `strict: true` is a preset that raises the rules marked S below to `error`.
- Per-rule overrides use `lint.rules.<id>` (free keys); a user who wants a floor sets it at user level. Flags and env vars can't relax severities.
- Heuristic rules default to at most `warning`, so outside strict mode a false positive never blocks a run.

**Suppression:** none in the plan in v0.1; repo or user config sets a rule's severity.

**Initial rule set** (default severity; S = raised to `error` by `strict`):

| Rule | Default | Rule | Default |
|---|---|---|---|
| `script-block-scalar` | warning | `duplicate-sibling-name` | info |
| `if-without-else` | info | `input-in-stdin-plan` | info |
| `exec-bracket-test` | info | `input-secret-name` | warning |
| `ignore-errors-capture` | info | `input-unused` | warning |
| `while-reads-body-capture` | warning | `input-never-matches` | warning |
| `when-scope` | warning | `argument-injection-dashdash` | info |
| `slow-probe` | info | `mangled-encoding` | info |
| `timeout-exceeds-budget` | warning | `concurrency-clamped` | warning |
| `repeat-without-attempt-timeout` | warning | | |

Script hygiene (heuristic): `script-unexported-var`, `posix-unquoted-var`, `pwsh-env-var-syntax` and `prompting-command`, all `warning`, S.

Strict-only (`off` by default, S): `script-sleep-loop`, `script-trap`, and `raw-privilege-escalation` (later).

Later rules: `container-privileged`, `path-on-container`, `root-run`, `script-no-shell-features`. Q-l (`.cmd` shims) may add one in batch 6.

### Consequences

- Good: what the human approves is byte-for-byte what runs, independent of PATH, binary or file changes between hook and run.
- Good: rules mode gives "Blokky ≡ the same bash calls" without a custom validator, for `exec`-only plans.
- Good: lint can't block a run on a heuristic false positive unless the user opted into strict mode.
- Neutral: plans built from `script` leaves always ask in rules mode; a validator is the way to allow them automatically.
- Bad: approval records and rewriting add state, a TTL and an expiry error that agents must handle; the behaviour depends on a spike result (rewritten input on `ask`).
- Bad: honouring env for config location (Q-d) leaves a run by hand open to a redirected config; only approved runs are checked.

## More information

- Brief: §2 (flag classes), §3.3 (inputs in the hook), §3.4 (configuration), §3.9 (validation, hook), §4 (questions).
- Tracker: T-0011; the answers are linked from the [open-questions table](../plan/TRACKER.md#open-questions).
- Docs that apply these answers: `docs/validation-and-permissions.md` (Q-a, Q-b, Q-c, Q-g, Q-in6), `docs/configuration.md` and `schema/blokky-config.schema.json` (Q-d, Q11), the lint rule reference in `docs/cli.md` or its own page (Q10).
- Companion: [blokky-specs ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md) (Q9, Q-in1, Q-in3, Q-in4, Q-in5).
