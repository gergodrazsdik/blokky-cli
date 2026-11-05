# Blokky — specification phase brief (for Claude Code)

You are starting the **specification phase** of Blokky, a new open-source project on GitHub. It uses two repositories (§0.1).

> **Revised 2026-11-05** (blokky-cli ADR 0006, blokky-specs ADR 0013): blokky-specs covers only the language; the spec is completed as `1.0.0-draft.N` with all planned features and no milestone tags; the phase order is spec → examples → review → conformance suite → runtime planning, spikes and an incremental CLI (§1.5, §1.6, §8). Sections §2–§4 still use `v0.1`/`later` tags; in the spec they are historical, and they now feed the CLI planning session.

This phase produces **documentation and specification**:
- The only code allowed is throwaway spike code on unmerged `spikes/*` branches, after I approve the spike plan (§8).
- Implementation starts after the spec draft has been reviewed and the runtime planning session has set the MVP scope (§8). The spec stays a draft during implementation and is refined by it.

---

## 0. Working agreement

### 0.1 Repositories

The **language specification** is versioned separately from the **implementation**.

| Repo | Contents | Versioning |
|---|---|---|
| `gergodrazsdik/blokky-specs` | **The language only:** what a plan means and what it can observe (syntax, semantics, the plan-visible error codes, an abstract run-result model), the plan schema, the user-facing example suite, the conformance suite (cases + format + testkit *interface*). | Own semver: pre-releases `1.0.0-draft.N` until `1.0.0`. A plan's `blokky:` field names `MAJOR.MINOR` (`blokky: 1.0`). |
| `gergodrazsdik/blokky-cli` | .NET implementation (CLI host, later the MCP host) and everything runtime-specific: the JSON contracts (report, events, `leaves`, validator protocol) and their schemas, exit codes and CLI-only error codes, run dir and cleanup, configuration, run budget, validation/hook integration, approval, lint rules, OS process control, testkit implementation, threat model, agent docs, install, design docs, CI/CD. | Own semver, incremental: a release may support only part of the language; it declares the spec version and a feature matrix, and rejects unsupported features with "not supported by this blokky version". |

**Working with both repos:**
- Claude Code works in `blokky-cli` with `blokky-specs` loaded as an additional directory, or the other way round, depending on where the current step's files live.
- Each repo has its own branches, PRs, ADRs and CHANGELOG.
- **Conformance:** `blokky-cli` CI checks out `blokky-specs` at a pinned tag (submodule or explicit checkout; decided in `ci-cd.md`) and runs the conformance suite against the AOT binary.
- **A change that touches both repos** gets two linked PRs. The spec PR merges first.

### 0.2 Git, PRs and parallel work

- **Branches.** Work on feature branches named `docs/<topic>` (spikes: `spikes/<topic>`). Never commit directly to `main`, except the initial skeleton commit.
- **Commits.** Commit on your own: small, logical commits with Conventional Commits messages (`docs: …`, `chore: …`). Every commit carries a DCO `Signed-off-by` line.
- **Pull requests.**
  - Push feature branches and open PRs with the `gh` CLI.
  - Each PR description gives a short summary, the decisions it applies (link ADRs), and open items.
  - **Do not merge PRs.** I review and merge.
- **GitHub repositories.** Ask me before creating them or changing their visibility.
- **Parallel work.**
  - When the work can be parallelized, use subagents, each in its own **git worktree** on its own branch, opening its own PR.
  - Give each subagent explicit file ownership so branches don't conflict.
  - Shared files are owned by the main agent only; subagents propose changes to them in their PR description:
    - **`blokky-specs`:** `spec/glossary.md`, `schema/blokky.schema.json`, `spec/errors.md`, `adr/README.md`.
    - **`blokky-cli`:** `docs/cli.md` (exit codes, command list), `docs/contracts/*`, `schema/*.json`, `docs/adr/README.md`.
  - Do not fan out before the language spec draft is merged (§8).
- **Rebasing.** After a PR merges, rebase the remaining branches onto `main` before continuing.

## 1. What Blokky is

Blokky is a small, declarative YAML block language and runner that wraps shell commands in common structured-programming blocks. Its users are mainly **coding agents** (Claude Code, Codex, etc.), and secondarily the humans who review what those agents run.

**The problem.** Agents constantly write ad-hoc shell for timeouts, retries, polling loops, waiting on CI runs, temp directories and cleanup. That shell is long, fragile and hard to review. Cleanup steps (`rm -rf "$tmp"`) trigger approval prompts. Compound commands defeat permission rules.

**The idea.**
- Control flow and resource lifetime move into a small, declarative layer.
- The leaf shell commands become simple, single-purpose and easy to verify.
- The runtime owns its artefacts. Removing them is a single `blokky clean <run-id>` (or a `--cleanup` flag) that only touches what that run created, so it needs no hand-written `rm -rf`.

### 1.1 Design principles

- **Small and declarative.** It is not a scripting language and not Ansible. If a feature needs a general expression language, it probably doesn't belong.
- **Easily verifiable.** A human or classifier should understand a plan's intent at a glance.
- **Leaves stay simple.** Strict mode can enforce that.
- **Deterministic, structured output.** Results come back as a per-node JSON report, sized for an agent's context. Execution *order* is deterministic: children start in declaration order (§3.1). Only completion order and timings are inherently non-deterministic.
- **Blokky does not sandbox and does not decide what is allowed.** Validation stays the responsibility of the agent tool runner. Blokky makes validation easy by exposing everything that will execute and by supporting an externally configured validator plugin.

### 1.2 Security posture (how to reason about security)

- **The baseline is "the same thing without Blokky":** what an agent could do with a plain shell tool call, i.e. a bash script.
  - **The goal:** using Blokky never makes things *less* safe than the equivalent shell, and it should make them *more* reviewable.
  - **Main benefits:** simpler leaves; structure that humans and agent classifiers can review; runtime-owned artefacts.
- **The threat model in one sentence:**
  - A user running an unreviewed, hostile plan is equivalent to running an unchecked script downloaded from the internet.
  - The realistic hostile author is an agent under prompt injection.
  - Blokky must **not amplify** what such an agent can do compared with a plain bash call. It must not grant capabilities, bypass the runner's permission system, or **hide** what will run.
- **Agents will keep a generic shell tool.** Blokky is an additional tool, not a replacement. Controls that only work if agents can *only* use Blokky are out of scope.
- **Resource exhaustion** (output floods, runaway parallelism, loops) is possible without Blokky too, and its impact is local. Bounds exist for agent ergonomics and predictable behaviour, not as security boundaries.
- **Hygiene vs controls.** Strict-mode lint, secret masking, display sanitization and the `cmd` exclusion are hygiene or language design. The runner's permission system, the hook and the validator are the controls. The docs must not present hygiene as enforcement.

### 1.3 Run artefacts and cleanup

- **Every run gets a unique, sortable run id** (e.g. a ULID). It is always reported (§3.2) and exported to leaves as `BLOKKY_RUN_ID`.
- **What a run leaves behind.** All artefacts of a run (report, event log, logs, capture files, tempdirs, stopped ephemeral containers) are registered under its run id in a per-user state directory.
- **Processes are always stopped by run end.** That covers leaves, their descendants and services (§3.8). This is independent of artefact cleanup.
- **Artefacts are kept by default.** `blokky run` does not delete them, so the agent (or the user) can inspect logs, temp files and containers after a failure.
- **Cleanup commands:**
  - **`blokky clean <run-id>`** removes all artefacts of one run.
  - **`blokky run --cleanup on-success|always`** tries to clean up before exiting.
  - **`blokky gc`** removes the artefacts of **all** runs except those still in progress.
- **The typical agent flow:** run the plan, read the report. On success, call `blokky clean <run-id>` (or use `--cleanup on-success`); on failure, inspect first.
- **Result files:** if an agent needs a file produced during a run, the plan copies or moves it to its destination itself. Capture files and tempdirs are runtime-owned and disappear with `clean`/`gc`.
- **No automatic sweep and no scheduled gc service.** Only the user (or an agent on the user's request) decides when to clean.
- **"In progress" is determined by an advisory lock** (`flock` / `LockFileEx`) that the run holds on its registry entry for its lifetime, plus boot id and hostname. This works across PID namespaces and process restarts.
- **Deletion rules for `clean` and `gc`:**
  - same safety rules as tempdir deletion (§3.8): fd-relative, no-follow, no filesystem crossing, never outside the registered roots;
  - the registry lives in the 0700 state dir, not inside artefacts, so artefacts can't forge it;
  - only the invoking user's own runs; never as root on another user's behalf;
  - containers are removed only by an explicit `clean`/`gc`, via the engine CLI, listed as derived commands.
- **Best-effort.** A 100% cleanup guarantee isn't possible (a SIGKILLed run can't clean up). `clean`/`gc` handle leftovers whenever the user decides.

### 1.4 Platform depth

.NET is the implementation platform, but Blokky is **not limited to the built-in BCL APIs** (e.g. `System.Diagnostics.Process`). Where the BCL can't deliver the required semantics, Blokky reaches into the platforms directly with AOT-safe `LibraryImport` P/Invoke: `posix_spawn`/`setsid`, Job Objects, signals, console control events.

### 1.5 Evolution until v1.0.0

- **The spec is a draft until 1.0.0.** It is released as `1.0.0-draft.1`, `-draft.2`, …; many decisions can only be confirmed once the runtime exists, so the draft is refined during the runtime implementation and validation. Syntax, semantics and every decision in this brief may be revisited until then.
- **The CLI is versioned on its own** (0.x, incremental) and implements the language step by step.
- Decisions marked "baseline" below are the current direction. Ask me before changing them, but they are not frozen.
- **Accepted review findings are not final** either. When one turns out to fit poorly, raise it as an open question instead of forcing it in.

### 1.6 How the spec is built

1. **Complete language spec first, as a draft** (in `blokky-specs`), with **all planned features** (containers, `become`, services, locks, `race`, `matrix`, …), each specified to the same depth.
   - **Why:** the language stays coherent and idiomatic, and features are judged together.
   - **No milestone tags in the spec:** every feature belongs to 1.0. Which features a CLI release supports is the CLI's business (its feature matrix and roadmap).
   - The `v0.1`/`later` tags used elsewhere in this brief are historical; they are input for the CLI planning session.
2. **Then a user-facing example suite** covering every feature, then a **review** (by me and an agent council) that also asks whether each feature earns its place.
3. **Then the conformance suite, the runtime planning session, the spikes and an incremental CLI;** the spec is refined along the way and becomes 1.0.0 once the runtime has validated it.

Some questions are better left open than decided early; record them in §4 instead of inventing an answer.

## 2. Baseline decisions (current direction; ask before changing)

- **Name:** Blokky. Binary: `blokky`.
- **Runtime:** .NET 11, **Native AOT is a hard requirement.**
  - AOT/trim warnings are build errors.
  - Integration and conformance tests run against the published AOT binary.
  - Platform APIs via P/Invoke where needed (§1.4).
- **Plan format:** YAML, with strict surface rules (§3.0).
  - Parse by walking the YAML node tree (YamlDotNet representation or event parser). No reflection-based POCO deserialization.
  - Every scalar is read as a string. No YAML 1.1 implicit typing.
  - Keep line and column on every AST node.
- **Plan source:**
  - **stdin via heredoc** is the **convention for agents**: `blokky run <<'BLOKKY_PLAN_EOF' … BLOKKY_PLAN_EOF`.
    - The delimiter MUST be quoted, so the outer shell doesn't expand `$VARS` inside the plan.
    - The delimiter is collision-resistant on purpose.
    - Enforcing the exact form is `hook check`'s job (§3.9), because heredoc smuggling happens before Blokky starts. `blokky run` only warns when a plan contains a delimiter-like line.
  - A **file path** (`blokky run plan.blokky.yaml`) is also supported, for tests, reusable plans, humans and the root handoff pattern (§3.6). Use it with `--expect-sha256` when review and run are separate steps.
  - **Plan inputs** (§3.3) parameterize a plan, mainly reusable file plans: `blokky run --input BRANCH=feature/x plan.blokky.yaml`. Values come only from `--input` (CLI) or an `inputs` object (MCP), never from stdin, env or config.
- **Configuration:** hierarchical JSON config files with **two key trust classes** (§3.4).
- **MCP:** a planned second host (§3.9).
- **Architecture:**
  - A `Blokky.Core` library (parser, AST, linter, executor, config, result model) with thin hosts (CLI now, MCP later).
  - An `IProcessLauncher` boundary isolates platform process control (§3.8).
  - A **single invocation builder** renders exactly what `leaves`/`blokky plan` show and what is executed, from the bound plan inputs.
  - **`exec` is the primitive.** A `script` leaf compiles to `exec: [<shell>, <flags>, <scriptfile>]`; containers and `become` wrap that one shape. The `blokky __exec` trampoline may become the universal launcher inside those boundaries (spike, §8).
- **JSON contracts:** the result report, the event log, `leaves` output, the validator protocol and config each use source-generated `System.Text.Json` contexts and carry their own `schema_version`. The language-level contracts (report, events, leaves, validator protocol) are specified in `blokky-specs`.
- **Validation:**
  - Blokky exposes everything that will execute (§3.9) and supports an optional validator plugin.
  - The validator is configured only in user-level config. It is never a CLI argument or env var the agent could omit or change.
- **Flags and env vars cannot set user-only keys or relax lint/strict severities.** This is a design rule, not a security control (the hook pins the command line).
  - Flags come in three classes: tightening (`--max-duration`, `--expect-sha256`), neutral (`-v`, `--cleanup`) and data (`--input`, §3.3).
  - `--input` only binds `{ var }` sites. It never reaches literal fields, user-only keys or lint settings, so it can't loosen anything.
- **CLI parsing:** one spelling per option, no short aliases for data flags, **no response files (`@file`)**, no option bundling, no alternative value delimiters (`--opt=v`, `--opt:v`). `hook check` and `blokky run` share one argv parser.
- **Strict mode:** configurable lint rules that push logic into blocks. Examples: no `sleep`/`until`/`while` loops in leaves, no `trap`, quoted `"$X"` in POSIX shells. It does **not** blanket-ban commands like `rm`. Strict mode is hygiene.
- **Variables:**
  - Plan variables reach commands **only through explicit mapping**, never implicitly:
    - `env:` maps a value to a chosen env name, e.g. `env: { RUN: { var: RUN_ID } }`;
    - `export: [RUN_ID, PR]` exports plan variables under their own names.
    - In `exec` arrays, a whole element can be `{ var: X }`.
    - Plan variables come from plan inputs (§3.3), captures, `for` items and `tempdir.as`.
    - This mirrors Azure Pipelines' `env:` mapping: a command sees exactly what is listed, and `leaves` shows it.
  - Never textual substitution into command text. Leaf text stays static, so permission matching stays valid.
  - Per-shell injection properties are documented in `shells.md` (e.g. unquoted `$X` in POSIX shells allows argument injection). The docs make no absolute "prevents injection" claim.
- **Process control:**
  - Leaves run in their own session (`setsid`) on Unix, or their own Job Object on Windows.
  - Timeouts and cancellation kill the **whole process tree**, graceful-then-forced, with a configurable grace period.
  - All descendants die no later than run end, **including when Blokky itself is SIGKILLed** (lifeline, §3.8).
  - Best-effort against processes that deliberately escape (double-fork daemons, Windows breakaway); documented.
- **Leaves:** two kinds — `exec` (a program with arguments, no shell) and `script` (shell text run by a shell). **Shells:** `bash`, `sh`, `pwsh`, `powershell`. `zsh` and `fish` come later. **`cmd` is not supported** (§3.5).
- **Every node has an optional `name`** (a display template; §3.1).
- **Distribution v0.x:** GitHub Releases with per-RID AOT binaries, SHA-256 checksums, artifact attestations and an SBOM attestation; **unsigned**. Code signing and packaging (winget, Homebrew, dotnet tool, apt/rpm) come later.
- **Target platforms:** linux-x64, linux-arm64, osx-arm64, win-x64. WSL counts as Linux.
- **Open source on GitHub.** License: **Apache-2.0 with DCO sign-off** (no CLA), in both repos.
- **Plan file extension:** `*.blokky.yaml` (also `*.blokky.yml`).

## 3. Language model (draft; the spec refines it)

### 3.0 Syntax surface and document structure

**YAML surface rules.** These are normative; each violation is an error with a code and line/column.

- **Rejected:**
  - anchors, aliases and `<<` merge keys;
  - explicit tags;
  - multi-document streams;
  - **duplicate keys**;
  - **unknown keys** — reported with a did-you-mean suggestion based on edit distance **and a synonym table** for habits from other tools (`register` → `capture`, `loop`/`with_items` → `for: { each }`, `retries` → `retry: true` + `max`, `steps`/`tasks` → `do`, `continue-on-error` → `ignore_errors`, `timeout-minutes` → `timeout`, `working-directory` → `cwd`);
  - an unsupported plan version;
  - empty values for required keys;
  - empty lists.
- **Booleans** are exactly `true` / `false`.
- **Character lint:** one pass over the **raw decoded text, comments and keys included**, before parsing.
  - Allowlist: TAB, LF, and CR only as part of CRLF.
  - Bidi overrides, zero-width and other control characters are errors ("Trojan Source" attacks often hide in comments).
- **Size caps:** plan size and nesting depth are capped before the AST is built.
- **Plain-scalar pitfalls** must be documented, with targeted errors and warnings:
  - ` #` starts a comment, so `exec: echo done # tidy` is truncated (warn when a comment follows a command on the same line);
  - `script: [ -f x ]` becomes a list (targeted error); `exec: [ -f x ]` is a valid argv array for `[` (lint suggests `path:`);
  - a leading `"` changes parsing;
  - `"^\d+$"` is an invalid YAML escape;
  - flow-mapping probes containing `,` `[` `]` `{` `}` break.
  - The agent guide teaches: plain scalars only for simple `exec` strings; **`script:` text always as a `|`/`|-` block scalar** (lint warns otherwise); regexes always single-quoted; block mappings for non-trivial probes.
- **Errors carry a corrected snippet.** Each targeted error includes a minimal fixed YAML fragment (e.g. `retry: 3` → `retry: true` + `max: 3`). Agents fix errors most reliably by copying.
- **Targeted errors:**
  - a mapping or single node where a body list is expected ("prefix with `- `");
  - a string-valued condition, e.g. `if: "$X == 1"` ("conditions are mappings, e.g. `{ var: X, equals: '1' }`");
  - GitHub-Actions-style `if:` next to another kind ("use `when:`");
  - `${` in a literal non-display field ("use `{ var: X }`, nest `cwd`, or `"$X"` in a shell string");
  - plan-input mistakes (§3.3).

**Root document.**

- The root MUST be a single node mapping. A bare top-level list is rejected with a hint.
- **Plan-only keys**, allowed only at the root:
  - `blokky:` — the spec version the plan targets;
  - `inputs:` — plan inputs (§3.3);
  - `outputs:`;
  - `defaults:` — a **closed set**: `shell`, `leaf_timeout`, `become` (later).
- `defaults.leaf_timeout` applies to `exec`/`script` nodes and probes only, never to groups, so it can't break sequences.

**Value references and interpolation (per-field).**

- **`${X}` interpolation** only in display strings: `name`, `log` / `fail` / `assert` messages.
  - `$${` escapes it.
  - An undefined variable is a runtime error and a static warning.
  - Non-scalar values render as compact JSON; a `capture_file` variable renders its path.
  - Resolves **Blokky variables only, never process env**.
- **Whole-value references** where a value affects execution:
  - `{ var: X }` in `exec` array elements, `env` values, `cwd`, `path:` subjects;
  - `{ from_env: NAME }` in `env` values;
  - `stdin: { var: X }` (content) or `stdin: { file: { var: X } }` (file content).
  - Any reference mapping may carry `secret: true`.
  - No string concatenation. Join paths by nesting relative `cwd`: a relative child `cwd` resolves against the parent's cwd.
- **Every other field is literal.** Executed text (`exec` strings, `script` text, probes) never interpolates. Values reach commands only via `env:`/`export:` (as environment variables) or as `{ var: X }` elements of `exec` arrays.
- **`syntax.md` publishes a per-field table** (literal / `${}` / reference), covering **all** fields including later ones: lock name, image, mounts, service fields, `become` user, `collect`, `matrix` values, `count`, `sleep`, `concurrency`, `glob`, and the `inputs.*` keys (`default`, `description`, `in`, `matches` are all literal).

**Single source of truth.**

- The JSON Schema is normative for structure; `reference.md` is normative for semantics.
- Field tables in the reference are generated from the schema or checked against it in CI.
- Rules JSON Schema can't express are listed explicitly in the reference, e.g.:
  - exactly one kind per node;
  - at most one repetition modifier;
  - the kind × modifier validity;
  - modifiers outside repetition not referencing the node's own variables;
  - input shadowing, and inputs never reaching literal fields (§3.3).

### 3.1 Nodes, modifiers, conditions

Ansible-style: a plan is a tree of **nodes**.

- Each node has **exactly one kind**, the thing it executes.
- Any node can carry **modifiers**: attributes that scope, repeat, limit or label it.
- A "block" such as a timeout block or a tempdir block is just a node whose kind is `do` and which carries modifiers.
- **Companion fields** belong to their kind (`then`/`else`, `message`, `code`, `level`, `concurrency`, `fail_fast`, run options).
- **Body fields** (`do`, `then`, `else`, `finally`, `all`, `any`, `race`) accept lists only.

**Milestone tags:** **v0.1** = first release; **later** = specified now, reserved, scheduled in the specs `ROADMAP.md`.

**Deterministic start order.** In `all`, `any` and parallel `for`:
- Children (iterations) **start in declaration (item) order**. With `concurrency: N`, the next child starts as soon as a slot frees, still in declaration order.
- `race` starts all children immediately, in declaration order.
- The report lists children in plan order; only completion order and timings vary.

**Node kinds:**

| Kind | Milestone | Purpose |
|---|---|---|
| `exec` | v0.1 | Leaf **without a shell**. The value is an **argv array** (elements are literals or `{ var: X }`) or a **string** that Blokky splits into argv (§3.5). |
| `script` | v0.1 | Leaf **run by a shell**. The value is shell text, written to a temp file in the run dir and passed to the shell as a file (§3.5). Sibling `shell:` picks the shell. Lint warns unless the text is a `\|`/`\|-` block scalar. |
| `do` | v0.1 | Sequence: run children in order. `fail_fast: false` runs the remaining children after a failure and fails at the end. |
| `all` | v0.1 | Parallel: all children must succeed. Companions: `concurrency` (default: unbounded, subject to the default cap in Q-r), `fail_fast` (default `true`: a failing child cancels the others). |
| `any` | v0.1 | First success wins: succeeds when one child succeeds (cancelling the rest), fails only if all fail. **Default `concurrency: 1`**, i.e. an ordered fallback ("try A, else B, else C"). `concurrency: N` races N alternatives at a time. Error aggregation when all fail: §4 Q-r. |
| `race` | later | First *completion* decides, success or failure. All children start at once; `concurrency` is not allowed. |
| `if` | v0.1 | Two-way branch: `if: <condition>`, `then: [...]`, optional `else: [...]`. Else-if nests in `else`. An `if` without `else` is allowed; lint suggests `when`. |
| `sleep` | v0.1 | Explicit delay. |
| `log` | v0.1 | `log: "<message>"` with sibling `level:` (`debug`, `info`, `warn`). No process, no shell. Goes to the report and event log; on stderr only per the stderr rules (§3.2). Replaces `echo` leaves. |
| `fail` | v0.1 | `fail: "<message>"` with optional sibling `code:` (default `BK_FAIL`). |
| `assert` | v0.1 | `assert: <condition>` with sibling `message:` and optional `code:` (default `BK_ASSERT`). The canonical form for "fail unless X". Whether a condition error inside `assert` maps to its `code` is open (Q-n). |

**Leaf options** (node-level siblings of `exec`/`script`):
- `stdin`, `ok_exit`, `stderr: merge`, `encoding`, `max_log_bytes`, `keep_descendants`, `env`, `export`;
- `shell` (`script` only).

A mapping given as the value of `exec:`/`script:` gets a targeted error ("move `timeout` to the node level").
- **`ok_exit: [1, …]`** adds exit codes that count as success. Exit 0 always succeeds.
- **`keep_descendants: true`** opts out of killing the leaf's remaining process group/job at leaf completion (§3.8). It is visible in `leaves`.

**Modifiers (any node unless noted):**

| Modifier | Milestone | Purpose |
|---|---|---|
| `name` | v0.1 | Display label (interpolation template). The node-level entry shows the raw template; per-iteration/attempt entries are interpolated. |
| `when` | v0.1 | Guard: run the node only if the condition holds, else `skipped`. Evaluated **once, in the parent's scope**. |
| `timeout` | v0.1 | **Total** wall-clock limit for the node. Covers lock wait, tempdir creation, container start, service startup, body and repetition. Kills the process tree. |
| `lock` | later | `lock: NAME` or `lock: { name, wait }` (§3.8). |
| `tempdir` | v0.1 | Runtime-owned temp dir that **always becomes the node's cwd**: `tempdir: true` or `tempdir: { as: WORK, prefix: build }`. A relative `cwd` on the same node resolves inside it; an absolute `cwd` with `tempdir` is an error. Created **once per node** (nest `retry` inside if attempts need a fresh dir). Kept until `clean`/`gc`. |
| `cwd` | v0.1 | Working directory: a literal path or `{ var: X }`. |
| `env` | v0.1 | Scoped environment variables. Values: literal, `{ var: X }` or `{ from_env: NAME }`, each optionally `secret: true`. Some keys are forbidden (§3.9 A3). |
| `export` | v0.1 | `export: [RUN_ID, PR]`: exports the listed plan variables as env vars of the same name, scoped like `env`. A name both exported and set in `env:` on the same node is an error. |
| `container` | later | `container: { image, mounts, user, env_pass }` (ephemeral) or `container: { exec: NAME }` (existing) (§3.6). |
| `become` | later | `become: USER` or `become: { user }` (§3.6). |
| `services` | later | `services: [ { name, exec \| script, shell, env, export, cwd, ready: <condition>, ready_timeout } ]`. Background processes started before the node and stopped (whole tree) after it. **`ready:` is a condition** polled until it holds, using the service-scoped subjects `{ port: 8080, open: true }` (loopback only) and `{ output: stdout, contains: … }`, plus `probe`. No built-in HTTP checks. |
| `finally` | v0.1 | Cleanup nodes for this node, run **once per node** (not per iteration). They run after success, failure, explicit `fail`, timeouts and cancellation, always under their own `finally_timeout`, even after `timeout` fired. Whether `finally` nodes may capture is open (Q-s). |
| `ignore_errors` | v0.1 | `ignore_errors: true`: the node's failure doesn't fail its parent. The report shows `failed` with `ignored: true`. It never swallows outer cancellation or budget kills. A node whose failure is ignored binds no captures; lint suggests `ok_exit` for `capture` + `ignore_errors`. |
| `retry` | v0.1 | Repetition: `retry: true` (retry on any failure) or `retry: <condition>` (retry only when it holds, e.g. `{ result: error, in: [BK_TIMEOUT_ATTEMPT] }`). |
| `while` | v0.1 | Repetition: condition checked **before** each iteration. |
| `until` | v0.1 | Repetition: condition checked **after** each iteration. Polling is `until` with a `result:` condition. |
| `for` | v0.1 / later | Repetition over items. All `for` options live **inside** the `for:` mapping (see `for`). |
| `attempt_timeout` | v0.1 | Per-attempt/iteration limit. Valid only with a repetition modifier. |
| `capture` / `capture_file` | v0.1 | **`exec`/`script` only.** See Variables. |

**Repetition rules:**

- **At most one repetition modifier per node.** For combinations, nest nodes.
- **Siblings of `retry`/`while`/`until`:**
  - `max` — **total attempts/iterations**, the same meaning for all three;
  - `every`;
  - `attempt_timeout`;
  - `on_body_failure` (`while`/`until` only);
  - `retry` also has `backoff` and `jitter`.
- **Loops must be bounded.** `while`/`until` require `max` and/or `timeout`. An unbounded loop is a parse error.
- **`timeout` vs `attempt_timeout`.**
  - When `timeout` hits, the current attempt's process tree is killed and the node fails with `BK_TIMEOUT_TOTAL`.
  - An `attempt_timeout` produces `BK_TIMEOUT_ATTEMPT`. For `retry` it is a failure that is retried. For `while`/`until` it is a body failure governed by `on_body_failure`.
- **`on_body_failure: stop | continue`** applies to **`while`/`until` only** and means "a body failure = not done yet".
  - Defaults are by modifier: `until` → `continue`, `while` → `stop`.
  - It is invalid on `retry` and on `any`.
- **`fail_fast: true | false`** (default `true`) is the separate "keep going, fail at the end" switch for `do`, `all` and `for`.
- **How repetition ends.**
  - An `until` loop succeeds when its condition holds.
  - Failure codes: `BK_RETRIES_EXHAUSTED`, `BK_LOOP_MAX_REACHED`, `BK_TIMEOUT_TOTAL`. Each nests the last attempt's error.
  - Explicit `fail`/`assert` failures are retried only if the `retry` condition matches their code.
- **Must be specified in the reference (D15):**
  - `every` semantics (delay after the end vs period from the start), and whether there is a wait before the first iteration;
  - backoff kinds, cap and jitter defaults;
  - whether a condition is evaluated after an attempt killed by `attempt_timeout`;
  - whether probe durations count toward `attempt_timeout`;
  - zero-iteration `while`;
  - a retry-by-default table: run failure, attempt timeout, condition error, `fail`/`assert`.
- **Report:** iteration count, which bound ended the loop, the last condition result, and `iterations[]` (interpolated name, index, item).

**`for` (options inside the mapping):**

```yaml
for: { glob: "src/**/*.csproj", as: PROJ, concurrency: 4, fail_fast: true }
```

| Source | Milestone | Use case |
|---|---|---|
| `each: [a, b, c]` | v0.1 | Static list. |
| `count: N` | v0.1 | Fixed number of iterations (item value and `_INDEX` base specified in the reference). |
| `glob: "…"` | v0.1 | Files, expanded by **Blokky**. Rules: <ul><li>ordinal sort; no dotfiles unless the pattern names them;</li><li>no symlink following;</li><li>`/` separators in item values;</li><li>no `..` escaping the cwd subtree;</li><li>zero matches is an error unless `allow_empty: true`.</li></ul> Host paths (§3.6). |
| `items: { var: X, json: .path }` | later | Items from a JSON array in a captured variable. Mandatory `max_items`; items recorded in the report. No config gate. |
| `lines: { var: X }` | later | Items from the lines of a captured variable. Same rules. |
| `matrix: { OS: [..], ARCH: [..] }` | later | Cartesian product. Binds each key as a variable; `as` is forbidden; the index is `MATRIX_INDEX`; the report `item` is an object. |

**`for` options:**
- `as:` is **required** for scalar sources and names the item variable; `<AS>_INDEX` holds the index.
- `concurrency: N` makes it a fan-out, starting iterations in item order.
- `fail_fast` as above.

**Argument-injection hygiene for item values and plan inputs:** document it, recommend `--` before such arguments, and add a lint hint.

**Variables.**

- **Names.**
  - Pattern `^[A-Z][A-Z0-9_]*$`. Derived names (`<AS>_INDEX`, `MATRIX_INDEX`) count as declarations for collision checks. The `BLOKKY_` prefix is reserved.
  - **Plan variables and environment variables are separate namespaces.** Nothing is exported implicitly; only `env:`/`export:` create env vars for a command.
  - **`export:` names** must not be reserved environment names: `PATH`, `HOME`, `USER`, `SHELL`, `IFS`, `TMP*`/`TEMP`, `PWD`, `LD_*`, `DYLD_*`, `BASH_ENV`, `ENV`, `PS*`, `PATHEXT`, `COMSPEC`, `SystemRoot`, `BLOKKY_*`, and the execution-influencing list (§3.9 A3).
    - Setting such a name deliberately requires an explicit `env:` entry, which is visible in `rendered` and flagged (`exec_env_risk`).
    - Some `env:` keys are forbidden outright (§3.9 A3).
  - Windows env names are case-insensitive; env-name collisions are checked case-insensitively.
- **Capture forms (on `exec`/`script` nodes):**
  - `capture: NAME` binds trimmed stdout.
  - `capture: { NUM: .number, STATE: .state }` binds several JSON fields (same `json:` path subset as conditions). Non-scalar values become compact JSON text. A missing path fails the node with a distinct code.
  - `capture_file: NAME` writes stdout to a runtime-owned file in the run dir and binds its **path**. Use it for large outputs: env vars have OS size limits (≈128 KiB per string on Linux, 32K chars on Windows).
    - The file stays until `clean`/`gc`. A plan that needs it afterwards copies or moves it.
    - It can appear in `outputs` (as a path).
  - `secret: true` is a node-level sibling that marks all captures of that node as secret.
  - A `capture` over `max_capture_bytes` is an error that suggests `capture_file`; never silent truncation. A NUL byte is an error.
- **Binding and scope.**
  - **Iteration-local binding:** a capture is visible to *its own iteration's* condition (`until`, `while`, `retry` condition) even if that iteration failed. It is *published* to the enclosing scope only on success.
  - **A capture on the repeating node itself** (`retry`, `while`, `until`) publishes from the terminating iteration.
  - **Captures in nested body nodes of loops are loop-scoped.** `for` iterations never publish; their captures leave the loop only through `collect` (later; `collect: { X: XS }` on the `for` node, ordered by item index, failed or skipped iterations → `null`).
  - **The first `while` check** sees only the outer scope. Lint flags `while` conditions that read body captures without `defined`.
  - **In `do`,** a capture is visible to later siblings and after the `do`.
  - **A capture inside a conditional branch** is "possibly undefined" (static warning).
  - **In `any`,** only the winning child's captures bind, and branches MAY reuse a name (never the name of a plan input).
  - **Duplicate capture names across `all` children** are an error.
  - **`finally`** sees whatever the body bound.
  - **Shadowing** is an error.
- **Modifiers outside repetition** (`when`, `timeout`, `lock`, `tempdir`, `cwd`, `env`, `container`, `become`, `services`) **must not reference variables the node itself introduces** (`as`, `tempdir.as`, its own captures). This is a static error: "move it into the body".
- **Plan inputs** are plan variables in the **plan scope** that encloses the root node (§3.3).
- **Plan outputs.**
  - `outputs: [RUN_ID, CONCLUSION]` at the root returns final values in the report. Inputs may be listed too.
  - An undefined output reports `null`.
  - A secret output reports `{ "secret": true }`, never a masked value.
  - A loop-scoped output is a static error unless collected.

**Secrets.**

- **Sources.** Secrets come **only by reference** (`{ from_env: GH_TOKEN, secret: true }`, `{ var: X, secret: true }`, captures with `secret: true`). A literal value marked secret is a parse error. **There are no secret plan inputs** (§3.3).
- **Masking** applies to runtime sinks: stderr lines, report tails, log files (streaming, with a chunk-overlap window), events, interpolated display strings, the validator request. Values shorter than a minimum length are not masked; Blokky warns instead.
- **`blokky plan` and `leaves` never resolve secret references.** They show `TOKEN ← env GH_TOKEN (secret)`.
- **Limits.** Masking does not follow derived or transformed values. The `env:` condition subject reports only the condition result, but operators like `matches`/`gt` form an oracle, so this is hygiene, not secrecy. The threat model says so.

**Conditions (one reusable construct).** Used by `if`, `when`, `assert`, `while`, `until`, `retry` and `ready`.

Shape: **one subject key plus one operator key**, with optional `json:` and `ignore_case:`. Composites are `all` / `any` / `not`. The only operator-less form is the bare probe (truthy by exit code).

```yaml
{ var: STATUS, in: [queued, in_progress] }
{ var: PR, json: .mergeable, equals: MERGEABLE }
{ var: RUN_ID, not_empty: true }
{ env: CI, not_equals: "true" }
{ path: node_modules, not_exists: true }
{ host: os, equals: windows }
{ result: stdout, matches: '^\d+$' }                  # the node's own result (exec/script nodes only)
{ result: error, in: [BK_TIMEOUT_ATTEMPT] }           # retry conditions
{ probe: { exec: [docker, info] } }                    # bare probe: true if exit 0
{ probe: { exec: queue-cli depth }, gt: 0 }            # operator applies to the probe's trimmed stdout
{ probe: { exec: git diff --quiet HEAD~1 -- package-lock.json, true_exit: [1], false_exit: [0] } }
{ probe: { script: "(Get-Service sshd).Status", shell: pwsh }, equals: Running }
{ all: [ … ] }  { any: [ … ] }  { not: … }
```

**Subjects.** All are evaluated by Blokky itself, spawning no process, except `probe`.

| Subject | Tests |
|---|---|
| `var: NAME` | A Blokky variable. |
| `env: NAME` | The **effective** environment of the scope where it is evaluated. |
| `path: P` | The **host** filesystem, relative to the node's cwd. |
| `host: os \| arch` | os ∈ {`linux`, `macos`, `windows`} (WSL = `linux`); arch ∈ {`x64`, `arm64`}. **Static**: statically false `host:` branches are pruned before the shell/OS check and in `leaves`. |
| `result: stdout \| stderr \| exit \| error` | The node's own result (`error` = error code). Valid in `until` and `retry`; `stdout`/`stderr`/`exit` only when the node's kind is `exec` or `script`. |
| `probe: { exec \| script, … }` | A probe. A mapping with exactly one of `exec:` (same values as the `exec` kind) or `script:` (with optional `shell:`). **Closed key list inside the mapping:** `exec`/`script`, `shell` (script only), `env`, `export`, `timeout`, `true_exit`, `false_exit`, `timeout_result`. Probes also inherit the scope's `env`/`export`. |
| `port: N` / `output: stdout\|stderr` | **`ready:` conditions of `services` only** (later). |

**Operators.**

| Operator | Negated form | Notes |
|---|---|---|
| `equals` | `not_equals` | Trailing `\r?\n` trimmed. |
| `in: [..]` | `not_in` | |
| `contains` | `not_contains` | Plain substring. |
| `matches` | `not_matches` | `RegexOptions.NonBacktracking`; exact semantics in Q3. |
| `empty` | `not_empty` | Unary. Behaviour on undefined variables: Q-m. |
| `defined` | `not_defined` | Unary. |
| `exists`, `is_file`, `is_dir` | `not_exists` | Unary, `path` only. |
| `open` | — | Unary, `port` only. |
| `gt` / `ge` / `lt` / `le` | (complementary) | Numeric. A non-numeric value is a **condition error**. |

**Operator rules.**

- Unary operators take the value `true` only.
- Single negation uses the `not_*` form. `not: {…}` around a simple condition that has a `not_*` form is a **parse error** with a fix-it.
- `json: .path` extracts a field before comparing (tiny dot/index subset). A missing path is a condition error unless the operator is `defined`/`not_defined`.
- `ignore_case: true` applies to the string operators.
- Referencing an undefined variable is a condition error, except with `defined`/`not_defined` (and see Q-m).

**Composites.**

- Condition `all`/`any` take lists and **short-circuit sequentially**. They mirror node `all`/`any` in truth semantics but not in execution, and the docs say so.
- A condition mapping in a body list, or a node in a condition position, gets a targeted error.
- Probes SHOULD be side-effect-free.

**Condition errors.** These make the condition neither true nor false; the owning node fails with a distinct code:
- a non-numeric value for a numeric operator;
- a probe exit outside `true_exit`/`false_exit` (with only one list given, the other side is its complement);
- a probe timeout (unless `timeout_result: false|true`);
- probe output over the limit;
- an undefined variable;
- a bad JSON path.

**Probes.**

- **Exit-code semantics.** By default, exit 0 means true and anything else false.
- **Probes are full leaves for process control** (session, tree kill). They are killed when their own timeout expires, when an enclosing `timeout` hits, or when the surrounding node is cancelled. Short-circuiting means later probes never start.
- **Limits.** `timeout:` per probe, with a separate, shorter `probe_timeout` default. A small stdout capture limit applies.
- **Scope.**
  - Probes in `if`, `while` and `until` run in the node's own scope (`cwd`, `env`, `container`, `become`).
  - `when` is evaluated once in the **parent's** scope, so a skip never creates a tempdir, starts a container or takes a lock. Lint flags a `when` that uses relative paths or probes on a node setting `cwd`, `container` or `become`.
- **Startup cost.** Process start is cheap; shell startup is the cost (pwsh roughly 0.3–1s, `sh`/`bash` a few ms). `probe: { exec }` is the fast path. Document it; lint suggests `path:`/`var:` subjects or `exec` probes for tight polling.
- **Visibility.** Probes appear in `leaves` as `probe` units. The report records each executed probe.

**Out of scope for now:** semver comparison; HTTP checks; arithmetic, string manipulation, templating; an early-exit "end plan successfully" construct.

**Modifier application order, outermost to innermost (normative):**

`when` → `timeout` (total) → `lock` → `tempdir` → `cwd` → `env` → `container`/`become` → `services` → `finally` → repetition (`retry` | `while` | `until` | `for`) → `attempt_timeout` → node kind (incl. `capture`).

- `finally` runs under its own `finally_timeout`, even after `timeout` fired.
- `name` is a display template rendered per attempt/iteration.
- `ignore_errors` applies to the node's final outcome.
- Grace-period accounting against `timeout` is open (Q-k).

**Sketches** (draft; the spec decides). The CI example uses the background-run pattern (§3.2), because its 45-minute wait exceeds typical agent tool-call limits.

```yaml
blokky: 0.1
name: wait for CI on ${BRANCH}
cwd: ./repo
inputs:
  BRANCH:                                 # no default -> required, non-empty
    description: Branch whose CI run to wait for
    matches: '^[A-Za-z0-9._/][A-Za-z0-9._/-]*$'
outputs: [RUN_ID]
do:
  - name: find run id
    exec: [gh, run, list, -b, { var: BRANCH }, -L1, --json, databaseId, -q, '.[0].databaseId']
    capture: RUN_ID                       # on the repeating node: published from the terminating iteration
    until: { result: stdout, matches: '^\d+$' }
    every: 10s
    timeout: 2m
  - log: "Watching CI run ${RUN_ID}"
  - all:                                  # fail-fast: a failing child cancels the others
      - name: watch run
        exec: [gh, run, watch, { var: RUN_ID }, --exit-status]
        timeout: 45m                      # run with --max-duration in the harness's background mode
      - name: queued guard
        exec: [gh, run, view, { var: RUN_ID }, --json, status, -q, .status]
        until: { result: stdout, not_equals: queued }
        every: 15s
        timeout: 5m
```

Fragments (each would sit inside a `do:` list):

```yaml
- name: restore only if the lockfile changed
  if: { probe: { exec: git diff --quiet HEAD~1 -- package-lock.json, true_exit: [1], false_exit: [0] } }
  then:
    - exec: npm ci
  else:
    - log: dependencies unchanged

- exec: npm ci
  when: { path: node_modules, not_exists: true }

- assert: { var: RUN_ID, matches: '^\d+$' }
  message: "No CI run found for branch ${BRANCH}"
  code: CI_RUN_NOT_FOUND

- name: build ${PROJ}
  for: { glob: "src/**/*.csproj", as: PROJ, concurrency: 4 }
  exec: [dotnet, build, { var: PROJ }, -c, Release]

- name: download from the first mirror that works   # any = ordered fallback by default
  any:
    - exec: curl -fsSLo pkg.tgz https://mirror-a.example/pkg.tgz
    - exec: curl -fsSLo pkg.tgz https://mirror-b.example/pkg.tgz

- name: drain the queue                              # while-do
  while: { probe: { exec: queue-cli depth }, gt: 0 }
  every: 2s
  max: 500
  timeout: 8m
  do:
    - exec: queue-cli process-batch --size 50
      timeout: 2m

- name: test against the CI run                     # shell features -> script; variables only via export/env
  script: |
    set -o pipefail
    npm test 2>&1 | tee test.log
    echo "tested for CI run $RUN_ID"
  shell: bash
  export: [RUN_ID]
  env: { GH_TOKEN: { from_env: GH_TOKEN, secret: true } }
```

### 3.2 Output contract, report and run budget

**Channels.**

- **stdout:** the final JSON report, and nothing else.
- **stderr when not a TTY** (the agent-harness case):
  - **always** the report-path line, first;
  - warnings and above;
  - `log` events at `info`/`debug` go only to the report and event log.

  Under a harness, stderr also lands in the agent's context, so it stays minimal.
- **stderr on a TTY or with `-v`:** progress lines and `log` events.
- **Display sanitization.** In all human-readable output, C0/C1 control characters and ESC are escaped, so captured data can't inject ANSI/OSC sequences. JSON output is safe by construction.
- **Persistent files** in the run dir (per-user state dir, §3.8):
  - `report.json`, written by temp file + atomic rename;
  - `events.jsonl`, append-only, flushed per event; readers tolerate a truncated last line;
  - leaf logs.
- **`blokky report <run-id>` / `blokky report --last`** re-prints a run's report. The background-run pattern doesn't need to catch the stderr path.

**Report (`schema_version` versioned; normative in `blokky-specs/spec/output-contract.md`).**

- **Run-level fields:**
  - `run_id`;
  - `plan` (`sha256`, `spec_version`, `source`);
  - `inputs` (`NAME → { value, source }`, declaration order) and `inputs_sha256`;
  - `status` and `error`;
  - `outputs`;
  - `artifacts` (run dir, tempdirs, containers, `cleaned`);
  - `duration_ms`;
  - `truncation`.
- **Per node:**
  - stable path id, `name`, kind, source position;
  - status: `succeeded`, `failed`, `skipped`, `timed_out`, `cancelled`, plus `ignored`;
  - `error` (code, message, nested cause);
  - attempts/iterations;
  - `log` events;
  - output tails.
- **Leaves additionally carry** `exit_code` (uint32 on Windows), `signal` (Unix) and `killed_by` (`timeout`, `attempt_timeout`, `cancel`, `shutdown`, `budget`) as separate fields.
- **Timeout status model.** A node is `timed_out` iff **its own** `timeout`/`attempt_timeout` fired; its error code is always present; `killed_by` appears on leaves only.
- **Ordering.** Nodes appear in **plan order**; events carry monotonic sequence numbers. The docs list the inherently non-deterministic fields (completion times, durations, `any`/`race` winners).
- **Report byte budget** with deterministic truncation:
  - collapse succeeded subtrees first;
  - aggregate loop iterations (count, first/last N, failures);
  - cap per-node tails.

  The full report is always in `report.json`.

**Illustrative examples** (shapes, not final field names; the spec defines them).

stdout, successful run of the CI sketch:

```json
{
  "schema_version": "0.1",
  "run_id": "01JC9X4M7QK3Z8V2W5T6Y1R0PA",
  "plan": { "sha256": "9f2c41…", "spec_version": "0.1", "source": "stdin" },
  "inputs": { "BRANCH": { "value": "feature/x", "source": "cli" } },
  "inputs_sha256": "4be0d7…",
  "status": "succeeded",
  "duration_ms": 1214530,
  "outputs": { "RUN_ID": "12345678901" },
  "artifacts": {
    "run_dir": "/home/u/.local/state/blokky/runs/01JC9X4M7QK3Z8V2W5T6Y1R0PA",
    "tempdirs": [],
    "containers": [],
    "cleaned": false
  },
  "nodes": [
    { "id": "/", "name": "wait for CI on feature/x", "kind": "do", "pos": "2:1", "status": "succeeded", "children": [
      { "id": "/do/0", "name": "find run id", "kind": "exec", "pos": "11:5", "status": "succeeded",
        "iterations": { "count": 3, "ended_by": "condition", "failures": [] },
        "exit_code": 0, "stdout_tail": "12345678901" },
      { "id": "/do/1", "kind": "log", "status": "succeeded", "message": "Watching CI run 12345678901" },
      { "id": "/do/2", "kind": "all", "status": "succeeded", "children": [
        { "id": "/do/2/all/0", "name": "watch run", "kind": "exec", "status": "succeeded",
          "exit_code": 0, "stdout_tail": "✓ main CI · 12345678901 …" },
        { "id": "/do/2/all/1", "name": "queued guard", "kind": "exec", "status": "succeeded",
          "iterations": { "count": 2, "ended_by": "condition" }, "stdout_tail": "in_progress" }
      ]}
    ]}
  ],
  "truncation": null
}
```

stdout, failure before execution (pre-run envelope; no run id, no run dir, nothing to clean; every error listed at once). It covers parse, character-lint, sha-mismatch, config and input errors:

```json
{ "schema_version": "0.1", "status": "invalid",
  "errors": [ { "code": "BK_INPUT_MISSING", "input": "BRANCH",
                "message": "required input BRANCH is missing: blokky run --input BRANCH=<value> … wait-ci.blokky.yaml" } ] }
```

stdout, failure during execution: the run would end with the same envelope as success, plus for example:

```json
"status": "failed",
"error": { "code": "BK_TIMEOUT_TOTAL", "node": "/do/0", "message": "timeout 2m exceeded",
           "cause": { "code": "BK_LOOP_CONDITION_FALSE", "last_result": { "stdout": "" } } },
"outputs": { "RUN_ID": null }
```

stderr, not a TTY:

```
blokky: run 01JC9X4M7QK3Z8V2W5T6Y1R0PA report /home/u/.local/state/blokky/runs/01JC9X4M7QK3Z8V2W5T6Y1R0PA/report.json
blokky: warn /do/2/all/0 "watch run": output truncated to 64 KiB (full log in run dir)
```

`events.jsonl` excerpt:

```
{"seq":1,"t":"2026-11-04T01:02:03.120Z","ev":"run_start","run_id":"01JC9X4M…","plan_sha256":"9f2c41…","inputs":{"BRANCH":{"value":"feature/x","source":"cli"}},"inputs_sha256":"4be0d7…"}
{"seq":2,"t":"2026-11-04T01:02:03.131Z","ev":"node_start","node":"/do/0","name":"find run id"}
{"seq":3,"t":"2026-11-04T01:02:04.402Z","ev":"iteration_end","node":"/do/0","iteration":1,"exit_code":0,"condition":false}
…
{"seq":41,"t":"2026-11-04T01:22:17.650Z","ev":"run_end","status":"succeeded"}
```

**Leaf output.**
- Pipes are drained concurrently into bounded tail buffers plus the log file.
- `max_log_bytes` truncates and records the truncation.
- `stderr: merge` keeps interleaving when ordering matters.

**Run budget (harness alignment).**

- Default sources, each of which can only lower the budget: a **default run budget** below common harness limits (value: Q-f), config, `BLOKKY_MAX_DURATION`, `--max-duration`. Nested runs inherit the remaining budget.
- At the budget minus `cleanup_reserve`, Blokky cancels everything, runs `finally`, writes the report and exits with a distinct code.
- `cleanup_reserve` is the **single cleanup deadline** for budget, cancel and signal shutdown (§3.8).
- `hook check` reads the tool call's `timeout` and requires or injects `--max-duration` ≤ timeout − margin. Background mode is exempt.
- Lint warns when a `timeout` exceeds the budget, and when a repeating node has `timeout` < `max × every` without `attempt_timeout`.
- **The agent guide documents the background-run pattern:** start the run in the harness's background mode with an explicit `--max-duration`, then `blokky report <run-id>` afterwards.

**Normalized plan view.** `blokky plan` prints the desugared plan with every effective default filled in: start order, `concurrency`, `fail_fast`, `on_body_failure`, `max`, total vs per-attempt timeouts, resolved shell and binary, variable scopes, pruned `host:` branches, `TOKEN ← env GH_TOKEN (secret)` references, and inputs with their source and constraint (`BRANCH ← input (cli) = 'feature/x'`, `ENVIRONMENT ← input (default) = 'staging' ∈ {staging, production}`). Hidden defaults become visible.

**Encoding.**
- **Plan:** UTF-8, with or without BOM. A UTF-16 BOM is detected (e.g. from PowerShell `Out-File`/`>` redirection). Invalid UTF-8 is rejected with its position. Input mangled to `?` (PowerShell 5.1 pipes non-ASCII as ASCII) gets a lint hint.
- **Leaf output:** decoded as UTF-8 with U+FFFD replacement in the report; raw bytes in logs; optional per-leaf `encoding:`.
- **Report:** UTF-8 without BOM, written to the raw stdout stream.

**Durations and clocks.**
- Duration grammar: `500ms`, `10s`, `2m`, `1h30m`. No bare numbers.
- Timeouts use a monotonic clock.
- Jitter uses a seedable RNG.
- The executor's clock is .NET `TimeProvider` (also the virtual clock in tests).

**Codes.**
- User `code` values match `^[A-Z][A-Z0-9_]*$`.
- **Built-in codes use the reserved `BK_` prefix**; user codes starting with `BK_` are rejected.
- Input codes: `BK_INPUT_SYNTAX`, `BK_INPUT_UNKNOWN`, `BK_INPUT_DUPLICATE`, `BK_INPUT_MISSING`, `BK_INPUT_EMPTY`, `BK_INPUT_INVALID`, `BK_INPUT_TOO_LARGE` (§3.3).
- The catalogue lives in `blokky-specs/spec/errors.md`.

**Versioning.**
- The plan's `blokky:` field names the spec version it targets (scheme: Q13).
- Unknown keys are errors, so an older binary fails clearly on a newer plan.
- Each JSON contract carries its own `schema_version`; consumers ignore unknown fields.
- The tool and spec stay 0.x until the plan format is frozen.
- Schema `$id`s are versioned per spec release.

### 3.3 Plan inputs

**Purpose.**
- Inputs parameterize a plan. The main use is the **reusable, hash-pinned file plan**: review it once (`blokky plan`, note the SHA-256), then run it many times with different values.
- Compared with plain bash, they are the equivalent of `bash -s -- feature/x <<'EOF'`, so they amplify nothing.
- They are more reviewable than captures or `from_env`: values are known before execution, so `leaves` and the hook can show them concretely.
- **Agent guide:** in heredoc plans, write literals; use inputs in reusable plan files. A low-severity lint flags inputs in stdin plans.

**Syntax.** `inputs:` is a **root-only mapping** of `NAME → { default?, description?, in? | matches? }`.
- A list form gets a targeted error with the mapping snippet.
- `inputs:` on a non-root node gets a targeted error.

```yaml
inputs:
  BRANCH:                                          # no default -> required, non-empty
    description: Branch whose CI run to wait for
    matches: '^[A-Za-z0-9._/][A-Za-z0-9._/-]*$'    # also rules out a leading '-'
  ENVIRONMENT:
    default: staging
    in: [staging, production]
  NOTE:
    default: ''                                    # optional, may be blank
```

**Semantics.**
- **Strings only.** `default: 3` is the string `'3'`. There is no `type:`; using it gets a targeted error ("every value is a string; constrain with `in:`/`matches:`").
- **Required is derived.**
  - No `default:` → required and **non-empty**.
  - With `default:` → optional.
  - There is no `required:` key; it gets a fix-it error ("omit `default:` to make an input required").
  - Optional-without-default doesn't exist, so **inputs are always defined**. "Optional and may be blank" is `default: ''`, tested with `empty`/`not_empty`.
- **Explicit values beat defaults.** `--input NOTE=` gives `''`, not the default.
  - An empty value for a required input is `BK_INPUT_EMPTY`.
- **Defaults are literal strings.** `default: { from_env: … }` and other references are targeted errors.
- **Constraints.** At most one per input: `in: [..]` or `matches: '…'`.
  - They have exactly the condition-operator semantics (`RegexOptions.NonBacktracking`, no trimming); there is no `ignore_case`, no `not_*`, no numeric operators.
  - The `default` must satisfy the constraint (static error).
  - A violation is `BK_INPUT_INVALID`, quoting the constraint.
  - Constraints are **author hygiene, not a control**. The hook never auto-allows *because* constraints exist.
- **`description`** is display only, never interpolated (`${` there is an error). It appears in `blokky plan` and in the missing-input error. It is **never** shown in hook `ask` reasons, because it can mislead the approver.
- **Plan scope.** Inputs bind into a **plan scope that encloses the root node**, before anything runs.
  - Root modifiers may reference them: `name: … ${BRANCH}`, `cwd: { var: REPO }`, root `when`, root `env`.
  - Collisions with `capture`, `as`, `tempdir.as` or derived names (`<AS>_INDEX`) are the existing shadowing error; the message names the input ("BRANCH is declared in `inputs:` (line 4)").
- **Usage.** Inputs are used only through the existing variable forms:
  - `{ var: X }` in `exec` arrays, `env`, `cwd`, `path:`, `stdin`;
  - `export:`;
  - `${X}` in display strings;
  - the `var:` condition subject;
  - `outputs:`.
  - argv[0] from an input is an error, as for every variable. String-form `exec` can't reference inputs.
- **Inputs never reach literal fields:** `timeout`, `every`, `max`, `count`, `concurrency`, `each`, `glob`, `shell`, `ok_exit`, `defaults.*`, `tempdir.prefix`, `inputs.*` keys. A targeted error says "`timeout` is literal; inputs can't parameterize it". Accepting references there later would be additive (Q-in5).
- **No secret inputs.** `secret:` on an input is a parse error with a fix-it pointing to `env: { X: { from_env: X, secret: true } }`.
  - A CLI value has already leaked into the agent transcript, `ps` and shell history before Blokky could mask it.
  - Inputs are reported verbatim and are non-secret by definition.
  - Lint warns on secret-looking names (`*TOKEN*`, `*SECRET*`, `*PASSWORD*`, `*_KEY`).
- **No static pruning by inputs.** `host:` stays the only statically pruned subject.
  - `plan`/`leaves` show both branches; an optional `decided_by_inputs` annotation is allowed.
  - A required input stays required even if it is only used inside a pruned `host:` branch.

**CLI.**
- **One spelling: `--input NAME=VALUE` as two tokens**, repeatable.
  - No `-i` (it reads as "interactive" in docker/sudo), no `--input=…`, no `--input:…`.
  - All flags come before the plan path or `<<`.
  - Split on the first `=`. NAME follows the variable pattern; a case-insensitive did-you-mean suggests close names (`branch` → `BRANCH`).
- **Value rules.**
  - Rejected: NUL; all C0/C1 control characters except TAB, **including CR and LF**; bidi and zero-width characters; U+FFFD (what .NET leaves behind for invalid UTF-8 argv).
  - Per-value and total size caps (a free config key, a few KiB by default).
  - Multi-line data belongs in the plan or a file, not an input.
- **Sources.** Values come only from argv (CLI) or the `inputs` object (MCP).
  - Never from stdin, env (`BLOKKY_INPUT_*`), config, `--input-file` or `--inputs-json` in v0.1.
  - Missing inputs are never prompted for, even on a TTY.
- **Per command:**

| Command | Behaviour |
|---|---|
| `run` | Strict. All input errors are reported at once (pre-run envelope, §3.2), **before** the run id, run dir, validator, root `when`/probes, root `tempdir` or any spawn. |
| `validate` | Strict: a verdict is for a concrete invocation. |
| `hook check` | Parses `--input` from the command string. Input errors → `deny` with a fix-it (Q-in6). Valid values feed its `leaves` analysis. |
| `plan` / `leaves` | Accept `--input`. A missing required input still exits 0, shows `<input:NAME>` markers (distinct from `<dynamic>`) and sets `inputs_complete: false`. Unknown or duplicate inputs are still errors. |
| `lint` | Accepts `--input`; checks names and constraints; never requires values. |
| others | No `--input`. |

- **Missing-input errors print the exact copyable command,** e.g. `blokky run --input BRANCH=<value> --expect-sha256 <h> wait-ci.blokky.yaml`.
- **Usage errors** (malformed flags, input errors) use the usage/invocation exit code (Q6).

**Hook, validation and approval.**
- **`--input` is a data flag** (§2): its values feed the hook's own `leaves` analysis. Budget injection (`--max-duration`) must never drop or reorder them.
- **Canonical value grammar.** Each `--input` word is either:
  - bare `NAME=VALUE`, with VALUE in `[A-Za-z0-9._/:@%+,=-]*`; or
  - `NAME='…'` single-quoted, with no `'`, CR or LF inside.
  - Rejected: `"…"`, `$`, backticks, `\`, `$'…'`, unquoted globs or braces, and a bare `~` after `=` (bash expands `--input X=~/repo` before Blokky starts).
  - Anything non-canonical → `ask`/`deny`, never allow.
  - Agents resolve values first and pass literals: `--input B="$(git branch --show-current)"` is non-canonical.
- **Concrete rendering with provenance.**
  - `rendered`, cwd, `stdin` and `path:` effects show input values quoted for the unit's own shell (POSIX single quotes; pwsh `'…'` with `''` doubling).
  - `leaves` and the validator request carry `inputs` (`NAME → { value, source: cli|mcp|default }`) and `inputs_complete`.
  - Runner-rules mode matches real values, e.g. `gh run list -b 'feature/x' …` against `Bash(gh run list:*)`.
  - Documented pitfalls:
    - prefix-rule permissiveness is the runner's own;
    - a value that can't be rendered as one word → `ask`;
    - `script` text doesn't become matchable because its exports are concrete;
    - pwsh units → `ask` in rules mode (Q-c).
- **Concrete values never clear risk flags.** `dynamic_exec` (`exec: [sh, -c, { var: CMD }]`), `exec_env_risk` and the nested-plan dynamic marking still fire: caller-supplied data run as code is still dynamic code.
- **Input-derived paths.** Read and cwd effects derived from inputs (`cwd: { var: DIR }`, `path:`, `stdin: { file: … }`) are checked against the workspace read roots; anything outside → `ask`.
- **`ask` reasons show input values in full,** never silently truncated. If the display budget is exceeded, an explicit marker is shown and the decision stays `ask`.
- **Hashes.**
  - `--expect-sha256` stays **plan bytes only**. Argv has no TOCTOU gap: the hook sees exactly what `run` receives.
  - The report, `leaves`, the validator request and `run_start` carry `inputs` and **`inputs_sha256`** (RFC 8785 canonical JSON of the effective map).
  - Q-a approval records bind an **invocation digest** of `{ plan_sha256, spec_version, inputs, cwd }`. A rewritten `--approval <id>` run takes its inputs from the record, or fails with `BK_APPROVAL_MISMATCH`.
  - "Template approval" (any valid inputs) is not offered in v0.1 (Q-in2).
- **Nested Blokky (A8).**
  - Static expansion happens only when the nested plan is static and its `--input` values are literals (or parent inputs known at hook time). Otherwise the unit is dynamic → `ask`.
  - Inputs are never inherited implicitly.
  - A child with a missing required input is a static error in the parent's `leaves`.
  - There is no string concatenation, so `NAME=<var>` can't be built in an `exec` array; nested runs with variable inputs need a `script` leaf and are dynamic.
- **MCP.**
  - The `run`/`plan`/`leaves` tools take `inputs: { NAME: "VALUE" }`. Non-string values and `null` are rejected (no coercion), and **duplicate JSON keys** are rejected.
  - Validation is the same; `source: "mcp"`.
  - "Never auto-allow `run`" still holds.

**Agent ergonomics.**
- **Targeted errors with corrected snippets** for:
  - `${{ inputs.X }}`, `{ var: inputs.X }`, `{ input: X }`, `${X}` in executed or literal fields;
  - lowercase names;
  - `required:`;
  - `type:`;
  - `secret:`;
  - list form;
  - non-root `inputs:`.
- **Synonyms:**
  - `enum|options|choices → in`;
  - `pattern|regex → matches`;
  - `desc|help → description`;
  - `value → default`;
  - root `params|parameters|args|arguments|vars|variables → inputs`.
- **Lint:**
  - a `script` that references `$X`/`$env:X` for a plan variable neither exported nor in `env:` → fix-it `export: [X]` (applies to all variables);
  - a condition that can never match an `in:`-constrained input (`equals: prod` vs `in: [staging, production]`) → "did you mean `production`?";
  - an input declared but never referenced (uses in pruned `host:` branches count);
  - the `--` argument-injection hint for inputs used as `exec` elements.

### 3.4 Configuration (in `blokky-cli`)

**Files, in increasing precedence:**

1. User-level: `~/.config/blokky/config.json` on Unix, `%APPDATA%\blokky\config.json` on Windows. Whether `XDG_CONFIG_HOME`/`APPDATA`/`HOME` are honoured or the passwd home is used is open (Q-d).
2. Repo-level: `.blokky/config.json` at the repo root. Committed.
3. Repo-level local: `.blokky/config.local.json`. Gitignored by convention.

There is no user-level `config.local.json` and no `BLOKKY_CONFIG`. Config cannot supply input values or input defaults. For tests, `--config <file>` loads a file at **repo trust** (it can't set user-only keys).

**Format:** JSONC, published as `blokky-cli/schema/blokky-config.schema.json`.

**Key trust classes** (published per key as `x-blokky-trust`):

1. **User-only keys:**
   - executable paths (validator, shell paths, engine CLI);
   - locations (state/log dir, temp root);
   - enabling switches (`become`, later the consent mechanism);
   - `require_hook_approval` (Q-a);
   - runner-rules settings (§3.9).

   A repo-level, repo-local or `--config` file that sets them is a **config error**.
2. **Free keys:** everything else, e.g. lint severities, strict mode, caps, tail sizes, default shell, defaults.

Lint and caps are hygiene and ergonomics (§1.2), so they need no ordered "raise-only" protection. A user who wants a floor sets it at user level; the hook and validator use user-level settings.

**Protection guidance.** Document deny rules for agent edits of `.blokky/**` and the user config. State plainly that repo-level settings protect against accidents, not against a compromised agent.

**Commands:** `blokky config show --effective` prints each setting with its source file and trust class.

### 3.5 Leaf execution: `exec` and `script`

**`exec` — a program with arguments, no shell.**

- **Array form (canonical):** `exec: [prog, arg, { var: X }]`.
  - Variables appear only as whole elements; argv[0] from a variable is an error.
- **String form (shorthand for static commands):** `exec: dotnet build -c Release`. Blokky splits it with POSIX shlex rules (whitespace, single/double quotes, backslash), with **no expansion, no globbing and no operators**.
  - Unquoted metacharacters (`|`, `&`, `;`, `<`, `>`, `(`, `)`, `*`, `?`, `~`, `#` at word start) are errors.
  - `$` and backticks are errors outside single quotes, because users expect them to expand.
  - The error text: "use the array form with `{ var: X }`, or `script:`".
  - `blokky plan` and `leaves` show the resulting argv.
- **Windows.** Programs receive a single command line. Blokky builds it from argv with the standard `CommandLineToArgvW`-compatible quoting (what .NET `ArgumentList` does). `.bat`/`.cmd` targets are an error (they are re-parsed by cmd.exe); the message suggests running such tools (`npm`, `npx`) in a `pwsh` `script`.
- **Distroless containers.** `exec` works where no shell exists.

**`script` — shell text run by a shell.**

- **Script file.** The text is written to a temp file in the run dir (`runs/<id>/scripts/<node-id>.<ext>`, mode 0600) and passed to the shell **as a file**:
  - no command-line length limits (32K chars on Windows, ≈128 KiB per argument on Linux);
  - exact bytes, no quoting layer;
  - the exact script stays inspectable until `clean`.
- **Invocation per shell.** The normative runner table is in `blokky-specs/spec/shells.md`:
  - bash/sh: `--noprofile --norc <file>`.
  - pwsh: `-NoProfile -NonInteractive -File <file>.ps1`.
  - Windows PowerShell 5.1: additionally `-ExecutionPolicy Bypass` (process scope); the file is written with a **UTF-8 BOM** so non-ASCII text is read correctly.
  - Explicit `pipefail`/`-e` decisions (recommend no implicit `-e`).
  - pwsh `$ErrorActionPreference` / `$PSNativeCommandUseErrorActionPreference`.
  - Any prologue is shown in `leaves`.
- **File vs `-c`.** Differences from `-c` mode are documented (`$0`, script name in error messages).
- **Containers.** The run's scripts dir is **mounted read-only** into the container. A `script` leaf needs a shell in the image.
- **`become`.** The script file must be readable by the target user (Q17, spike).
- **Lint.** Warns when `script:` text is not a `|`/`|-` block scalar.
- **Shell-feature detection (very low priority, later).** A lint hint when a `script` uses no shell features: "use `exec` (faster, no shell)". Execution never auto-switches between the two.

**Supported shells:**

| Shell | Milestone |
|---|---|
| `bash`, `sh`, `pwsh`, `powershell` (Windows PowerShell 5.1) | v0.1 |
| `zsh`, `fish` | later |

**`cmd` is not supported.** cmd textually expands `%X%` in every command before parsing, so passing variables via the environment degrades to textual substitution everywhere. This is a language-design decision.

**Shell × OS support matrix** (`shells.md`):
- fish and zsh have no native Windows build;
- bash/sh on Windows means Git Bash/MSYS (recommended policy: only when configured explicitly; Q4).

A reached `script` that names a shell unsupported on the current OS is an error; statically false `host:` branches are pruned first, so portable plans work.

**Environment of a leaf.**
- Inherited process env, plus the node's `env:`/`export:` mappings (scoped from ancestors).
- `BASH_ENV`/`ENV` are cleared after the merge; node env cannot set the forbidden keys (§3.9 A3).

**Per-shell injection properties** (documented):
- POSIX: unquoted `$X` allows argument injection; lint requires `"$X"`.
- PowerShell 7.3+ vs 5.1 argument passing.
- pwsh lint: `$X`/`${X}` where X is an exported name → use `$env:X`.
- `.cmd` shims (`npm`, `npx`) called from pwsh are re-parsed by cmd.exe, so variable arguments carry cmd's risks (policy: Q-l).

**Executable resolution** (`exec` argv[0] and shell binaries).
- Blokky resolves against `PATH` only (never the cwd or the app dir), using **Blokky's own** environment, not node `env:`.
- Absolute paths are passed to the launcher, so platform search orders don't matter (spike: confirm).
- On Windows a fixed `PATHEXT` policy applies.
- The resolved absolute path appears in `leaves`, `blokky plan` and the report.

**Shell resolution order** (for `script`): leaf `shell:` → `defaults.shell` → config `shell` → detection. Detection is `bash` → `sh` on Unix (never `$SHELL`) and `pwsh` → `powershell` on Windows. It is deterministic and documented.

**Strict-mode lint per shell** (`script` text).
- v0.1 uses **token-level heuristics** for POSIX shells and PowerShell. They are enough to produce the `dynamic_exec` flag (§3.9 A4).
- A hand-written POSIX lexer comes after v0.1; tree-sitter is a later option.

### 3.6 Containers and privilege (milestone: later; language specified now, mechanics in design notes)

The **language-level** syntax and semantics (value shapes above, path model, interactions) are in `blokky-specs/spec/containers-and-privilege.md`, written in the draft. Spike-dependent mechanics live in `blokky-cli/docs/design/notes/`. The keywords are reserved in v0.1.

**Path model (applies already in v0.1).**
- `path:`, `glob`, `tempdir`, `cwd` and `capture_file` paths are **host** paths, evaluated by Blokky.
- Under `container`:
  - `cwd` maps through the mounts; an unmapped cwd is an error;
  - `exec` argv[0] resolves **inside** the container; the run's scripts dir is mounted read-only for `script` leaves;
  - probes run inside the container while `path:` checks the host, and lint flags `path:` conditions on container nodes.

#### Container modifier

- **Ephemeral mode** (`{ image, mounts, user, env_pass }`): Blokky starts the container once for the node and runs each leaf with `exec`.
  - At node end the container is **stopped**, not removed, so it can be inspected.
  - It is removed by `clean`/`gc`, and is labelled with the run id.
- **Existing mode** (`{ exec: NAME }`): exec into an already-running container. Scope: Q16.
- **Report:** the engine, image digest and container id.
- **`leaves`:** each leaf's container context. The engine commands Blokky will issue are listed as **derived** units from the single invocation builder.
- **Engine integration:** drive engine CLIs (`docker`, `podman`, `nerdctl`) behind `IContainerEngine`.
  - The engine CLI is spawned with Blokky's env, not node env, so node `DOCKER_HOST` can't redirect it.
  - An API driver is possible later; runc is out of scope.
- **Termination via the engine:** an in-container process group with a recorded PID; TERM → grace → KILL via `<engine> exec … kill`; stopping the container as the fallback. **Provisional — verify in spike** (incl. images without a shell or `kill`).
- **Security docs:** containers aren't a sandbox by default. Mounting the engine socket or running privileged is equivalent to host root, and lint flags both. The default mount is the cwd, only if it lies under the repo root (recommendation; Q16).

#### Become modifier

- **Unix:** `sudo -n -u <user>` (passwordless only), or **dropping** privileges when Blokky runs as root.
- **`defaults.become: USER`** applies to every node that doesn't set its own `become`. For root runs this makes "drop to the invoking user unless marked" the explicit default.
- **Root-run lint** (when `become` lands): warn when running as root without `defaults.become`.
- **Config resolution under `sudo`:** when euid is 0, only root-owned config counts as user-level; the drop target is derived from `SUDO_UID` and validated against passwd. Details: Q17.
- **Windows:** Sudo for Windows elevation depends on out-of-band consent (deferred, §3.7).
- **Enabling** `become` is a **user-only key**. It is an accident-prevention and reviewability opt-in; `sudo -n` in a leaf is equally available.
- **Strict lint:** flags raw `sudo`/`doas`/`su`/`pkexec`/`runas` in leaves.
- **Visibility:** `leaves` and the report show the effective user and elevation per leaf.
- **Variable, tempdir and script-file transport across `become`** is open (Q17, spike):
  - sudo's env reset drops Blokky variables;
  - 0700 tempdirs, capture files and script files are unreadable by the target user.

  v0.1 tempdir/capture-file ownership semantics may later be **extended** (ACL/chown) but **never relaxed**.
- **Kill across a privilege boundary isn't guaranteed.** Provisional — verify in spike.
- **Alternative:** `container` with `user:` is often the simpler way to run as another user.

**Agent handoff pattern (works in v0.1):**
1. The agent writes the plan to a `*.blokky.yaml` file.
2. The user reviews it (`blokky plan` / `blokky leaves`) and notes the printed SHA-256.
3. The user runs it themselves: `sudo blokky run --expect-sha256 <hash> [--input NAME=VALUE …] file.blokky.yaml`. The hash covers the plan only; the reviewed input values are visible in `blokky plan --input …` and recorded in the report.

Any password prompt happens in the user's terminal, before Blokky starts.

### 3.7 Interaction model

- **Missing plan inputs are never prompted for** (§3.3).
- **Leaves never read interactive user input.**
  - stdin is `/dev/null` (never a closed fd 0) unless the plan supplies `stdin:`.
  - Leaves run in their own session with no controlling tty, so `/dev/tty` fails immediately instead of hanging.
  - Lint flags commands known to prompt where a non-interactive flag exists.
- **No passwords, ever.** Blokky never prompts for, accepts, stores or forwards passwords.
- **Out-of-band OS consent** (UAC via Sudo for Windows, `sudo -A` askpass, polkit) is **deferred** to the roadmap.
  - The keyword **`consent`** (a node modifier and a user-only config section) is reserved.
  - Until then, any node that would need consent fails with `BK_CONSENT_UNSUPPORTED`. The handoff pattern covers the need.

### 3.8 Process lifecycle, cancellation and runtime-owned resources

**Process launcher (`IProcessLauncher`).**
- `exec` is the primitive every leaf compiles to (§2 Architecture).
- `System.Diagnostics.Process` can't create a new session/process group (Unix) or a suspended/job-assigned child atomically (Windows).
- Options:
  - direct `posix_spawn` with `POSIX_SPAWN_SETSID` (glibc ≥ 2.26, macOS) / `CreateProcess` P/Invoke;
  - a **self-re-exec trampoline** (`blokky __exec`);
  - .NET 11 Process additions (`KillOnParentExit`, `SafeProcessHandle`).
- **This is spike #1.** Acceptance criterion: **"the harness SIGKILLs blokky → no leaf survives longer than the grace period"**, on every OS.

**Lifeline (orphan prevention).**
- With plain bash, a harness kill of the shell's process group reaches every child. Blokky puts leaves in their own sessions, so it must provide the equivalent itself.
- **Unix:** each trampoline (or one per-run watchdog in its own session) holds the read end of a pipe whose write end only the Blokky parent owns. On EOF it kills the registered groups (TERM → grace → KILL). On Linux, `PR_SET_PDEATHSIG` from a dedicated long-lived spawn thread is an additional fast path. Spike-dependent options: `KillOnParentExit`, `PDEATHSIG` from a dedicated thread.
- **Windows:** a kill-on-close Job Object holding all children; per-leaf jobs nested in the run job (Windows 8+). Breakaway is never allowed.

**Leaf completion and descendants.**
- A leaf completes when its **direct child exits**. Pipes then drain for a bounded `drain_timeout` and are closed; the report flags `descendants_held_output`.
- **By default, the leaf's remaining session/job is killed at leaf completion** (`keep_descendants: true` opts out, visibly). Real daemons belong in `services`.
- Every descendant dies no later than run end, on every OS.
- Escape via double-fork or setsid-by-the-leaf is best-effort and documented.

**Windows graceful termination.**
- "Graceful" = `CTRL_BREAK_EVENT` to the leaf's process group if a console is attached, else none; then `TerminateJobObject`. The report records which method was used.
- Spike: give each leaf a hidden console (`CREATE_NEW_CONSOLE` + hidden window, or ConPTY) so CTRL_BREAK always works.
- Race-free job assignment needs `CREATE_SUSPENDED` or `PROC_THREAD_ATTRIBUTE_JOB_LIST`.
- A harness-launched Blokky on Windows that gets `TerminateProcess` skips shutdown mode. The run budget is the cleanup path there, and `clean`/`gc` handle the rest.
- Provisional — verify in spike.

**Blokky's own termination.**
- SIGINT/SIGTERM/SIGHUP (`PosixSignalRegistration`) and Windows console control events switch Blokky into **shutdown mode**:
  1. cancel all nodes;
  2. run `finally`;
  3. write a partial report;
  4. exit with a documented code.

  All of this happens under the single cleanup deadline.
- A **second interrupt** skips `finally` and force-kills.
- A closed stdout (`| head`) has defined behaviour: report to file only, a documented exit status.
- Children in their own session don't receive the terminal's Ctrl-C; say so.

**Single cleanup deadline.**
- Once in cancel, budget or shutdown mode, every sub-timer (grace, `finally_timeout`, `drain_timeout`, validator time) is clamped to `min(own, deadline − report_slice)`.
- A fixed final slice is reserved for flushing the report.
- `cleanup_reserve` is the one setting; there is no separate `shutdown_budget`.

**Cancellation timeline.** A normative section with a worked example and a conformance case per rule:
- **Nested limits:** the effective limit is the minimum of the nested limits.
- **`finally`:** gets its own `finally_timeout`. Error precedence: the body error is primary and a `finally` error is attached; a failing `finally` fails an otherwise successful node.
- **Joins:** `all`/`any`/`race` complete only after cancelled siblings are **confirmed**: direct child reaped and KILL sent to its session/job, bounded by grace + a fixed reap timeout. Anything still alive is reported as `survivors`.
- **Waits:** `sleep` and `every` waits respond to cancellation immediately.
- **Skipped nodes:** a node skipped by `when` runs no `finally`.
- **Grace accounting:** Q-k.

**Status propagation (must be specified, D15):**
- whether a `do`/`all`/`any` whose children were all skipped is succeeded or skipped;
- whether an `ignored` child counts as a success for `any`;
- parent status for a child that is `timed_out` vs `cancelled`;
- the plan exit code when the root `when` is false;
- whether a condition error in `when` fails the node, and whether `ignore_errors` applies.

**tempdir safety.**
- **Creation:** a fresh, exclusive mkdtemp-style dir (0700, unpredictable name, optional `prefix`) under the user-only temp root. The default temp root is a per-user 0700 directory, never bare `/tmp`.
- **Identity:** keep the dir fd from creation and record dev/inode, so deletion can verify it's the same dir.
- **Deletion** (by `clean`/`gc`/`--cleanup`):
  - fd-relative (`openat`/`unlinkat`, `O_NOFOLLOW`);
  - never following symlinks, junctions or reparse points;
  - never crossing a filesystem boundary;
  - never outside the registered roots;
  - Windows: retry with backoff for files locked by AV or indexers.
- **Failure reporting:** how cleanup failures are reported is Q15.

**Run registry and state dir.**
- A per-user state dir (`XDG_STATE_HOME` / `%LOCALAPPDATA%`), 0700 dirs and 0600 files, never inside a repo. Its location is a user-only key.
- It holds:
  - per-run dirs (report, events, logs, capture files);
  - the registry of tempdirs and containers;
  - the advisory liveness lock.
- File names derive from node ids, never from interpolated `name`.
- Per-leaf and per-run log caps, with a truncation marker.

**Locks (later).**
- OS advisory locks (`flock`/`LockFileEx`), released on process death; no PID files and no gc needed.
- A per-user 0700 lock dir; Windows `Local\` names.
- Names match `^[a-z0-9][a-z0-9._-]{0,63}$`.
- `lock: { wait }` bounds waiting, within the node's `timeout`, with a distinct code.
- Nested locks: Q-j.
- Wait time appears in the report.

**Services readiness (later).**
- Port checks try both `127.0.0.1` and `::1`.
- A service that exits before it is ready fails the node immediately.
- Service output is drained continuously.
- `ready_timeout` applies.
- Readiness checks are listed as effects (§3.9).

**Paths.**
- Relative paths resolve against the **invocation cwd** for both stdin and file plans.
- Windows `.exe` started via WSL interop escapes the Linux kill; documented as best-effort.

### 3.9 Validation, hook and hosts

**`blokky leaves` and the validator request describe everything that affects execution.**

- **Per executable unit** (`exec`, `script`, `probe`, `service`, `derived`; with context flags such as `in_finally`):
  - for `exec`: the argv with resolved absolute argv[0]; for `script`: the shell name and absolute path, the script text and the script file path;
  - the exact invocation;
  - **`stdin`**: the literal content, or a dynamic marker;
  - the effective cwd, or a dynamic marker;
  - **`env_overrides`**: node-level env, names and static values, secrets as references, dynamic keys marked;
  - the `export:` names (values static or marked dynamic);
  - container/become context;
  - repetition upper bounds;
  - flags `dynamic_exec` and `exec_env_risk`;
  - **`rendered`**: a shell-equivalent one-liner in the unit's own shell syntax, with env assignments as a prefix. Examples: `BASH_ENV= GIT_DIR=/x RUN_ID=<dynamic> gh run watch "$RUN_ID"`; for pwsh, `$env:X='…'; …`. Humans and rule matchers can read a unit exactly like the equivalent bash call.
- **Run-level fields:**
  - the plan SHA-256 and spec version;
  - `inputs`, `inputs_complete` and `inputs_sha256` (§3.3);
  - **plan-dependent effects only**: `path:` subjects, glob roots, the tempdir prefix, `capture_file`, `stdin: { file }` sources, readiness targets. Fixed runtime writes (state dir, logs, report) are documented once, not listed.

**A3: environment that changes what a leaf runs.**
- Node `env:` may not set `PATH`, `BASH_ENV`, `ENV`, `SHELLOPTS` or `BASHOPTS`; they are cleared after the merge.
- A published **execution-influencing variables** list (`LD_*`, `DYLD_*`, `NODE_OPTIONS`, `GIT_SSH_COMMAND`, `GIT_CONFIG_*`, `PERL5OPT`, `PYTHONPATH`, `DOTNET_STARTUP_HOOKS`, `JAVA_TOOL_OPTIONS`, `DOCKER_HOST`, …) marks units with `exec_env_risk: true`.
- `hook check` never auto-allows those without an explicit validator verdict.

**A4: dynamic code.**
- The lint heuristics emit `dynamic_exec` for:
  - a variable in command position;
  - `eval` / `source` / `.` / `sh -c "$X"`;
  - `iex` / `Invoke-Expression`;
  - dynamic stdin into a known interpreter.
- Validators get the flag without needing their own shell parser.

**A8: nested Blokky.**
- `leaves` detects nested invocations: an `exec` whose argv[0] resolves to the Blokky binary, or a `script` whose text invokes `blokky`.
- A static nested plan is expanded inline as `nested_plan` (rules for its inputs: §3.3). Otherwise it is marked dynamic, and the hook returns `ask`.
- Blokky exports `BLOKKY_RUN_ID` and the remaining budget to leaves.

**Side-effect-free commands.**
- `plan`, `leaves`, `lint`, `config show`, `schema`, `report` and `version` spawn **no process**, run no probe, create no files and run no validator.
- Conditionals show both branches; dynamic `for` sources stay unexpanded; a `glob` may be expanded but is marked as a snapshot.

**Which commands call the validator.** `validate` and `hook check`. Whether `blokky run` calls it when no approval record exists is Q-b.

**`blokky hook check <runner>`** (v0.1 for Claude Code).
- **Input:** reads the runner's tool-input JSON on stdin.
- **Canonical forms only:**
  - exactly `blokky run [allow-listed flags] <<'BLOKKY_PLAN_EOF'\n<body>\nBLOKKY_PLAN_EOF` (plus an optional final newline), parsed with **exact bash heredoc semantics**: any `\r`, `<<-`, multiple heredocs, or anything after the redirection is rejected;
  - or a file path with `--expect-sha256`.
- **Allowed flags**, enumerated by class in `validation-and-permissions.md`: tightening (`--max-duration`, `--expect-sha256`), neutral (`-v`, `--cleanup`), data (`--input`, with the canonical value grammar of §3.3). The same applies to the file form.
- **The extracted body is parsed with the same parser `blokky run` uses.**
- **It never passes through.** It emits an explicit `ask` or `deny` whenever the validator (or runner-rules mode) doesn't allow, including when none is configured or it is unavailable. Spike: verify that a hook `ask` overrides the runner's allow rules, so a "don't ask again for `blokky run`" rule can't recreate the prefix allowlist.
- **Auto-allow** is allowed for the read-only subcommands (`plan`, `leaves`, `lint`, `config show`, `schema`, `report`, `version`). `clean`/`gc`: Q-t.
- **Read roots:** plan-dependent read roots outside the runner's workspace scope → `ask`.
- **Run budget:** reads the tool call's `timeout` and requires or injects `--max-duration` (§3.2).
- **`ask` reasons include a leaves summary:** the `rendered` lines and plan-dependent effects (never the `name` labels, which can mislead), so the human approves what will run.
- **The docs say in their first paragraph:** **never allowlist `blokky` by prefix** (e.g. `Bash(blokky run:*)`); use the hook.

**Runner-rules mode** (`hook check --rules`).
- Evaluate every unit's `rendered` command (leaves, probes, derived engine commands) against the runner's **own** Bash allow/deny rules, as if each were a separate Bash call.
- Allow if all are allowed; deny if any is denied; otherwise `ask`. Unparseable or dynamic units fall back to `ask`.
- This makes "Blokky ≡ the same bash calls" the default posture without a custom validator. Matching semantics: Q-c.

**A2: what is approved is what runs.**
- The hook and `blokky run` can resolve differently: config location, `PATH`, cwd, the `blokky` binary, file content.
- In v0.1:
  - heredoc is the agent-canonical form;
  - the plan SHA-256 is in `plan`/`leaves`/the report;
  - a tightening `--expect-sha256` exists;
  - the hook normatively uses the tool-input cwd;
  - plan inputs are part of what is approved; `inputs_sha256` is reported (§3.3).
- Approval binding (approval records, `require_hook_approval`, hook input rewriting to `<abs>/blokky run --approval <id> --max-duration <t>`) is Q-a.

**MCP host (later; shapes the architecture now).**
- Separate read-only tools (`plan`, `leaves`) and a `run` tool.
- `run` takes an explicit `cwd` and an `inputs` object (§3.3).
- `hook check` gets an MCP-input variant. Tool-name approval is effectively a prefix allowlist, so the same "never auto-allow `run`" rule applies.
- `notifications/cancelled` and stdin EOF map onto shutdown mode.

**Agent guide additions.**
- **Windows invocation:** Q-h.
- **Heredoc-expansion sentinel:** Q-o.

## 4. Open questions — ask me, don't decide silently

Ask in small batches using multiple-choice questions where possible, with a recommendation each. Record the answers in grouped ADRs, in the repo they concern.

**Needed for the language spec draft:**

1. Confirm the modifier order (§3.1).
2. Variable details: the JSON path subset; `collect` milestone and syntax.
3. Conditions:
   - confirm the subject/operator tables;
   - `matches` semantics (whole-text search vs per-line, `(?m)`);
   - trimming;
   - the InvariantCulture number grammar;
   - the default `probe_timeout` and probe capture limit.
4. Shells: confirm the v0.1 set, the detection lists, the runner invocation table (pipefail/-e), and the Git Bash policy (recommended: explicit only).
5. Duration defaults: a global default `timeout`? `leaf_timeout`, `finally_timeout`, `drain_timeout`, `cleanup_reserve`.
6. Exit codes of `blokky`:
   - success, plan failure;
   - usage/invocation error (malformed flags, input errors);
   - needs-approval, validation rejection;
   - parse/lint error, config error;
   - budget exceeded, interrupted;
   - expect-sha256 mismatch;
   - internal error.
7. Output defaults: tail size, report byte budget, log caps.
8. Default `max_items` for `for` over captured output (no config gate).
9. Validator plugin protocol: request/response JSON, allow/deny/ask semantics, timeouts, behaviour when unavailable.
10. Strict-mode lint rule set: initial rules, IDs, severities, configuration, suppression.
11. Config details: exact file names; the full list of user-only keys.
12. Are `name` values required to be unique, or are node path ids enough for report readability and diagnostics?
13. Plan version scheme: what does `blokky:` hold (spec version `0.1`?), and is it required?
14. *(Answered: a leaf's remaining session/job is killed at leaf completion by default; `keep_descendants: true` opts out. See §3.8.)*
15. How does `clean` report a deletion failure (partial cleanup), and what exit code does it use?
16. **Containers, language level** (moved into the draft batch): existing-mode scope and default mount policy (recommended: cwd only if under the repo root). Engine detection and termination mechanics stay later.
17. **`become`, language level** (moved into the draft batch):
    - `defaults.become` semantics;
    - root-run lint severity;
    - `doas`;
    - config resolution under sudo;
    - variable, tempdir and script-file transport across `become`.
- **Q-a.** Approval binding: approval records, `require_hook_approval`, hook input rewriting. Records bind the invocation digest (plan sha, spec version, inputs, cwd; §3.3).
- **Q-b.** Does `blokky run` call the validator itself when one is configured and no approval record exists?
- **Q-c.** Runner-rules mode: rule-matching semantics against `rendered`, and when units fall back to `ask` (incl. the input-rendering pitfalls in §3.3).
- **Q-d.** Trust of `XDG_CONFIG_HOME`/`APPDATA`/`HOME` for locating the user config: honour with drift detection, or use the passwd home?
- **Q-e.** `setsid` via `posix_spawn` vs trampoline; per-leaf lifeline vs per-run watchdog.
- **Q-f.** The default run budget and its relation to harness timeouts.
- **Q-g.** Which dynamic constructs force `ask` by default: dynamic cwd/argv/stdin/env, `dynamic_exec`, `exec_env_risk`, nested plans. (Bound plan inputs count as static; missing inputs → `ask`.)
- **Q-h.** Windows agent invocation: a PowerShell here-string piped to `blokky run`, or file form + `--expect-sha256`? Includes the pwsh grammar for `--input` values.
- **Q-j.** Nested locks: forbid, or acquire in a fixed order?
- **Q-k.** Does the grace period count against `timeout`?
- **Q-l.** `.cmd` shims called from pwsh with variable arguments: lint, or document only?
- **Q-m.** `empty`/`not_empty` on an undefined variable: condition error, or "empty"?
- **Q-n.** Does a condition error inside `assert` fail with the assertion's user `code`?
- **Q-o.** A heredoc-expansion sentinel (e.g. a required `# $BLOKKY_RAW` comment that disappears if the delimiter was unquoted), or document the gap?
- *(Q-p answered: plan variables reach commands only through explicit `env:`/`export:`; §2, §3.1.)*
- **Q-q.** Repository split details:
  - repo names (`blokky-specs`/`blokky-cli`, or `blokky-spec`/`blokky`);
  - where the testkit *interface* vs implementation lives;
  - submodule vs pinned checkout;
  - license of the spec text (Apache-2.0 vs CC BY 4.0).
- **Q-r.** Default and maximum `concurrency` for `all`/`any`/`for`; how `any` aggregates errors when all children fail.
- **Q-s.** May `finally` nodes `capture`, and are those bindings visible anywhere?
- **Q-t.** May `hook check` auto-allow `blokky clean <run-id>` (deletes only that run's registered artefacts) and `blokky gc`?
- **Q-u.** Does `clean` also remove the report and logs, or keep them unless `--all`?
- **Q-in1.** Native nested-plan invocation: a later `call`/`plan:` node with `inputs:` and child `outputs`? (A module system, against "small"; recommended: not now.)
- **Q-in2.** Template approval: approve once for all constraint-satisfying inputs? (Recommended: no for v0.1; record it in the Q-a ADR.)
- **Q-in3.** `--input-file` (with `--expect-inputs-sha256`, never auto-allowed): later, if needed.
- **Q-in4.** An input sourced from env (secret-only, never resolved in plan/leaves/hook, env values only): later, if needed.
- **Q-in5.** Allowing references in today's literal fields (`timeout: { var: T }`): revisit after v0.1 usage.
- **Q-in6.** Hook verdict on input errors: `deny` with fix-it (recommended) vs `ask`.

**Later** (ask when the related spike or milestone comes up):

16b. Container engine detection order and termination mechanism.

18. Out-of-band consent: mechanisms, consent timeout, unattended detection.

## 5. Deliverables

### `blokky-specs`

```
README.md                 # what the language is, spec status, how versions work
LICENSE  NOTICE  CONTRIBUTING.md  CODE_OF_CONDUCT.md  SECURITY.md (reporting)
CHANGELOG.md              # spec versions (1.0.0-draft.N, …)
ROADMAP.md                # language evolution
.github/CODEOWNERS        # human review for .github/workflows/**
spec/
  overview.md             # concepts, mental model, when (not) to use Blokky
  prior-art.md            # Ansible, duckflux, execline, exec-json, agent-safe-runner, CI step timeouts
  glossary.md
  syntax.md               # YAML surface rules, root document, plan inputs syntax, per-field reference/interpolation table, pitfalls, targeted errors
  reference.md            # every node kind and modifier; validity matrix; security considerations section
  execution-model.md      # modifier order, start order, timeouts, cancellation, status propagation (normative, OS-independent)
  variables.md            # names, reserved names, plan inputs and plan scope, capture forms, binding/scope, outputs, secrets
  conditions.md           # subjects, operators, composites, probes, condition errors
  shells.md               # exec splitting rules, script-file invocation table per shell, OS matrix, resolution, injection properties
  containers-and-privilege.md  # language-level spec
  errors.md               # the BK_ codes a plan can observe (node errors)
  results.md              # abstract run-result model (statuses, errors, outputs) that conformance cases assert on
adr/                      # grouped ADRs + README index
schema/
  blokky.schema.json      # plans; normative for structure
examples/                 # user-facing suite: small per-feature examples and larger comprehensive ones; canonical forms only; README index
conformance/
  README.md               # case format, layout, per-platform overrides, pairwise policy, timing tolerance
  testkit-interface.md    # required fake-executable behaviours (implementation-independent)
  cases/                  # written after the review, before the CLI MVP
```

### `blokky-cli`

```
README.md  LICENSE  NOTICE  CONTRIBUTING.md  CODE_OF_CONDUCT.md  SECURITY.md
CHANGELOG.md
ROADMAP.md                # feature matrix (which language features each release supports), distribution, packaging
.github/CODEOWNERS
docs/
  cli.md                  # run (stdin/file, --input, --cleanup, --max-duration, --expect-sha256), plan, leaves, validate, lint, report, clean, gc, config, schema (--agent), hook check, version; flag classes; parser rules (no response files); exit codes
  contracts/
    output-contract.md    # stdout/stderr rules, run dir, report, pre-run envelope, events, truncation
    leaves-and-validation.md  # leaves document, rendered form, effects, flags, validator protocol
    errors.md             # CLI-only error codes (usage, config, approval, run, cleanup)
  configuration.md        # files, precedence, trust classes, protection guidance
  validation-and-permissions.md  # hook check, canonical forms incl. --input value grammar, flag classes, runner-rules mode, validator plugin, approval binding
  security/threat-model.md  # baseline-vs-bash posture, attack paths (incl. reviewed plan + agent-chosen inputs, argv visibility), hygiene vs controls, best-effort kill/cleanup, masking limits
  agents/
    agent-guide.md        # heredoc convention, inputs vs literals, --input quoting, run budget/background pattern, clean flow, canonical patterns, YAML pitfalls
    skill/SKILL.md        # draft Claude Code skill (embeds the generated cheat sheet)
    mcp-tool-description.md
  install.md              # per platform, attestation + checksum verification, macOS quarantine, upgrade/uninstall
  mcp.md
  dev-setup.md
  design/
    brief.md              # this file
    architecture.md       # Core + hosts, IProcessLauncher, single invocation builder, registry, config loader
    tech-stack.md         # .NET 11, YamlDotNet, System.CommandLine, STJ source gen, TimeProvider, P/Invoke policy
    process-control.md    # launcher, lifeline, setsid, Windows jobs/console, signals, cleanup deadline
    validation-strategy.md
    testkit.md            # implementation of conformance/testkit-interface.md
    ci-cd.md
    spike-plan.md
    notes/                # container termination, privilege-boundary kill, consent
  adr/                    # grouped ADRs + README index
schema/
  blokky-config.schema.json   # incl. x-blokky-trust per key
  report.schema.json  events.schema.json  leaves.schema.json  validator.schema.json
```

**Generated agent cheat sheet:** `blokky schema --agent` emits a compact grammar card generated from the plan schema and the validity matrix. SKILL.md and the MCP tool description embed it, so they never drift from the parser.

## 6. Validation strategy (`blokky-cli/docs/design/validation-strategy.md`; conformance format in `blokky-specs`)

**Conformance suite** (cases in `blokky-specs/conformance/cases/`).
- Each `*.blokky.yaml` case is paired with a normalized expected report, the `blokky` exit code, and expected error codes with line/column.
- **Coverage:** every node kind, modifier, error code, YAML surface rule, start-order rule, cancellation-timeline rule, status-propagation rule, `clean`/`gc` behaviour, and config precedence/trust class.
- **Input binding and CLI argument parsing.** The case format gets a CLI-args field. Cases cover:
  - first-`=` split (`X=a=b`);
  - empty, missing, unknown and duplicate inputs, with did-you-mean;
  - defaults and `source`;
  - shadowing by `capture`/`as`/`tempdir.as`/derived names;
  - root-modifier references;
  - argv[0] from an input;
  - `print-argv` round-trip for spaces, a leading `-`, quotes and non-ASCII;
  - character-rule and size errors;
  - `inputs_sha256` stable across key order;
  - hook acceptance and rejection (`$( )`, backtick, `"…"`, newline, `--input=`, `-i`, `@rsp`, a flag after the path, `~`);
  - MCP non-string, `null` and duplicate keys;
  - nested plans with literal vs variable inputs;
  - the pre-run envelope with no run dir created.
- **Per-platform overrides;** pairwise selection for modifier combinations.
- **Timing tolerance:** assert ordering and status; only generous upper bounds on durations.
- **Shell-runner tests** use real shells and form a separate layer.

**Testkit.** Interface in `blokky-specs/conformance/testkit-interface.md`; `blokky-testkit` (a .NET AOT app) in `blokky-cli`. Never shipped in release archives.
- **Basics:**
  - `sleep`
  - `exit`
  - `print` (stderr/interleaving, `--after`, `--bytes N --rate R`, `--invalid-utf8`, `--crlf`)
  - `flaky --fail-times N --state <file>`
  - `read-stdin`
  - `write-file`
  - `emit-json`
- **Process control:**
  - `spawn-tree --depth N --hang`
  - `escape --setsid | --daemonize`
  - `hold-pipe`
  - `ignore-sigterm`
  - `signals --record <file>`
  - `die --signal <SIG>`
  - `open-tty` (verifies the no-controlling-tty behaviour)
  - common `--pid-file`
- **Services:** `listen --port P --after <dur>`.
- **Filesystem / security:**
  - `symlink`
  - `junction` (Windows)
  - `swap-dir-with-symlink --after <dur>`
  - `print-env`
  - `print-argv` (conformance compares `leaves` output with the actually spawned argv)
- **Harness kill:** a test that SIGKILLs `blokky` and asserts that no leaf survives longer than the grace period (spike #1 acceptance).

**Other test layers.**
- Unit tests (parser, linter, config loader) with golden error-message tests (line/column, corrected snippets).
- Executor tests with a fake process layer and a virtual `TimeProvider`.
- Integration tests against the AOT binary on every OS.
- `examples/` as smoke fixtures.
- Parser fuzzing.
- Schema conformance.
- An agent-usage eval plan for later.

## 7. CI/CD

**`blokky-specs`:**
- docs lint;
- schema validity;
- examples and conformance cases validate against the schemas;
- reference field tables checked against the schema;
- DCO check.

**`blokky-cli`:**
- **PR checks:**
  - AOT analyzers (warnings as errors) and unit tests;
  - AOT-publish `blokky` and `blokky-testkit`;
  - check out `blokky-specs` at the pinned tag;
  - conformance and integration suites on the OS matrix (ubuntu x64/arm64, macos arm64, windows x64);
  - shell-runner tests (install `pwsh` where missing);
  - container tests on Linux only and `become` tests in a dedicated passwordless-sudo job, when those milestones arrive;
  - DCO check.
- **Release on tag:**
  - per-RID AOT binaries built natively per OS;
  - archives, SHA-256 checksums, artifact attestations, SBOM attestation;
  - GitHub Release with notes from CHANGELOG;
  - **no code signing in v0.x.**

**Both repos, hardening:**
- actions pinned by SHA;
- least-privilege `GITHUB_TOKEN`;
- a protected `release` environment with manual approval;
- tag and branch protection (required checks, reviews);
- CODEOWNERS on `.github/workflows/**`;
- Dependabot/Renovate and CodeQL;
- no `self-update` command in v0.x.

**install.md:** `gh attestation verify` is the primary verification path.

**Later:** signing (Authenticode, macOS notarization) and packaging (winget, Homebrew tap, dotnet tool, apt/rpm). No scheduled gc: only the user decides when to clean.

## 8. Process

Steps 1–3 are done (2026-11-05). The order from step 4 on was revised on 2026-11-05 (blokky-cli ADR 0006).

1. **Skeletons.** Create both local repos with empty files and headings, commit on `main`, and ask me before creating the GitHub repos. *(Done.)*
2. **Open questions.** Ask me the "needed for the draft" questions (§4) in batches and record them in grouped ADRs. *(Done.)*
3. **Language spec draft** (main agent, sequential, in `blokky-specs`): glossary, syntax, reference, execution model, variables, conditions, shells, containers and privilege, errors, output contract, leaves and validation, schemas; the exit-code table in `blokky-cli/docs/cli.md`. *(Done; PRs marked **Draft spec**.)*
4. **Scope cleanup and versioning.**
   - Move everything CLI- or runtime-specific from `blokky-specs` to `blokky-cli` (§0.1): the JSON contracts and their schemas, CLI-only error codes, run budget, config, hook, approval, lint rules, OS process-control mechanisms. Keep the language-level semantics, and add an abstract run-result model (`spec/results.md`) that conformance cases can assert on.
   - Drop the `v0.1`/`later` milestone tags; plans write `blokky: 1.0`.
5. **Remaining language features.** Specify every feature still sketched to the same depth as the rest (`race`, `lock`, `services`, `items`/`lines`/`matrix`, `collect`, `consent`, `zsh`/`fish`, …), asking open questions in batches. Then I tag **`1.0.0-draft.1`**.
6. **Example suite** (`blokky-specs/examples/`), for users: small examples per feature and larger comprehensive ones, together covering every feature, in canonical forms; plus the conformance case format and testkit interface (`conformance/README.md`, `testkit-interface.md`), `overview.md` and `prior-art.md`. Subagents in separate worktrees may write parts in parallel.
7. **Review**, by me and an **agent council**: independent reviewer agents with distinct lenses, for example
   - principles and minimalism: does each feature earn its place against §1.1 (small, declarative, not a scripting language)?
   - the agent as user: can an agent write correct plans first time from the docs and errors?
   - security posture: same as bash, nothing hidden, hygiene not presented as control;
   - implementability: runtime cost, portability, what can't be verified before the runtime exists;
   - spec quality: consistency, ambiguity, examples vs reference.

   Findings are verified, consolidated and decided by me; accepted ones become ADRs and a new draft (`1.0.0-draft.N`). This step replaces the former consistency pass.
8. **Conformance suite** (`blokky-specs/conformance/cases/`), each case citing the rule IDs it tests (`EM-n`, `VA-n`, …), for the features that survive the review. Its pass rate guides the CLI implementation.
9. **Runtime planning session:** the CLI's MVP feature set, the order in which features follow, CLI versioning, and which CLI docs (configuration, validation and permissions, threat model, agent docs, design docs) are needed when.
10. **Spike plan and spikes** (`blokky-cli/docs/design/spike-plan.md`): every item marked **"Provisional — verify in spike"**. Expected items:
    1. the process launcher (`posix_spawn` + `setsid` vs trampoline vs .NET 11 APIs), the **lifeline**, and tree kill per OS. Acceptance: no leaf survives a SIGKILL of blokky;
    2. Windows: graceful termination, hidden console per leaf, race-free job assignment;
    3. YamlDotNet under AOT: duplicate-key and alias behaviour, position tracking, same-line comment detection, event-parser fallback;
    4. schema shape and editor behaviour (yaml-language-server typing of unquoted scalars, incl. `default: 1.10` in `inputs`);
    5. .NET 11 `KillOnParentExit` at GA; `PDEATHSIG` from a dedicated thread;
    6. confirm that absolute executable paths bypass platform search;
    7. fd-relative safe deletion (`openat`/`unlinkat` via LibraryImport; Windows reparse-point-safe delete);
    8. pipe drain / `descendants_held_output` per OS;
    9. `RegexOptions.NonBacktracking` under AOT/trim;
    10. whether a hook `ask` overrides the runner's allow rules, and whether a rewritten tool input is shown on `ask` (Claude Code);
    11. the **`blokky __exec` trampoline as the universal launcher** inside boundaries (argv/env/script transport over a pipe, sudo/doas closing fds, the binary inside containers, script-file readability across `become`);
    12. the `pwsh -File` exit code after a failing native command;
    13. container termination via the engine CLI, privilege-boundary kill, Sudo for Windows modes.

    After I approve the plan, spikes run on unmerged `spikes/*` branches. Their findings go back into the spec draft.
11. **Incremental CLI implementation:** the MVP first, then feature by feature in the planned order, with the conformance pass rate as the guide. The spec is refined through ADRs as the runtime validates it, and becomes `1.0.0` when the runtime has validated it.

**Style:**
- Precise and concise.
- Normative language (MUST/SHOULD/MAY) in the spec and in the configuration and validation docs.
- Plain, friendly language in user docs.
- No marketing fluff.
- Every example must be something a user could actually run once the CLI supports its features; examples use canonical forms only.
