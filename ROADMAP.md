# Roadmap: blokky-cli

This is the high-level picture. Task-level detail lives in the [work tracker](docs/plan/TRACKER.md). Language features and their milestones are in the [blokky-specs roadmap](https://github.com/gergodrazsdik/blokky-specs/blob/main/ROADMAP.md).

## Vision

Coding agents keep writing fragile ad-hoc shell for timeouts, retries, polling, temp directories and cleanup. Blokky moves that control flow into a small declarative YAML layer that a human or a classifier can review at a glance, runs it with deterministic, structured output sized for an agent's context, and owns the artefacts a run leaves behind. It never does more than the equivalent bash script could, and it makes everything that will run visible to the agent runner's permission system.

## Phases

1. **Specification draft.** The complete language spec in blokky-specs, every planned feature tagged `v0.1` or `later`, plus the CLI exit codes. *(current)*
2. **Spikes.** Verify the runtime behaviour the spec depends on: process launch and tree kill per OS, the lifeline that stops leaves when Blokky is killed, Windows job objects and graceful termination, YamlDotNet under Native AOT, safe deletion, and how Claude Code hooks interact with allow rules.
3. **Finalized spec.** Fold the spike findings back in; resolve or re-scope every provisional item.
4. **v0.1 implementation.** The .NET 11 Native AOT CLI.
5. **MCP host.** A second host next to the CLI, with read-only `plan`/`leaves` tools and a `run` tool.
6. **Signing and packaging.** Code signing and native packages.

## v0.1: implementation

- `Blokky.Core` (parser, AST, linter, executor, config, result model) with a thin CLI host.
- Commands: `run` (stdin heredoc or file, `--input`, `--cleanup`, `--max-duration`, `--expect-sha256`), `plan`, `leaves`, `validate`, `lint`, `report`, `clean`, `gc`, `config show`, `schema` (including the agent cheat sheet), `hook check` for Claude Code, `version`.
- Process control: own session or Job Object per leaf, whole-tree timeouts and cancellation, no leaf outliving the run even when Blokky is killed.
- Run registry and runtime-owned artefacts with `clean`/`gc`.
- Hierarchical JSONC configuration with user-only and free keys; optional validator plugin; runner-rules mode for the hook.
- Shells: `bash`, `sh`, `pwsh`, Windows PowerShell 5.1.
- Testkit and conformance runs against the published AOT binary on linux-x64, linux-arm64, osx-arm64 and win-x64.

## v0.1: distribution

- GitHub Releases with per-RID Native AOT binaries, SHA-256 checksums, artifact attestations and an SBOM attestation (`gh attestation verify` as the primary check).
- Unsigned binaries in 0.x; no `self-update` command.
- Install docs per platform, including macOS quarantine handling.

## Later

- MCP host.
- Language features tagged `later` in the spec: containers, `become`, services, locks, `race`, `matrix`, `for` over captured output, `collect`, `zsh` and `fish`.
- Out-of-band OS consent (UAC through Sudo for Windows, askpass, polkit).
- Code signing (Authenticode, macOS notarization).
- Packaging: winget, Homebrew tap, dotnet tool, apt/rpm.
