# 0007: Runtime plan: MVP, release order, docs and test-first build

- Status: accepted
- Date: 2026-11-07
- Questions: the runtime planning session (brief §8 step 9, tracker T-0053)
- Builds on: [ADR 0006](0006-phase-order-and-incremental-cli.md)

## Context and problem statement

The language spec is at `1.0.0-draft.5`. Its conformance suite has 1,147 cases covering all 380 rules, and implementations skip what they don't support (`BK_PLAN_NOT_SUPPORTED`, CF-12). ADR 0006 left four things for this session:

- the CLI's MVP;
- the order in which features follow;
- CLI versioning and releases;
- when the remaining CLI docs are written.

The maintainer answered them as a questionnaire on 2026-11-07, with a recommendation for each question.

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| MVP language scope | **the full validator plus sequential execution**; validator only; a broader MVP with groups and repetition |
| MVP platforms | Linux and macOS, Windows next; all three at once; **Linux only first** |
| How agents reach Blokky in the MVP | **the CLI plus agent docs**; the CLI plus the Claude Code hook; the CLI, hook and MCP host |
| Spikes relative to the MVP | **the spikes the MVP depends on first, the rest before their feature's release**; every spike first |
| Order after the MVP | **value first**; platforms early; risk first |
| Releases | **GitHub Releases with Native AOT binaries, public from 0.1 once its conformance subset passes**; also a dotnet tool; private until 0.3 |
| When the CLI docs are written | **as proposed (design docs before code, user and agent docs with the release that needs them)**; threat model first |
| How the suite drives the build | **the testkit and runner first, then test-first**; the runner alongside |

## Decision outcome

### The MVP (0.1)

- **Validation:** the complete validator. Every plan and input error is covered by the suite's validation cases.
- **Execution:** sequential plans:
  - `exec` (array and string form), `script` with `bash`, `sh` and `pwsh`;
  - `do`, `if`, `log`, `fail`, `assert`, `set`, `return`;
  - `when`, `timeout`, `cwd`, `env`, `export`, `finally`, `ignore_errors`;
  - `capture` and field captures, `inputs`, `vars`, `outputs`;
  - conditions without probes.
- **Not in 0.1:** `all`, `any` and `race`; repetition; probes; `services`; `lock`; `tempdir`; `container`. These fail validation with `BK_PLAN_NOT_SUPPORTED`.
- **Platform:** Linux only (x64 and arm64). macOS and Windows follow in 0.6. The launcher abstraction is still designed for them, so the port doesn't reshape it.
- **Host:** the CLI (`validate`, `run`, the JSON report and exit codes), with the agent docs (agent guide, skill) so agents call it through their shell tool.

### Release order

Each step is a 0.x minor release, published once the conformance subset for its features passes.

| Release | Adds |
|---|---|
| 0.1 | The validator and sequential execution (Linux) |
| 0.2 | Repetition (`retry`, `while`, `until`, `for` with `collect`, `carry` and `index`) and probes |
| 0.3 | `all`, `any`, `race`, `tempdir`, `lock` |
| 0.4 | The Claude Code hook and approval |
| 0.5 | `services` |
| 0.6 | macOS and Windows (process-control port, `powershell`) |
| 0.7 | `container` |
| later | The MCP host |

A platform port moves earlier if a user needs it.

### Spikes

Before MVP code, only the spikes the MVP depends on run (brief §8 spike list):

- 1: launcher, lifeline and tree kill on POSIX;
- 3: YamlDotNet under AOT;
- 4: schema and editor behaviour;
- 5: `KillOnParentExit` / `PDEATHSIG`;
- 6: absolute executable paths;
- 7: safe deletion;
- 9: the regex subset on `NonBacktracking`.

Every other spike runs before the release that brings its feature:

- 2: Windows;
- 8: drain per OS;
- 11 and 13: containers;
- the joins and engine-matrix spikes;
- the PowerShell spikes.

### Releases

- **Builds:** Native AOT single-file binaries per RID (`linux-x64` and `linux-arm64` in 0.1) on GitHub Releases, with SHA-256 checksums. Attestations and the SBOM follow the CI/CD design (T-0040, T-0041).
- **Tags:** `cli-v0.x.y`, signed and pushed by the maintainer.
- **Requirements:** each release lists the spec version it implements and its feature matrix, and publishes its conformance pass rate.
- **Packaging:** no package managers until later.

### Docs

| When | Docs |
|---|---|
| Before MVP code | Design docs refreshed: architecture, process control, tech stack, testkit, validation strategy, CI/CD |
| With 0.1 | `configuration.md`, `cli.md`, the contracts, agent docs (`agent-guide.md`, `SKILL.md`), README and install |
| Before 0.4 (hook) | `validation-and-permissions.md`, the threat model |
| With the MCP host | `mcp.md`, the tool description |

### Test-first

The first CLI work is `blokky-testkit` (Native AOT) and the conformance runner with its adapter, all in blokky-cli and run in CI on Linux. Then the validator is built until the suite's validation cases pass, and then the run engine until the 0.1 subset passes.

### Consequences

- **Good:** 0.1 is small, fully tested against the suite, and useful to agents on Linux at once. Every later release has a clear conformance target.
- **Good:** the spikes that only matter later don't hold up the MVP.
- **Neutral:** macOS and Windows users wait until 0.6, unless the order changes.
- **Bad:** POSIX-only process control first carries a risk that the Windows port reshapes the launcher. Designing the launcher abstraction for all three OSes up front, and the spike 2 design review, contain that risk.

## More information

- Brief §8 step 9 (marked done) and step 10 (the spike order).
- ROADMAP.md: the release plan.
- Tracker: T-0053 done; phase 5 tasks added.
