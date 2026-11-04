# Agent instructions: blokky-cli

These instructions are for coding agents (Claude Code, Codex and others) working in this repo. Humans: see [CONTRIBUTING.md](CONTRIBUTING.md).

## This repo

blokky-cli holds the .NET implementation of Blokky (CLI host, later an MCP host), configuration, validation and hook integration, the threat model, agent docs, install docs, design docs and CI/CD. Its sibling **blokky-specs** holds the language specification, JSON contracts, schemas, examples and the conformance suite. Expect both repos checked out side by side (`../blokky-specs`). Each has its own branches, PRs, ADRs and CHANGELOG. A change touching both gets two linked PRs; the spec PR merges first.

## Brief and process

[docs/design/brief.md](docs/design/brief.md) is the source of truth for the current phase. Read it before any non-trivial work. Its §4 open questions are asked, not decided silently; its §8 defines the process.

**Current phase: specification.** No implementation code. Spike code is allowed only on unmerged `spikes/*` branches, after the maintainer approves the spike plan.

## Working agreement

- Work on feature branches (`docs/<topic>`, spikes `spikes/<topic>`). **Never commit to `main`.**
- Push branches and open PRs with `gh`. The PR description gives a summary, the decisions it applies (link ADRs), tracker IDs and open items. **Never merge PRs**; the maintainer reviews and merges.
- Conventional Commits (`docs: …`, `chore: …`); small, logical commits.
- **DCO and signatures:** always `git commit -s`, with commit signing enabled; never disable signing or pass `--no-verify`. Tags are annotated and signed (`git tag -s`), and release tags are the maintainer's. Write the commit message to a file outside the repo and pass it with `git commit -s -F <file>`.
- After a PR merges, rebase remaining branches onto `main`.

## Commands

Use `just` (`just --list`):

| Recipe | What it does |
|---|---|
| `just doctor` | Check tools and git settings; prints install hints. |
| `just hooks` | Install the lefthook git hooks. |
| `just lint` | All linters: `lint-md`, `lint-yaml`, `lint-actions`, `lint-sh`, `typos`, `editorconfig`. |
| `just pin` | Pin workflow actions to full SHAs (`pinact run`). |
| `just claude` | Start Claude Code with `../blokky-specs` added. |

Run `just lint` before committing. The hooks (pre-commit, commit-msg) run the same checks on staged files plus gitleaks, the DCO sign-off and the Conventional Commits header.

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
