# 0002: CLI and run lifecycle (open-question batch 3)

- Status: accepted
- Date: 2026-11-04
- Questions: Q6, Q15, Q-f, Q-t, Q-u

## Context and problem statement

The [specification-phase brief](../design/brief.md) §4 lists questions that must be answered before the language spec draft and the CLI docs are written. This ADR records the maintainer's answers to batch 3, the CLI and run lifecycle: `blokky` exit codes, how cleanup failures are reported, the default run budget, what `clean` removes, and whether the hook may auto-allow cleanup. It refines [ADR 0001](0001-baseline.md) and builds on [blokky-specs ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md) (`cleanup_reserve`, timers).

Each question was asked as a multiple choice with a recommendation, on 2026-11-04. The options considered are listed per decision below; the chosen one is marked.

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| Q6 exit codes | **small dense set (0–9, 70, 128+N)**; `sysexits.h` mapping; minimal 0/1/2 |
| Q15 partial cleanup | **JSON list of removed and failed items, own exit code**; generic failure code; exit 0 with warnings |
| Q-f default budget | **1m50s, hook margin 10s**; 9m30s, margin 30s; no default |
| Q-f budget sources | **the default is a fallback, explicit sources may exceed it**; the default is a ceiling |
| Q-u `clean` scope | **everything except `report.json` and `events.jsonl`; `--all` removes those too**; everything; keep report and logs unless `--all` |
| Q-t hook auto-allow | **`clean <run-id>` allowed, `gc` asks**; both allowed; neither |

## Decision outcome

All answers follow the recommendation. The Q-f follow-up corrects the brief's statement that the default run budget is one of the sources that "can only lower the budget".

### Q6: exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | Plan failed: the run finished and a node failed. |
| 2 | Usage error: malformed flags or arguments, `--input` errors. |
| 3 | Plan invalid: parse, character-lint, schema or lint errors. |
| 4 | Config error. |
| 5 | Approval needed (`ask`). |
| 6 | Rejected (`deny` by the validator). |
| 7 | `--expect-sha256` mismatch. |
| 8 | Run budget exceeded. |
| 9 | Cleanup incomplete (`clean`, `gc`). |
| 70 | Internal error (a bug in Blokky). |
| 128+N | Interrupted by signal N: 130 SIGINT, 143 SIGTERM, 129 SIGHUP. On Windows, Ctrl-C and Ctrl-Break give 130. |

- Codes 126 and 127 are avoided, because shells use them for "not executable" and "not found".
- The JSON on stdout always carries the details; the exit code only names the class of outcome.
- A closed stdout (`blokky run … | head`) doesn't change the code: the report goes to the run dir only.
- The codes are a contract of `blokky` and are documented in `docs/cli.md`; the report and the pre-run envelope carry the same outcome in their own fields.

### Q15: partial cleanup

`clean`, `gc` and `run --cleanup` share one deletion routine.

- `clean` and `gc` print JSON on stdout that lists what was removed and what failed, with the path and the reason. A partial deletion exits with 9.
- Registry entries stay for everything that is left, so rerunning `clean` or `gc` is idempotent and retries only the remainder.
- For `blokky run --cleanup on-success|always`, the exit code still reflects the plan. A partial cleanup sets `cleaned: partial` in the report's `artifacts` and writes a warning to stderr.

### Q-f: run budget

- **Default run budget: 1m50s.** It fits under Claude Code's default 2-minute Bash tool timeout, so a call without the hook still ends with a report rather than a harness kill.
- **The default is a fallback.** It applies only when no explicit source sets a budget. When config, `BLOKKY_MAX_DURATION` or `--max-duration` set one, the smallest explicit value wins, and it may be longer than the default. `--max-duration` stays a tightening flag: it only ever bounds a run, and an unbounded wait is already possible with a plain `sleep` in bash.
- **Nested runs** inherit the remaining budget and can only lower it.
- **Hook margin: 10s.** `hook check` reads the tool call's timeout and requires or injects `--max-duration` of at most that timeout minus 10s. Background mode is exempt, as in the brief; there the agent guide asks for an explicit `--max-duration`.
- This supersedes the brief's §3.2 wording that lists the default among the sources that "can only lower the budget".

### Q-u: what `clean` removes

- `blokky clean <run-id>` removes the run's logs, capture files, script files, tempdirs and (later) containers. It keeps the run's `report.json` and `events.jsonl` and its registry entry, marked as cleaned, so `blokky report <run-id>` keeps working.
- `blokky clean --all <run-id>` also removes the report, the event log, the run dir and the registry entry.
- `blokky gc` removes everything, reports included, of every run that isn't in progress.

### Q-t: hook auto-allow for cleanup

- `hook check` auto-allows exactly `blokky clean <run-id>`: no other flags, and a well-formed run id. It removes only one run's runtime-owned artefacts, and it is the step the agent flow ends with.
- `blokky clean --all <run-id>` and `blokky gc` get `ask`: `gc` removes every finished run, including runs the user or another session may still want to inspect, and `--all` removes the evidence of what ran.

### Consequences

- Good: callers and the hook can branch on the exit code without parsing JSON, and a budget kill is distinct from a plan failure.
- Good: the typical agent flow (run, read the report, `clean`) needs no approval prompt for the cleanup step, and the report stays readable afterwards.
- Neutral: the 1m50s default is tied to Claude Code's current tool timeout. If harness defaults change, the default changes with a new ADR.
- Neutral: runs longer than 1m50s always need an explicit budget, from the flag, the hook or config.
- Bad: kept reports and event logs accumulate until `clean --all` or `gc`; they are small, but nothing removes them automatically.

## More information

- Brief: §1.3 (artefacts and cleanup), §3.2 (run budget), §3.8 (shutdown, cleanup deadline), §3.9 (hook auto-allow), §4 (questions).
- Tracker: T-0010; the answers are linked from the [open-questions table](../plan/TRACKER.md#open-questions).
- Docs that apply these answers: `docs/cli.md` (exit codes, `clean`, `gc`, budget flags), `docs/validation-and-permissions.md` (Q-t, hook margin), `docs/agent-guide.md` (run budget, background pattern, clean flow). The report's `cleaned` field and the budget timeline are in blokky-specs (`spec/output-contract.md`, `spec/execution-model.md`).
