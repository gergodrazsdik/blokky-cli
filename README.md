# Blokky

Blokky is a small, declarative YAML block language and runner that wraps shell commands in common structured-programming blocks: timeouts, retries, polling loops, parallel groups, temp directories and cleanup. Its main users are coding agents (Claude Code, Codex and others), and secondarily the humans who review what those agents run. Control flow and resource lifetime move into a reviewable declarative layer, the leaf commands stay simple and single-purpose, and the runtime owns and cleans up its own artefacts. Blokky is not a sandbox: it makes what will run visible and easy to validate, and never does more than the equivalent bash script could.

> **Status: pre-alpha, specification phase. There is nothing to install yet.**

## Two repositories

| Repo | Contents |
|---|---|
| [blokky-specs](https://github.com/gergodrazsdik/blokky-specs) | The language specification, JSON contracts and schemas, examples and the conformance suite. Versioned on its own; a plan's `blokky:` field names a spec version. |
| **blokky-cli** (this repo) | The .NET implementation (CLI host, later an MCP host), configuration, validation and hook integration, threat model, agent docs, install docs, design docs and CI/CD. Each release declares the spec versions it supports. |

A change that touches both repos gets two linked pull requests; the spec PR merges first.

## Where to look

- [ROADMAP.md](ROADMAP.md): phases and planned features.
- [docs/design/brief.md](docs/design/brief.md): the design brief that drives the current phase.
- [docs/plan/TRACKER.md](docs/plan/TRACKER.md): detailed work tracker for both repos.
- [CONTRIBUTING.md](CONTRIBUTING.md): how to contribute (DCO sign-off, signed commits).
- [SECURITY.md](SECURITY.md): how to report a vulnerability.
- [docs/dev-setup.md](docs/dev-setup.md): tools for working on the repo.

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Contributions are accepted under the [Developer Certificate of Origin](https://developercertificate.org/) (no CLA).
