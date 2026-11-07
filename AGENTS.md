# Agent instructions: blokky-cli

These instructions are for coding agents (Claude Code, Codex and others) working in this repo. Humans: see [CONTRIBUTING.md](CONTRIBUTING.md).

## This repo

blokky-cli holds the .NET implementation of Blokky (CLI host, later an MCP host), configuration, validation and hook integration, the threat model, agent docs, install docs, design docs and CI/CD. Its sibling **blokky-specs** holds the language specification, JSON contracts, schemas, examples and the conformance suite. Expect both repos checked out side by side (`../blokky-specs`). Each has its own branches, PRs, ADRs and CHANGELOG. A change touching both gets two linked PRs; the spec PR merges first.

## Brief and process

[docs/design/brief.md](docs/design/brief.md) is the source of truth for the current phase. Read it before any non-trivial work. Its §4 open questions are asked, not decided silently; its §8 defines the process.

**Current phase: incremental CLI implementation** ([ADR 0007](docs/adr/0007-runtime-plan.md); brief §8 step 11). Production code lands on `feat/<topic>` branches, always with tests; docs-only changes use `docs/<topic>`. Spike code stays throwaway on unmerged `spikes/<topic>` branches, after the maintainer approves the spike plan. The spec in blokky-specs stays a draft and is refined through ADRs as the runtime validates it.

## Working agreement

- Work on feature branches (`feat/<topic>` for code, `docs/<topic>` for docs, spikes `spikes/<topic>`). **Never commit to `main`.**
- Push branches and open PRs with `gh`. The PR description gives a summary, the decisions it applies (link ADRs), tracker IDs and open items. **Never merge PRs**; the maintainer reviews and merges.
- Conventional Commits (`feat: …`, `fix: …`, `docs: …`, `chore: …`); small, logical commits.
- **DCO and signatures:** always `git commit -s`, with commit signing enabled; never disable signing or pass `--no-verify`. Tags are annotated and signed (`git tag -s`), and release tags are the maintainer's. Write the commit message to a file outside the repo and pass it with `git commit -s -F <file>`.
- After a PR merges, rebase remaining branches onto `main` (for a stack: `gh stack sync`, below).

## Stacked PRs

When one change builds on another that isn't merged yet, open the PRs as a native GitHub stack with the `gh stack` extension ([about stacked PRs](https://docs.github.com/en/pull-requests/get-started/about-stacked-prs), [commands](https://docs.github.com/en/pull-requests/reference/stacked-prs-cli-commands)), not as PRs based on each other's branches. GitHub then retargets and rebases the rest of the stack itself when a PR merges, so a PR is never closed because its base branch was deleted.

- Create the branches with `gh stack init <branch>…` (or `gh stack add <branch>` on the top branch), and commit to each as usual.
- Publish with `gh stack submit --auto --open` (without `--auto` it opens an interactive editor), then give each PR its real title and description with `gh pr edit`.
- The maintainer merges bottom-up (merging a PR also merges the ones below it). Afterwards, `gh stack sync --prune` updates trunk and the remaining branches locally and deletes the merged ones.
- `sync` and `rebase` may force-push the stack's branches (with lease); `link`, `merge` and `unstack` write to GitHub. Ask before those, as for any force push or GitHub write. Never `gh stack merge`.

## Commands

Use `just` (`just --list`):

| Recipe | What it does |
|---|---|
| `just doctor` | Check tools and git settings; prints install hints. |
| `just hooks` | Install the lefthook git hooks. |
| `just lint` | All linters: `lint-md`, `lint-yaml`, `lint-actions`, `lint-sh`, `typos`, `editorconfig`. |
| `just build` | Build the .NET solution (`dotnet build -m:1`, warnings as errors). |
| `just test` | Build, then run every test project (TUnit on Microsoft.Testing.Platform). |
| `just publish` | Native AOT publish of `blokky` and `blokky-testkit` (default `linux-x64`) to `artifacts/publish/`; fails on any trim/AOT warning. |
| `just test-aot` | `publish`, then the tests including those against the published binary (what CI runs). |
| `just format-check` | C# whitespace formatting (style rules are enforced by the build). |
| `just conformance` | Run the blokky-specs conformance suite against the AOT binary (added with the runner, T-0068). |
| `just pin` | Pin workflow actions to full SHAs (`pinact run`). |
| `just claude` | Start Claude Code with `../blokky-specs` added. |

Run `just lint` (and, for code, `just test`) before committing. The hooks (pre-commit, commit-msg) run the same checks on staged files plus gitleaks, the DCO sign-off and the Conventional Commits header.

## Work tracker

The single tracker for both repos is [docs/plan/TRACKER.md](docs/plan/TRACKER.md). Rules:

- IDs are never reused: `T-NNNN` tasks, `F-NNNN` findings, and the brief's own question IDs (`Q1…`, `Q-a…`).
- Every task has a repo tag: `[cli]`, `[specs]` or `[both]`.
- Finished items stay in place, ticked, with date and commit/PR link. Dropped items (`[-]`) keep a one-line reason.
- Update the tracker **in the same PR** as the work it tracks (in blokky-specs PRs, open a companion blokky-cli PR or note the update in the PR description for the maintainer).
- Start from "Now", in phase order. Propose before starting anything larger than one PR. Ask the maintainer about `[!]` items.
- New questions and findings discovered during work are added immediately, never only mentioned in chat.
- When a phase completes, add a one-line summary under its heading.

## Writing style

- Precise and concise. No marketing fluff.
- Normative language (MUST/SHOULD/MAY) in the spec and in the configuration and validation docs.
- Plain, friendly language in user docs.
- Every v0.1 example must be something a user could actually run; examples use canonical forms only.

## Security posture (don't over-engineer)

- The baseline is "the same as plain bash": what an agent could do with a plain shell tool call. Blokky must never be *less* safe than that and should be more reviewable.
- It must not grant capabilities, bypass the runner's permission system, or hide what will run.
- Blokky is not a sandbox. Agents keep a generic shell tool; controls that only work if agents can *only* use Blokky are out of scope.
- Hygiene (strict lint, secret masking, display sanitization) is not a control; the runner's permissions, the hook and the validator are. Docs must not present hygiene as enforcement.
- Resource bounds exist for ergonomics and predictability, not as security boundaries.

## Local overrides

`local.just`, `CLAUDE.local.md`, `.claude/settings.local.json` and `lefthook-local.yml` are personal and gitignored. Never commit them, and never put machine-specific paths or tools into committed files.
