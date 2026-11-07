# Spike plan

Each spike verifies runtime behaviour that the spec or the run-control contract marks "Provisional — verify in spike", or that the CLI design depends on and can't be confirmed on paper. Spikes are time-boxed experiments on unmerged `spikes/<name>` branches (brief §0.2). Their findings come back as spec or CLI ADRs and edits, and the spike code is thrown away. The order follows the runtime plan ([ADR 0007](../adr/0007-runtime-plan.md)): a spike runs before the release that needs it.

Status: proposed 2026-11-07 (T-0027), waiting for approval (T-0028).

## Ground rules

- **Shape:** a spike is a minimal .NET 11 console app, published with Native AOT (`PublishAot`), plus a script that exercises it. It is not production code: no reuse, no tests beyond the acceptance checks.
- **Platforms:**
  - Linux runs locally under WSL (Fedora) and in CI (`ubuntu-24.04`).
  - Windows runs locally (Windows binaries from WSL) and in CI (`windows-2025`).
  - macOS runs only on CI macOS runners, because no Mac is available. A spike that needs macOS behaviour states which results CI alone can confirm.
- **Report:** each spike ends with a short report in `docs/design/notes/spike-<name>.md`. It covers the answer, the evidence (commands, output), the decision it supports, and what changes in the spec or the docs. The report merges through a normal docs PR; the branch stays unmerged.
- **Time box:** S is about half a day, M one to two days. A spike that overruns stops and reports what it has.

## Spikes before MVP code (0.1, Linux)

Answering ADR 0007's spike question, this set adds three spikes to the brief's MVP list, because 0.1 needs them:

- **Spike 8, Linux part:** sequential leaves with descendants are in 0.1, and EM-20/EM-68 (drain and completion order) are provisional.
- **Spikes 12 and 14:** `script` with `pwsh` is in 0.1 on Linux, and SH-17/SH-20 are provisional.

Approving this plan approves the addition.

| # | Spike | Question | Rules | Acceptance | Size |
|---|---|---|---|---|---|
| 1 | **Launcher, lifeline and tree kill (POSIX)** | Which launch path gives each leaf its own session or process group, with no race, under Native AOT? The options are `posix_spawn` with `POSIX_SPAWN_SETSID` (glibc ≥ 2.26), a small `blokky __exec` trampoline, or .NET 11 `Process` APIs. How is the whole tree stopped (SIGTERM, grace, SIGKILL to the group)? | EM-15, EM-21, EM-22, EM-26, RC-6 | A leaf that spawns a three-level tree, plus a `setsid` and a double-fork escapee: no leaf-group process survives a timeout, and none survives `kill -9` of the host (with spike 5's lifeline). Escapees are stopped best-effort and reported. Results from 50 runs on CI and WSL. | M |
| 5 | **Lifeline when the host is killed** | Do .NET 11 `KillOnParentExit` (if it's in GA) and `PR_SET_PDEATHSIG` set from a dedicated thread stop the leaf trees when `blokky` gets SIGKILL? What about subreapers (`PR_SET_CHILD_SUBREAPER`)? | EM-22, RC-6 | `kill -9` of the host during a run with ten nested leaves leaves no leaf process alive within 1s, on Linux. The documented per-OS guarantee for the run-control contract is written down. | S |
| 8L | **Drain and leaf completion order (Linux)** | Can descendants that keep stdout/stderr open be detected (`descendants_held_output`)? Does draining for at least 1s, then stopping the tree, give EM-68's order (drain, stop the tree, bind captures, start the next sibling)? | EM-18, EM-20, EM-21, EM-68 | Testkit `spawn` cases: late output written within the drain bound is captured; a held pipe is reported and doesn't block past the drain bound plus grace; the next sibling never overlaps a dying descendant. | S |
| 6 | **Absolute executable paths** | Does starting a resolved absolute path bypass the OS search everywhere? Do symlinked tools keep their name (SH-29, ADR 0023 G-2)? | SH-25 to SH-29 | `program` is the PATH entry, not the symlink target, and `argv[0]` is as found. No `PATH` search happens after resolution. | S |
| 3 | **YamlDotNet under AOT** | Under trimming and AOT: duplicate keys, aliases, anchors, tags, complex keys, line/column positions on events, same-line comments (SY-89 pitfalls), the BOM and the encoding pass. Is the event parser needed instead of the deserializer? | SY-1, SY-14 to SY-29, SY-89 | The suite's syntax/text and syntax/yaml validation cases give the right code and position from a prototype parser (events only, no reflection), and the binary has no trim or AOT warnings. | M |
| 4 | **Schema and editor behaviour** | How do yaml-language-server and VS Code type unquoted scalars against the schema (`default: 1.10` in `inputs`, `on`, `010`)? Does the schema still give useful completion and hover? | SY-23 to SY-27, SY-95 | A short report with screenshots or logs for the cases in SY-89. The schema changes needed, if any, are proposed as a specs PR. | S |
| 7 | **Safe deletion of run-owned files** | Do `openat`/`unlinkat`/`fstatat` via `LibraryImport` give a removal that never follows links (EM-69) under AOT? | EM-69, reference.md §4.5 | A tempdir containing symlinks to outside files and directories is removed without touching the targets, including when a link is swapped in during removal (a TOCTOU attempt). | S |
| 9 | **Regex subset on `NonBacktracking`** | Can a validator for the portable subset (SY-61, SY-62) and its translation to .NET `RegexOptions.NonBacktracking` run under AOT, with `$`, `\b` and `(?i)` keeping the spec's meaning (CO-12's simple uppercase mapping)? | SY-61, SY-62, CO-12 to CO-14, RF-22 | The `conditions/operators` and `syntax/grammars` regex cases pass in a prototype, and anything outside the subset is rejected with RF-22. No AOT warnings. | S |
| 12 | **`pwsh -File` exit codes (Linux)** | What exit code does `pwsh -File` give after a failing native command, after `throw`, and after `exit N`, with the prologue? | SH-17, SH-18 | A table of observed exit codes for pwsh 7.x on Linux, matching or amending SH-17/SH-18. | S |
| 14 | **PowerShell prologue and minimum versions** | Does the prologue scanner (skipping comments, blank lines and `using`, rejecting `param(`) keep line numbers, and on which pwsh versions? | SH-13, SH-17, SH-20 | The shells/script pwsh cases' expected line numbers hold on pwsh 7.4 and 7.5 on Linux. The minimum version is stated, and SH-20 is resolved or narrowed. | S |

The conformance runner's process needs (the `kill` action, `after_run` checks, per-OS timing; CF-22, CF-40, CF-43, CF-64) are built in T-0068 on top of spikes 1, 5 and 8L. They are not a separate spike.

## Spikes before later releases

| # | Spike | Before | Question | Rules |
|---|---|---|---|---|
| J | **Join confirmation** | 0.3 | Cancelled children of `all`/`any`/`race` are confirmed (`finally` included) before the join completes, with bounded waits | EM-31, EM-32, EM-58 |
| 10 | **Hook `ask` vs runner allow rules** | 0.4 | Does a PreToolUse hook `ask` override the runner's allow rules, and is a rewritten tool input shown on `ask` (Claude Code)? | hook design (cli) |
| 2 | **Windows process control** | 0.6 | Hidden console per leaf (EM-15 MUST) inside a Job Object, race-free job assignment, CTRL_BREAK delivery, `graceful: none`, tree kill, lifeline via the job | EM-15, EM-26, EM-30, RC-6 |
| 5W, 8W | **Lifeline and drain on Windows** | 0.6 | The Windows half of spikes 5 and 8 | EM-20, EM-22 |
| C | **Windows output code pages** | 0.6 | UTF-8 output per leaf (`[Console]::OutputEncoding`, the console code page) without changing global state | SH-34, SH-12, SH-17, VA-20, VA-21 |
| 11, 13 | **Containers: idle process and in-container kill** | 0.7 | Termination via the engine CLI with compatible images (`sh`, `sleep`, `kill`); no helper inside the image | CP-4, CP-8, CP-36, RC-21 |
| E | **Engine matrix** | 0.7 | Docker and Podman, rootless, SELinux without relabelling, desktop VMs; invoking-user mapping for non-root images; `exec --env-file` | CP-9 to CP-11, CP-16, CP-17, CP-37, CP-38 |
| M | **macOS port** | macOS release | `posix_spawn` setsid availability, no `PDEATHSIG` (kqueue `NOTE_EXIT` instead), `/private/var` paths; CI only | EM-15, EM-22, RC-6 |

Dropped: the `become` transport and sudoers part of spike 11, because `become` is cut (specs ADR 0017).

## Input from the council review of draft.3

The [council report](../review/council-draft.3.md) §4 mapped the proposed spikes onto the brief's spike list. As decided in §6 and applied in `1.0.0-draft.4` (specs ADRs 0017–0019):

| Topic | Spec rules | Plan above |
|---|---|---|
| Cancelled group children in joins: confirmation waits for `finally` | EM-31, EM-32, EM-58 | J |
| Leaf completion order: drain, stop the tree, captures, next sibling | EM-18, EM-20, EM-21, EM-68 | 8L, 8W |
| Tree kill and lifeline per OS; `keep_descendants: detach` | EM-15, EM-21, EM-22 | 1, 5, 5W, M |
| Windows graceful stop: hidden console per leaf (a MUST since [specs ADR 0020](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0020-conformance-gaps.md)), `graceful: none` | EM-15, EM-26, EM-30 | 2 |
| Containers with compatible images (`sh`, `sleep`, `kill`): idle process, in-container kill | CP-4, CP-8, CP-36 | 11, 13 |
| Engine matrix | CP-9 to CP-11, CP-16, CP-17, CP-37, CP-38 | E |
| Portable regex subset translated to .NET NonBacktracking under AOT | SY-61, SY-62, CO-13, RF-22 | 9 |
| Windows output code pages | SH-34, VA-20, VA-21, SH-12, SH-17 | C |
| PowerShell prologue scanner, execution policy, minimum shell versions | SH-13, SH-17, SH-20 | 12, 14 |
| Conformance runner: `kill` action, `after_run` checks, per-OS timing | CF-22, CF-40, CF-43, CF-64, EM-60 | T-0068 (built on 1, 5, 8L) |
