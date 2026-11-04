# Development setup

What you need to work on blokky-cli (and blokky-specs, which uses the same tooling). Linux, macOS, Windows and WSL all work. On Windows, run the `just` recipes from Git Bash or WSL: they use `sh`.

## Tools

| Tool | Used for | Install |
|---|---|---|
| git | Version control, signed commits | <https://git-scm.com/downloads> |
| [just](https://just.systems/) | Task runner | [packages](https://just.systems/man/en/packages.html) (`brew`, `winget`, `cargo`, `pipx install rust-just`) |
| [lefthook](https://lefthook.dev/) | Git hooks | [installation](https://lefthook.dev/installation/) (`brew`, `winget`, `npm`, `go install`) |
| [markdownlint-cli2](https://github.com/DavidAnson/markdownlint-cli2) | Markdown lint | `npm install -g markdownlint-cli2` |
| [yamllint](https://yamllint.readthedocs.io/) | YAML lint | `pipx install yamllint` or `uv tool install yamllint` |
| [actionlint](https://github.com/rhysd/actionlint) | Workflow lint | [install](https://github.com/rhysd/actionlint/blob/main/docs/install.md) |
| [typos](https://github.com/crate-ci/typos) | Spelling | `cargo install typos-cli`, `brew install typos-cli`, `pipx install typos` |
| [editorconfig-checker](https://github.com/editorconfig-checker/editorconfig-checker) | `.editorconfig` compliance | [installation](https://github.com/editorconfig-checker/editorconfig-checker#installation) |
| [ShellCheck](https://www.shellcheck.net/) | Shell script lint | [installing](https://github.com/koalaman/shellcheck#installing) |
| [gitleaks](https://github.com/gitleaks/gitleaks) | Secret scan in pre-commit | [installing](https://github.com/gitleaks/gitleaks#installing) |
| [gh](https://cli.github.com/) | PRs from the terminal | recommended |
| [pinact](https://github.com/suzuki-shunsuke/pinact) | Pin workflow actions to SHAs | recommended (maintainers) |
| [Claude Code](https://code.claude.com/docs/en/setup) | Agent sessions (`just claude`) | optional |
| .NET 11 SDK | Building Blokky | **needed once implementation starts**: <https://dotnet.microsoft.com/download> |

## First steps

```sh
git clone https://github.com/gergodrazsdik/blokky-cli.git
git clone https://github.com/gergodrazsdik/blokky-specs.git   # side by side
cd blokky-cli
just doctor   # lists missing tools and git settings, with install hints
just hooks    # installs the pre-commit and commit-msg hooks
just lint     # runs every linter, exactly as CI does
```

Set up commit signing and the DCO sign-off as described in [CONTRIBUTING.md](../CONTRIBUTING.md).

## Working with Claude Code

```sh
just claude
```

starts Claude Code in blokky-cli with `../blokky-specs` added and its instructions loaded. Extra arguments are passed through (`just claude --continue`). The committed `.claude/settings.json` turns on the Bash sandbox (macOS, Linux and WSL2; Linux needs `bubblewrap` and `socat`). Put personal tweaks in `.claude/settings.local.json` and `CLAUDE.local.md`; both are gitignored.

Claude Code reads `CLAUDE.md`, which imports `AGENTS.md`. Other agents read `AGENTS.md` directly.

## Local overrides

`local.just` (extra recipes), `lefthook-local.yml`, `CLAUDE.local.md` and `.claude/settings.local.json` are personal and gitignored.

<!-- CI smoke test; this PR is closed without merging. -->
