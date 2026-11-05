# CLI error codes

Status: **draft**

The language's own error codes (plan, input and node errors) are defined in [blokky-specs `spec/errors.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/errors.md), including the code rules (ER-1 to ER-3). This document adds the codes `blokky` uses for its own concerns, and maps every kind of error to where it is reported and to an exit code ([cli.md](../cli.md#exit-codes)).

## 1. Phases and exit codes

| Phase | Codes | Where it is reported | Exit code |
|---|---|---|---|
| **Usage** | `BK_USAGE`, `BK_INPUT_*` (language) | pre-run envelope | 2 |
| **Plan** | `BK_PLAN_*` (language), `BK_LINT` | pre-run envelope; `plan`, `leaves`, `lint`, `validate` | 3 |
| **Config** | `BK_CONFIG` | pre-run envelope | 4 |
| **Approval** | `BK_SHA_MISMATCH`, `BK_APPROVAL_*`, `BK_DENIED`, `BK_VALIDATOR_FAILED` | pre-run envelope; hook verdicts | 5, 6 or 7 |
| **Node** | node errors (language), user codes | the failing node's entry in the report | 1 (if it fails the run) |
| **Run** | `BK_BUDGET_EXCEEDED`, `BK_INTERRUPTED`, `BK_INTERNAL` | the report's run-level `error` | 8, 128+N, 70 |
| **Cleanup** | `BK_CLEANUP_INCOMPLETE`, `BK_RUN_NOT_FOUND`, `BK_RUN_IN_PROGRESS` | `clean`/`gc` output; the report's `artifacts` | 9 or 2 |

The order in which phases are checked before a run starts is in [cli.md](../cli.md#precedence-before-a-run-starts).

## 2. Usage, config and approval codes

| Code | Meaning |
|---|---|
| `BK_USAGE` | Malformed command-line flags or arguments (unknown flag, missing value, a disallowed spelling such as `--opt=v`). |
| `BK_LINT` | A lint finding at severity `error`. `detail.rule` is the lint rule id. |
| `BK_CONFIG` | A config file can't be read or parsed, fails its schema, or a non-user file sets a user-only key. `detail.file`, `detail.key`. |
| `BK_SHA_MISMATCH` | The plan bytes don't match `--expect-sha256` (exit 7). |
| `BK_APPROVAL_REQUIRED` | `require_hook_approval` is on and `blokky run` got no `--approval` (exit 5). |
| `BK_APPROVAL_NOT_FOUND` | `--approval` names a record that doesn't exist or was already used (exit 5). |
| `BK_APPROVAL_EXPIRED` | The approval record is older than `approval_ttl` (exit 5). |
| `BK_APPROVAL_MISMATCH` | The run's cwd or resolved config/state locations differ from the approval record (exit 5). |
| `BK_DENIED` | The validator or runner-rules mode denied the invocation (`validate`, exit 6). |
| `BK_VALIDATOR_FAILED` | The validator timed out, crashed, was missing or answered invalid JSON; the verdict becomes `ask` (exit 5 for `validate`). |

## 3. Run codes

| Code | Meaning | Exit code |
|---|---|---|
| `BK_BUDGET_EXCEEDED` | The run budget was reached; Blokky cancelled the run. Leaves killed by it carry `killed_by: budget`. | 8 |
| `BK_INTERRUPTED` | Blokky received a signal or console control event and shut down. `detail.signal`. | 128+N |
| `BK_INTERNAL` | A bug in Blokky. The message asks for a report. | 70 |

## 4. Cleanup codes

| Code | Meaning | Exit code |
|---|---|---|
| `BK_CLEANUP_INCOMPLETE` | `clean`, `gc` or `run --cleanup` couldn't remove every artefact; the output lists each failure with its path and reason. | 9 (for `clean`/`gc`) |
| `BK_RUN_NOT_FOUND` | `clean` or `report` names a run id that isn't registered. | 2 |
| `BK_RUN_IN_PROGRESS` | `clean` names a run that is still in progress; nothing is deleted. | 2 |
