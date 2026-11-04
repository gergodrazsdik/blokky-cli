# 0001: Implementation, distribution and process baseline

- Status: accepted
- Date: 2026-11-04
- Questions: none answered; partly settles Q-q (repo names, license)

## Context and problem statement

The [specification-phase brief](../design/brief.md) (§0, §1, §2) sets the current direction for the Blokky implementation and the project process before any design doc or code is written. Later ADRs answer the brief's open questions (§4) batch by batch, and they need a recorded starting point to refine or supersede.

This ADR records the **implementation, CLI, configuration, distribution and process** baseline. Language decisions are in [blokky-specs ADR 0001](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0001-language-baseline.md).

Baseline decisions are the current direction, not frozen (brief §1.5): everything can change until v1.0.0, and changing a baseline decision needs the maintainer's approval and a superseding ADR.

## Considered options

1. One grouped baseline ADR per repo that summarizes the brief's baseline and links to it.
2. One ADR per baseline decision.
3. No ADR; treat the brief as the record.

## Decision outcome

Chosen option: "one grouped baseline ADR per repo", because the brief asks for grouped ADRs (§8 step 2), per-decision files would add dozens of near-empty records, and the brief is a phase document that later ADRs can't supersede piece by piece.

The implementation and process baseline:

### Repositories and licensing

- Two repos (brief §0.1): **`blokky-specs`** (language spec, normative JSON contracts and schemas, examples, conformance suite including the testkit *interface*) and **`blokky-cli`** (.NET implementation, testkit implementation, configuration, validation and hook integration, threat model, agent docs, install, design docs, CI/CD).
- Each has its own semver, branches, PRs, ADRs and CHANGELOG. Each blokky-cli release declares the spec versions it supports.
- A change touching both repos gets two linked PRs; the spec PR merges first.
- blokky-cli CI runs the conformance suite from blokky-specs at a pinned tag (submodule vs explicit checkout: Q-q, decided in `ci-cd.md`).
- **License: Apache-2.0 with DCO sign-off, no CLA,** in both repos. Whether the spec text should use CC BY 4.0 instead is still open (Q-q).

### Runtime and parsing

- **.NET 11; Native AOT is a hard requirement** (brief §2). AOT and trim warnings are build errors. Integration and conformance tests run against the published AOT binary.
- Where the BCL can't deliver the required semantics, Blokky calls platform APIs directly with AOT-safe `LibraryImport` P/Invoke (`posix_spawn`/`setsid`, Job Objects, signals, console control events) (brief §1.4).
- Plans are parsed by **walking the YAML node tree** (YamlDotNet representation model or event parser); no reflection-based POCO deserialization. Every scalar is read as a string, and every AST node keeps its line and column.
- JSON contracts (report, event log, `leaves` output, validator protocol, config) use source-generated `System.Text.Json` contexts and each carry their own `schema_version`.
- The executor's clock is .NET `TimeProvider`, which is also the virtual clock in tests.

### Architecture

- A **`Blokky.Core`** library (parser, AST, linter, executor, config, result model) with thin hosts: the CLI now, an MCP host later.
- An **`IProcessLauncher`** boundary isolates platform process control.
- A **single invocation builder** renders exactly what `leaves` and `blokky plan` show and what is executed, from the bound plan inputs.
- **`exec` is the primitive:** a `script` leaf compiles to `exec: [<shell>, <flags>, <scriptfile>]`; containers and `become` (later) wrap that one shape. Whether a `blokky __exec` trampoline becomes the universal launcher is a spike question (brief §8.4).

### CLI

- The CLI parser allows one spelling per option, no short aliases for data flags, no response files (`@file`), no option bundling and no alternative value delimiters (`--opt=v`, `--opt:v`). `hook check` and `blokky run` share one argv parser.
- Flags come in three classes: tightening (`--max-duration`, `--expect-sha256`), neutral (`-v`, `--cleanup`) and data (`--input`).
- **Flags and env vars cannot set user-only config keys or relax lint/strict severities.** This is a design rule, not a security control; the hook pins the command line.

### Configuration and validation

- Hierarchical JSONC config files (user, repo, repo-local) with **two key trust classes**, user-only and free; a non-user file that sets a user-only key is a config error (brief §3.4). File names and the full list of user-only keys: Q11.
- Blokky does not decide what is allowed. It exposes everything that will execute (`leaves`, the validator request) and supports an **optional validator plugin**, configured only in user-level config, never by a CLI argument or env var (brief §2, §3.9).
- `blokky hook check` integrates with the agent runner's permission system (Claude Code in v0.1). It never passes through: when the validator or runner-rules mode doesn't allow, it emits an explicit `ask` or `deny`. The docs say never to allowlist `blokky` by prefix.
- Strict mode is configurable lint that pushes logic into blocks. It is hygiene, not enforcement, and doesn't blanket-ban commands like `rm`.

### Process control

- Leaves run in their own session (`setsid`) on Unix and their own Job Object on Windows (brief §2, §3.8).
- Timeouts and cancellation kill the whole process tree, graceful then forced, with a configurable grace period.
- **All descendants die no later than run end, including when Blokky itself is SIGKILLed** (lifeline). Processes that deliberately escape (double-fork daemons, Windows breakaway) are handled on a best-effort basis, and the docs say so.
- Mechanisms (`posix_spawn` vs trampoline, per-leaf lifeline vs per-run watchdog, Windows console and job handling) are spike #1 and #2 (Q-e).

### Run artefacts and cleanup

- Every run gets a unique, sortable run id. All of a run's artefacts (report, event log, logs, capture files, tempdirs, later stopped containers) are registered under it in a per-user state directory (brief §1.3).
- **Artefacts are kept by default.** `blokky clean <run-id>`, `blokky run --cleanup on-success|always` and `blokky gc` remove them; nothing sweeps automatically and there is no scheduled gc.
- "In progress" is determined by an advisory lock held by the run, plus boot id and hostname. Deletion is fd-relative, doesn't follow links, doesn't cross filesystems, stays inside the registered roots and touches only the invoking user's runs.

### Distribution and platforms

- **v0.x distribution:** GitHub Releases with per-RID AOT binaries, SHA-256 checksums, artifact attestations and an SBOM attestation; **unsigned** (brief §2, §7). Code signing and packaging (winget, Homebrew, dotnet tool, apt/rpm) come later. There is no `self-update` command in v0.x.
- **Target platforms:** linux-x64, linux-arm64, osx-arm64, win-x64. WSL counts as Linux.
- Binary name: `blokky`.

### Process

- **Current phase: specification** (brief §8). No implementation code; spike code only on unmerged `spikes/*` branches after the maintainer approves the spike plan. Implementation starts after the finalized spec is approved.
- Order: open questions answered in batches and recorded in grouped ADRs → language spec draft (sequential, main agent) → spike plan → spikes → fan-out docs → consistency pass → finalized spec.
- Work happens on feature branches (`docs/<topic>`, `spikes/<topic>`) with PRs; the maintainer reviews and merges. Commits use Conventional Commits, a DCO sign-off and a signature; tags are annotated and signed, and release tags are the maintainer's.
- Parallel work uses subagents, each with its own git worktree, branch, PR and explicit file ownership. Shared files are owned by the main agent. No fan-out before the language spec draft is merged.
- The single work tracker for both repos is [`docs/plan/TRACKER.md`](../plan/TRACKER.md).

### Consequences

- Good: design docs, the spike plan and later ADRs start from a recorded, linkable baseline.
- Good: each open-question ADR states which baseline item it refines.
- Neutral: details stay in the brief and, once written, in the design docs; this ADR only summarizes the direction.
- Bad: two places describe the baseline until the design docs exist. Once they are written, they are authoritative for the details and this ADR records only the decisions.

## More information

- Brief: §0 (working agreement), §1.3–§1.5, §2 (baseline decisions), §3.4, §3.8, §3.9, §7 (CI/CD), §8 (process).
- Open questions that refine this baseline: [tracker](../plan/TRACKER.md#open-questions), batches T-0008 to T-0013.
- Companion: [blokky-specs ADR 0001](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0001-language-baseline.md) (language).
