# Contributing to Blokky

Thanks for your interest. Blokky lives in two repositories, [blokky-specs](https://github.com/gergodrazsdik/blokky-specs) (the language) and [blokky-cli](https://github.com/gergodrazsdik/blokky-cli) (the implementation), and both follow the rules below.

> **The CLI is being implemented incrementally** ([ADR 0007](docs/adr/0007-runtime-plan.md)). The spec is pre-1.0, and syntax, semantics and every design decision may still change during 0.x. Nothing is released yet.

## Questions and discussion

- Questions about the plan language: open a "Spec question" issue in blokky-specs.
- Questions about the CLI, the docs or the tooling: open an issue in blokky-cli.
- Security problems: never in a public issue. See [SECURITY.md](SECURITY.md).

## Developer Certificate of Origin (DCO)

Every commit must carry a sign-off line:

```text
Signed-off-by: Your Name <you@example.com>
```

`git commit -s` adds it, using your `user.name` and `user.email`. By signing off you certify the [Developer Certificate of Origin 1.1](https://developercertificate.org/): in short, that you wrote the change or otherwise have the right to submit it under the project's open-source license (Apache-2.0). There is no CLA.

The sign-off email must match the commit author's email; CI checks every commit of a pull request. To fix missing sign-offs on a branch, run `git rebase --signoff main` and force-push the branch.

## Signed commits and tags

**`main` only accepts signed commits.** GitHub must show each commit in your pull request as "Verified". GPG, SSH and S/MIME signing keys all work:

- [About commit signature verification](https://docs.github.com/en/authentication/managing-commit-signature-verification/about-commit-signature-verification)
- [Telling Git about your signing key](https://docs.github.com/en/authentication/managing-commit-signature-verification/telling-git-about-your-signing-key) (GPG, SSH or S/MIME)
- Upload the public key to your GitHub account as a **signing** key.

Then enable signing for this repo (or globally with `--global`):

```sh
git config commit.gpgSign true
git config tag.gpgSign true
# For SSH keys additionally:
# git config gpg.format ssh
# git config user.signingKey ~/.ssh/id_ed25519.pub
```

Release tags are annotated and signed (`git tag -s vX.Y.Z -m "vX.Y.Z"`) and are created only by the maintainer. GitHub's tag rules protect `v*` tags from deletion and updates, but they can't check a tag object's own signature, so verify a release tag with `git verify-tag`.

## Commit messages

We use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/): `<type>[(scope)][!]: <summary>`, with type one of `build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`, `style`, `test`. Examples:

```text
docs(spec): define retry backoff defaults
chore: add editorconfig-checker to the lint recipe
```

Keep commits small and logical. The `commit-msg` hook checks the header and the sign-off.

## Branches and pull requests

- Never commit directly to `main`. Work on a feature branch: `feat/<topic>` for code (with tests), `docs/<topic>` for documentation and spec work, `spikes/<topic>` for spikes (never merged).
- Open a pull request. Its description gives a short summary, the decisions it applies (link the ADRs), the tracker IDs and any open items.
- PRs are **squash-merged** by the maintainer. The squash commit keeps your commits' messages, including their `Signed-off-by` lines. Keep the PR title a valid Conventional Commit header; it becomes the squash commit's title.
- A change that touches both repos gets two linked PRs; the spec PR merges first.
- After a PR merges, rebase your remaining branches onto `main`.

## Local setup: just and lefthook

You need [just](https://just.systems/) and [lefthook](https://lefthook.dev/), plus the linters listed by `just doctor`. See [docs/dev-setup.md](docs/dev-setup.md) for install pointers.

```sh
just doctor   # check tools and git settings, with install hints
just hooks    # install the git hooks (pre-commit and commit-msg)
just lint     # run every linter, as CI does
```

The hooks skip a check whose tool is missing and print a clear message. CI runs every check with all tools installed.

### Local overrides

These files are personal and gitignored:

| File | Purpose |
|---|---|
| `local.just` | Extra recipes, imported by the justfile if present. |
| `lefthook-local.yml` | Personal hook overrides ([lefthook docs](https://lefthook.dev/configuration/)). |
| `CLAUDE.local.md` | Personal notes for Claude Code. |
| `.claude/settings.local.json` | Personal Claude Code settings. |

## AI-assisted contributions

Contributions written with AI coding agents are welcome; much of this project is built that way. You are responsible for what you submit: review it, understand it, and make sure it is correct. Your DCO sign-off covers AI-assisted changes exactly like any other change. Agent instructions for this repo are in [AGENTS.md](AGENTS.md).

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating you agree to uphold it.
