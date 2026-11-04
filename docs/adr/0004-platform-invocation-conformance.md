# 0004: Process launcher, Windows invocation and conformance checkout (open-question batch 6)

- Status: accepted
- Date: 2026-11-04
- Questions: Q-e, Q-h, Q-o, Q-q (conformance checkout)

## Context and problem statement

Batch 6 of the [specification-phase brief](../design/brief.md)'s §4 questions covers platform details and the repository split. This ADR records the implementation-side answers:

- how the process-launcher mechanism gets decided (Q-e);
- the canonical agent invocation on Windows and the PowerShell grammar for `--input` (Q-h);
- whether stdin plans need a heredoc-expansion sentinel (Q-o);
- how CI gets the conformance suite (part of Q-q).

The `.cmd` shim rule (Q-l) and the spec-text license (the rest of Q-q) are in [blokky-specs ADR 0006](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0006-shims-and-license.md).

Each question was asked as a multiple choice with a recommendation, on 2026-11-04. The options considered are listed per decision below; the chosen one is marked.

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| Q-e launcher and lifeline | **spike #1 decides, with criteria**; trampoline now; `posix_spawn` now |
| Q-h Windows invocation | **heredoc where the shell tool is bash, file form in PowerShell**; also pwsh here-strings; file form everywhere |
| Q-o heredoc sentinel | **document the gap**; optional sentinel; required sentinel |
| Q-q conformance checkout | **pinned explicit checkout**; git submodule |

## Decision outcome

All answers follow the recommendation.

### Q-e: launcher and lifeline

Spike #1 decides between `posix_spawn` with `POSIX_SPAWN_SETSID` and a `blokky __exec` trampoline, and between a per-leaf lifeline and one per-run watchdog. A follow-up ADR records the result. The acceptance criteria are fixed now:

- no leaf or descendant survives a SIGKILL of `blokky` longer than the grace period, on linux-x64, linux-arm64, osx-arm64 and win-x64;
- AOT-safe (`LibraryImport` P/Invoke only), with no trim or AOT warnings;
- no orphaned watchdog or trampoline processes after the run ends;
- process start overhead per leaf is measured and reported.

The trampoline everywhere is the hypothesis tested first, because `become` already uses it (blokky-specs ADR 0005). Direct `posix_spawn` is the comparison.

### Q-h: Windows agent invocation

- **Where the runner's shell tool is bash** (including Claude Code on Windows, whose Bash tool runs in Git Bash), the heredoc form `blokky run <<'BLOKKY_PLAN_EOF' … BLOKKY_PLAN_EOF` is canonical, on every OS.
- **Where the shell tool is PowerShell,** the agent writes the plan with its file tool, then runs `blokky run --expect-sha256 <hash> <file>.blokky.yaml`; `blokky plan <file>` prints the hash. PowerShell here-strings piped to `blokky run` are not a canonical form: Windows PowerShell 5.1 pipes non-ASCII text as `?` and both versions add CRLF and encoding layers.
- **`--input` values in PowerShell** follow the same canonical rule as in bash (brief §3.3): a bare value from `[A-Za-z0-9._/:@%+,=-]*`, or single-quoted `'…'` with an embedded `'` written as `''`. Values with `$`, backticks, double quotes, CR or LF are non-canonical (`ask`/`deny`, never allow).
- The agent guide shows both forms; the hook's canonical-form rules for PowerShell tool calls are specified in `validation-and-permissions.md`.

### Q-o: heredoc expansion

No sentinel. An unquoted delimiter (`<<BLOKKY_PLAN_EOF`) lets the agent's own bash expand `$X` and backticks before Blokky starts, which is no worse than the same text run as plain bash. The defences that remain:

- `hook check` rejects every form but the quoted delimiter;
- `blokky run` warns when a plan contains a delimiter-like line;
- the agent guide documents the gap and the quoted form.

### Q-q: conformance checkout

- blokky-cli pins the conformance suite in one file, `conformance.lock`, holding a blokky-specs tag and its commit SHA.
- CI checks out blokky-specs at that SHA and verifies that the tag points to it.
- Locally, `just conformance` uses `../blokky-specs` when it is checked out side by side (with a warning if it differs from the pin), otherwise it fetches the pin.
- There is no git submodule, so clones need no `--recursive` and agents don't trip over submodule state. Bumping the pin is a one-line PR. How the pin is bumped automatically, if at all, is decided in `ci-cd.md`.

### Consequences

- Good: Q-q is fully answered (names and license: blokky-cli ADR 0001 and blokky-specs ADR 0006).
- Good: the agent guide has one canonical form per shell tool, and the hook only needs bash heredoc parsing plus the file form.
- Neutral: the launcher stays open until spike #1; design docs describe both candidates until then.
- Bad: PowerShell-based runners need two tool calls (write the file, then run it with its hash).
- Bad: an agent's unquoted heredoc in a runner without the hook still mangles the plan silently, apart from the delimiter warning.

## More information

- Brief: §0.1 (repositories, conformance), §3.0 (stdin convention), §3.3 (input grammar), §3.8 (launcher, lifeline), §3.9 (hook canonical forms), §8.4 (spikes), §4 (questions).
- Tracker: T-0013; the answers are linked from the [open-questions table](../plan/TRACKER.md#open-questions).
- Docs that apply these answers: `docs/design/process-control.md` and the spike plan (Q-e), `docs/agent-guide.md` and `docs/validation-and-permissions.md` (Q-h, Q-o), `docs/design/ci-cd.md` (Q-q).
- Companion: [blokky-specs ADR 0006](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0006-shims-and-license.md) (Q-l, Q-q license and testkit).
