# Roadmap: blokky-cli

This is the high-level picture. Task-level detail lives in the [work tracker](docs/plan/TRACKER.md). Language features and their milestones are in the [blokky-specs roadmap](https://github.com/gergodrazsdik/blokky-specs/blob/main/ROADMAP.md).

## Vision

Coding agents keep writing fragile ad-hoc shell for timeouts, retries, polling, temp directories and cleanup. Blokky moves that control flow into a small declarative YAML layer that a human or a classifier can review at a glance, runs it with deterministic, structured output sized for an agent's context, and owns the artefacts a run leaves behind. It never does more than the equivalent bash script could, and it makes everything that will run visible to the agent runner's permission system.

## Phases

1. **Language specification** in blokky-specs: the complete language as `1.0.0-draft.N`, an example suite, a review, and a conformance suite covering every rule. *(done: `1.0.0-draft.5`)*
2. **Runtime planning.** MVP, release order, docs and a test-first build ([ADR 0007](docs/adr/0007-runtime-plan.md)). *(done)*
3. **Spikes** for what the MVP depends on: the POSIX launcher, lifeline and tree kill; YamlDotNet under AOT; schema and editor behaviour; parent-death handling; safe deletion; the regex subset on `NonBacktracking`. The other spikes run before the release that needs them. *(current)*
4. **Incremental CLI**, test-first against the conformance suite: `blokky-testkit` and the conformance runner first, then the validator, then the run engine. Each release below ships when its conformance subset passes.
5. **`1.0.0`** of the language once the runtime has validated the draft.

## Releases

| Release | Adds |
|---|---|
| 0.1 | The full validator and sequential execution (`exec`, `script` with bash/sh/pwsh, `do`, `if`, `log`, `fail`, `assert`, `set`, `return`, `when`, `timeout`, `cwd`, `env`, `export`, `finally`, `ignore_errors`, captures, inputs, vars, outputs, conditions without probes) on Linux; the CLI with agent docs |
| 0.2 | Repetition (`retry`, `while`, `until`, `for`, `collect`, `carry`, `index`) and probes |
| 0.3 | `all`, `any`, `race`, `tempdir`, `lock` |
| 0.4 | The Claude Code hook and approval |
| 0.5 | `services` |
| 0.6 | macOS and Windows |
| 0.7 | `container` |
| later | The MCP host |

Features a release doesn't support fail validation with "not supported by this blokky version" (`BK_PLAN_NOT_SUPPORTED`). Each release lists the spec version it implements, its feature matrix and its conformance pass rate.

## Command set

`run` (stdin heredoc or file, `--input`, `--cleanup`, `--max-duration`, `--expect-sha256`), `plan`, `leaves`, `validate`, `lint`, `report`, `clean`, `gc`, `config show`, `schema`, as specified in [cli.md](docs/cli.md); which commands 0.1 ships is decided when its docs are written.

## Distribution

- GitHub Releases with per-RID Native AOT binaries (`linux-x64` and `linux-arm64` from 0.1; `osx-arm64` and `win-x64` from 0.6), SHA-256 checksums, artifact attestations and an SBOM attestation (`gh attestation verify` as the primary check).
- Unsigned binaries in 0.x; no `self-update` command.
- Install docs per platform, including macOS quarantine handling.

## Later

- Out-of-band OS consent (UAC through Sudo for Windows, askpass, polkit).
- Code signing (Authenticode, macOS notarization).
- Packaging: winget, Homebrew tap, dotnet tool, apt/rpm.
