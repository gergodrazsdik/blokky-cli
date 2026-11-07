# Architecture

Status: **draft** (T-0067, 2026-11-07). Decisions: [ADR 0001](../adr/0001-baseline.md) (baseline), [ADR 0006](../adr/0006-phase-order-and-incremental-cli.md) (incremental CLI), [ADR 0007](../adr/0007-runtime-plan.md) (runtime plan), [ADR 0008](../adr/0008-spike-findings.md) (MVP spike findings). Platform process control is in [process-control.md](process-control.md), written after the launcher spikes ([spike-plan.md](spike-plan.md)).

## 1. Components

| Component | Kind | Responsibility |
|---|---|---|
| `Blokky.Core` | library | Everything a host needs: plan reading, validation, the AST, input binding, the invocation builder, the executor, the run registry, config, the result model and the JSON contracts. No console, no argv. |
| `Blokky.Cli` | executable `blokky` | The CLI host: argv parsing, command dispatch, stdout/stderr channels, exit codes ([cli.md](../cli.md)). Also hosts `blokky hook check` from 0.4. |
| MCP host | later | A second thin host over the same Core ([mcp.md](../mcp.md)). Core keeps no CLI assumptions so this host adds no logic of its own. |
| `blokky-testkit` | executable, test-only | The fake program conformance cases run ([testkit.md](testkit.md)). Never shipped. |
| Conformance runner and adapter | test project | Runs the blokky-specs suite against the AOT `blokky` binary and reports the pass rate ([validation-strategy.md](validation-strategy.md)). |

Inside `Blokky.Core`:

| Part | Does |
|---|---|
| Plan reader | Bytes in, encoding pass and character lint (BOM, UTF-8, control characters), then the YAML front end: YamlDotNet's scanner and parser behind one adapter, an event stream with positions computed by Blokky's own line map, every scalar a string ([ADR 0008](../adr/0008-spike-findings.md)). |
| AST builder | Builds typed nodes from the events. Every node keeps line and column; nothing is deserialized by reflection. |
| Validator | Schema-level checks, static rules (the spec's `RF` rules), the feature gate (§3), lint and strict mode. Collects every error instead of stopping at the first. |
| Input binder | Binds `--input` values (or MCP `inputs`) to the plan's `inputs`, applies defaults, computes `inputs_sha256`. |
| Invocation builder | The **single** renderer of what a leaf runs: program, argv, env, cwd, script file. `leaves`, `plan`, the validator request and the executor all call it ([leaves-and-validation.md](../contracts/leaves-and-validation.md), LV-3). |
| Executor | Walks the bound plan: modifier layers, conditions, captures, `finally`, cancellation and the cleanup deadline ([run-control.md](../contracts/run-control.md)). Time comes only from `TimeProvider`; processes only from `IProcessLauncher`. |
| Process control | `IProcessLauncher` and its per-OS implementations, the lifeline and safe deletion (§4). |
| Run registry | The per-user state dir: run ids (ULID), run dirs, the advisory liveness lock, the registry of runtime-owned artefacts, `clean` and `gc`. |
| Config loader | Hierarchical JSON config, precedence and the two key trust classes ([configuration.md](../configuration.md), [ADR 0003](../adr/0003-validation-config-lint.md)). |
| Result model and contracts | The run result, error objects and their serialization: report, pre-run envelope, events, `leaves`, validator protocol (§5). |

## 2. Data flow

`blokky run` follows the precedence of [cli.md](../cli.md#precedence-before-a-run-starts); every step before "start" is static and starts no process (RC-1, LV-2).

```text
argv ──► CLI parser ──────────────────────────────────────────► usage errors (2)
plan bytes ──► --expect-sha256 ───────────────────────────────► BK_SHA_MISMATCH (7)
config files ──► config loader ───────────────────────────────► BK_CONFIG (4)
plan bytes ──► encoding + character lint ──► YAML events ──► AST
           ──► schema + static rules ──► feature gate ──► lint ► BK_PLAN_*, BK_LINT (3)
AST + inputs ──► input binder ──► bound plan ─────────────────► BK_INPUT_* (2)
bound plan ──► approval checks (0.4) ─────────────────────────► BK_APPROVAL_* (5)
   ── pre-run envelope on any failure above (OC-11) ──
bound plan ──► run registry: run id, run dir, lock, events.jsonl
           ──► executor ──► invocation builder ──► IProcessLauncher ──► leaves
           ──► run result ──► report.json (atomic) ──► stdout report (truncated, OC-22) ──► exit code
```

The static commands share the front half: `validate`, `lint`, `plan` and `leaves` stop after the bound plan (or after the AST when inputs are missing) and render it. `hook check` (0.4) runs the same front half on the command line it parses out of the runner's tool input, with the same argv parser as `run`.

## 3. Feature-support gate

Each release supports a subset of the language ([ROADMAP.md](../../ROADMAP.md)). Core holds two tables of feature ids, using the pinned vocabulary of the conformance suite (blokky-specs `conformance/README.md`, CF-12): **validated** (the validator fully checks the feature) and **runnable** (the run engine executes it), runnable always a subset of validated ([ADR 0009](../adr/0009-validator-delivery.md)). `validate` gates on validated, `run` on runnable; a release requires both to be equal for its features.

- The validator derives the feature ids a plan uses from the AST. Any unsupported id is a plan error `BK_PLAN_NOT_SUPPORTED` with `detail.feature` and the node's position, reported with every other plan error (exit 3). No process starts.
- The check runs after schema and static rules, so a plan that is invalid for other reasons gets those errors too.
- The conformance adapter declares support from the same tables (CF-54): validated for validation cases, runnable for run cases, so the runner's skips and the binary's behaviour can't drift.
- The table is per build, not per config: no flag or config key enables an unsupported feature.

0.1's table is ADR 0007's MVP: the full validator; `exec`, `exec-string`, `script` with `shell-bash`, `shell-sh`, `shell-pwsh`; the sequential kinds, modifiers and plan-level features it lists; conditions without probes.

## 4. Launcher abstraction

`IProcessLauncher` is the only way Core starts a process. It is designed for Linux, macOS and Windows from the start, although 0.1 implements only POSIX on Linux (ADR 0007).

Shape (names indicative; final in [process-control.md](process-control.md)):

- **Input:** a resolved launch spec from the invocation builder: absolute program path, argv, the complete environment, cwd, stdin source (null device, pipe or file), output pipes.
- **Output:** a handle to the leaf's process tree: pid, exit (code or signal), stdout/stderr streams, and three operations: graceful stop (SIGTERM / CTRL_BREAK), forced kill of the tree (SIGKILL to the group / `TerminateJobObject`), and "are descendants still holding output" (RC-5).
- **Lifeline:** a per-run component that guarantees no leaf tree outlives `blokky`, including after SIGKILL (RC-6).
- **Implementations:** POSIX (Linux in 0.1, macOS later) and Windows (0.6). Every OS-specific call goes through `LibraryImport` ([tech-stack.md](tech-stack.md#6-platform-calls)).

On Linux the spikes settled the mechanism ([ADR 0008](../adr/0008-spike-findings.md), [process-control.md](process-control.md)): `posix_spawn` with `POSIX_SPAWN_SETSID` (no trampoline, no .NET `Process` for leaves), a per-run watchdog as the lifeline, and drain before the tree stop. Windows jobs and consoles (spike 2) and macOS (spike M) are pending. The executor tests use a fake launcher, so the executor doesn't depend on the OS mechanism.

## 5. Errors and results

- **Error codes** come from two catalogues: the language's (blokky-specs `spec/errors.md`: `BK_PLAN_*`, `BK_INPUT_*`, node errors) and the CLI's ([contracts/errors.md](../contracts/errors.md)). Core models each error as code, message, node, position, fix, detail, causes and context, the error object of [output-contract.md](../contracts/output-contract.md) §5.
- **Phases** map to exit codes in one place ([contracts/errors.md](../contracts/errors.md) §1); hosts don't pick exit codes themselves. `BK_INTERNAL` (70) wraps any unexpected exception after the run started, and the report is still written (RC-2).
- **Pre-run failures** produce the pre-run envelope (OC-11 to OC-13): every error of the failing step, no run id.
- **Run results** are the language's abstract run result (blokky-specs `spec/results.md`) plus the CLI's run-level fields. The report maps `stopped` to `budget_exceeded` or `interrupted` by cause (run-control.md §3).
- **The report must carry everything the conformance result document needs**, because the adapter may only map, never infer (CF-2). A field the adapter lacks is a report gap, fixed in the contract, not in the adapter.
- **Messages** are not a contract; codes, `detail` and positions are.
- Every JSON contract is a source-generated `System.Text.Json` context with its own `schema_version` (OC-24).

## 6. Dependencies between parts

- Core depends on no host. Hosts depend on Core and add only I/O.
- The executor depends on interfaces (`IProcessLauncher`, `TimeProvider`, the run registry), so unit tests run it with fakes.
- The invocation builder depends only on the bound plan and the resolved environment; it starts nothing.
- Platform code is isolated behind `IProcessLauncher` and a small set of file-system helpers (fd-relative creation and safe deletion of run-owned files, from spike 7; the advisory lock).
