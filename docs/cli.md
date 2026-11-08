# Command-line interface

Commands, flags and flag classes, argument-parser rules and exit codes of the `blokky` CLI.

Status: **draft**. The exit codes (below) are drafted with the language spec draft; commands, flags and parser rules follow in the fan-out docs (brief §8.5).

## Exit codes

Decisions: [ADR 0002](adr/0002-cli-run-lifecycle.md) (the table), [ADR 0005](adr/0005-exit-code-details.md) (precedence, Windows events). Error codes and phases: [contracts/errors.md](contracts/errors.md) (CLI codes and the phase → exit-code mapping) and [blokky-specs `spec/errors.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/errors.md) (the language's codes).

| Code | Meaning | Typical error codes |
|---|---|---|
| 0 | Success. For `run`, also when the root node was skipped by its `when`. | — |
| 1 | Plan failed: the run finished and a node failed. | the root node's error |
| 2 | Usage error: malformed flags or arguments, `--input` errors, an unknown or in-progress run id for `clean`/`report`. | `BK_USAGE`, `BK_INPUT_*`, `BK_RUN_NOT_FOUND`, `BK_RUN_IN_PROGRESS` |
| 3 | Plan invalid: encoding, character lint, YAML, schema, static rules, lint at severity `error`. | `BK_PLAN_*`, `BK_LINT` |
| 4 | Config error. | `BK_CONFIG` |
| 5 | Approval needed (`ask`). | `BK_APPROVAL_*`, `BK_VALIDATOR_FAILED` (for `validate`) |
| 6 | Rejected (`deny` by the validator or runner-rules mode). | `BK_DENIED` |
| 7 | `--expect-sha256` mismatch. | `BK_SHA_MISMATCH` |
| 8 | Run budget exceeded. | `BK_BUDGET_EXCEEDED` |
| 9 | Cleanup incomplete (`clean`, `gc`). | `BK_CLEANUP_INCOMPLETE` |
| 70 | Internal error (a bug in Blokky). | `BK_INTERNAL` |
| 128+N | Interrupted by signal N: 130 SIGINT, 143 SIGTERM, 129 SIGHUP. Windows: Ctrl-C and Ctrl-Break 130; console close, logoff and shutdown events 129. | `BK_INTERRUPTED` |

- Codes 126 and 127 are never used: shells use them for "not executable" and "not found".
- The JSON on stdout always carries the details; the exit code only names the class of outcome.
- A closed stdout (`blokky run … | head`) doesn't change the code; the report goes to the run dir only.
- `blokky run --cleanup on-success|always` keeps the plan's code even if cleanup is incomplete; the report's `artifacts.cleaned` is `"partial"` and stderr carries a warning.

### Precedence before a run starts

`blokky run` checks an invocation in this order, and stops at the first step that fails:

1. command-line arguments and flags (2);
2. `--expect-sha256` against the plan bytes (7); on a mismatch nothing in the plan is trusted or checked further;
3. configuration (4);
4. the plan: encoding, character lint, YAML, schema, static rules, lint errors (3);
5. input values against the plan's `inputs` (2);
6. approval: `--approval` records and `require_hook_approval` (5).

The exit code is that of the failing step. Within a step, every error found is reported; the pre-run envelope's `phase` names the step (`usage`, `plan`, `config` or `approval`; step 2 reports `approval`).

### Per command

| Command | Exit codes |
|---|---|
| `run` | 0, 1, 2, 3, 4, 5, 7, 8, 70, 128+N |
| `plan`, `leaves` | 0 (also with missing required inputs, shown as `<input:NAME>`), 2, 3, 4, 7, 70 |
| `lint` | 0 (no findings at severity `error`), 2, 3, 4, 70 |
| `validate` | 0 (`allow`), 2, 3, 4, 5 (`ask`), 6 (`deny`), 7, 70 |
| `hook check` | 0 with a JSON verdict (`allow`, `ask` or `deny`) on stdout, including for invalid plans and inputs (`deny` with a fix-it) and internal problems (`ask`); 70 only if it can't produce a verdict at all |
| `report` | 0, 2, 70 |
| `clean`, `gc` | 0, 2, 9, 70 |
| `config show`, `schema`, `version` | 0, 2, 4, 70 |

## `validate` output

`blokky validate [--input NAME=VALUE]... <plan-file | ->` runs the static front half that `run` runs (encoding, YAML, schema, static rules, the feature gate on the *validated* table) and starts nothing ([architecture.md §1.1](design/architecture.md#11-the-ast-and-the-validator)).

- **Rejected** (exit 3, or the code of an earlier failing step): stdout carries the pre-run envelope ([output-contract.md §4](contracts/output-contract.md#4-the-pre-run-envelope)) with every error of the step. Plan errors carry `pos`; `node`, the path id of the node the error belongs to (`/` for the plan-only keys), only when it belongs to one (reader-level errors, such as SY-14, SY-16, SY-18, SY-32 and YAML syntax errors, have none); `detail` (`rule`, or `suggestion` for an unknown key, `supported` for `BK_PLAN_VERSION`, `feature` for `BK_PLAN_NOT_SUPPORTED`); and, for targeted errors, `fix`. stderr carries one line per error: `blokky: error CODE at LINE:COLUMN in NODE: message`, without `in NODE` for an error with no node.
- **Accepted** (exit 0): stdout carries the verdict, one JSON document followed by LF, and stderr nothing. Without a validator (`validator.path`, from 0.4) the decision is `allow`; the validator adds `ask` (exit 5) and `deny` (exit 6) with their `reason` (LV-29). Provisional until the 0.1 contracts (T-0071, tracker F-0034):

```json
{ "schema_version": "0.1", "status": "valid", "decision": "allow",
  "plan": { "sha256": "9f2c41…", "spec_version": "1.0", "source": "stdin" } }
```

`plan` has the run report's fields: the SHA-256 of the plan bytes, the spec version from `blokky:`, and `stdin` or the plan file's absolute path.
