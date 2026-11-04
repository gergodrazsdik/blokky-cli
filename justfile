# Blokky (blokky-cli) task runner. `just` lists the recipes.
# On Windows, run recipes from Git Bash or WSL (they use sh).

import? 'local.just'

# List the recipes
default:
    @just --list --unsorted

# Check that the required tools are installed and print install hints
[group('meta')]
doctor:
    #!/bin/sh
    missing=0
    check() {
      if command -v "$1" >/dev/null 2>&1; then
        printf '  ok       %s\n' "$1"
      else
        printf '  MISSING  %-22s %s\n' "$1" "$2"
        missing=1
      fi
    }
    echo "Required:"
    check git                  "https://git-scm.com/downloads"
    check just                 "https://just.systems/man/en/packages.html"
    check lefthook             "https://lefthook.dev/installation/"
    check markdownlint-cli2    "npm install -g markdownlint-cli2"
    check yamllint             "pipx install yamllint (or uv tool install yamllint)"
    check actionlint           "https://github.com/rhysd/actionlint/blob/main/docs/install.md"
    check typos                "cargo install typos-cli, or https://github.com/crate-ci/typos#install"
    check editorconfig-checker "https://github.com/editorconfig-checker/editorconfig-checker#installation"
    check shellcheck           "https://github.com/koalaman/shellcheck#installing"
    check gitleaks             "https://github.com/gitleaks/gitleaks#installing"
    echo "Recommended:"
    check gh                   "https://cli.github.com/"
    check pinact               "https://github.com/suzuki-shunsuke/pinact#install"
    check claude               "https://code.claude.com/docs/en/setup"
    echo "Later (implementation phase):"
    check dotnet               ".NET 11 SDK: https://dotnet.microsoft.com/download"
    echo "Git settings:"
    if [ "$(git config --bool commit.gpgSign || echo false)" = true ]; then
      echo "  ok       commit.gpgSign"
    else
      echo "  MISSING  commit.gpgSign         git config commit.gpgSign true (see CONTRIBUTING.md)"
      missing=1
    fi
    if [ "$(git config --bool tag.gpgSign || echo false)" = true ]; then
      echo "  ok       tag.gpgSign"
    else
      echo "  MISSING  tag.gpgSign            git config tag.gpgSign true"
      missing=1
    fi
    if [ -f "$(git rev-parse --git-path hooks)/commit-msg" ]; then
      echo "  ok       git hooks installed"
    else
      echo "  MISSING  git hooks              just hooks"
      missing=1
    fi
    exit "$missing"

# Install the git hooks (lefthook)
[group('dev')]
hooks:
    lefthook install

# Run all linters
[group('lint')]
lint: lint-md lint-yaml lint-actions lint-sh typos editorconfig

# Lint Markdown (markdownlint-cli2)
[group('lint')]
lint-md:
    git ls-files -z '*.md' | xargs -0 scripts/with-tool.sh markdownlint-cli2

# Lint YAML (yamllint)
[group('lint')]
lint-yaml:
    scripts/with-tool.sh yamllint --strict .

# Lint GitHub Actions workflows (actionlint)
[group('lint')]
lint-actions:
    scripts/with-tool.sh actionlint

# Lint shell scripts (shellcheck)
[group('lint')]
lint-sh:
    git ls-files -z '*.sh' | xargs -0 scripts/with-tool.sh shellcheck

# Check spelling (typos)
[group('lint')]
typos:
    scripts/with-tool.sh typos

# Check files against .editorconfig (editorconfig-checker)
[group('lint')]
editorconfig:
    git ls-files -z | xargs -0 scripts/with-tool.sh editorconfig-checker

# Pin GitHub Actions to full commit SHAs (pinact)
[group('meta')]
pin:
    pinact run

# Start Claude Code with blokky-specs added (expects ../blokky-specs)
[group('dev')]
claude *args:
    CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1 claude \
      --add-dir ../blokky-specs {{ args }}
