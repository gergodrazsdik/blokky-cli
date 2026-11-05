# Output contract

Status: **draft**. Moved from blokky-specs on 2026-11-05 ([ADR 0006](../adr/0006-phase-order-and-incremental-cli.md)); rule IDs are unchanged.

This document is normative for everything Blokky writes for consumers: the stdout and stderr rules, the run report, the pre-run envelope, the error object, the event log, tails and truncation, and the files in the run dir. The JSON Schemas (`schema/report.schema.json`, `schema/events.schema.json` in this repo) are normative for structure; this document for meaning. The `leaves` document and the validator protocol are in [leaves-and-validation.md](leaves-and-validation.md). Normative keywords are used as defined in [blokky-specs syntax.md §1](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/syntax.md#1-conventions). Rules carry IDs (`OC-n`).

## 1. Channels

- **OC-1.** **stdout** carries exactly one JSON document and nothing else: the run report (§3), or the pre-run envelope (§4) when the run fails before it starts. It is UTF-8 without a BOM, written to the raw stdout stream, followed by one LF.
- **OC-2.** **stderr when it isn't a terminal** (the agent-harness case) carries, in order:
  1. always first, once a run has started, the report-path line: `blokky: run <run-id> report <path>`;
  2. warnings and errors, one per line, prefixed `blokky: warn` or `blokky: error` and a space, with the node id and name where relevant.

  `log` events at `info` and `debug` go only to the report and the event log. Under a harness, stderr lands in the agent's context, so it stays minimal.
- **OC-3.** **stderr on a terminal, or with `-v`,** additionally carries progress lines (node start and end) and `log` events at every level.
- **OC-4.** **Display sanitization.** In all human-readable output (stderr, and `blokky plan`'s text view), C0 and C1 control characters and ESC are shown escaped (`\x1b`), so captured data can't inject terminal escape sequences. JSON output is safe by construction (control characters are JSON-escaped).
- **OC-5.** **Masking** of secrets applies to every channel and file ([variables.md §9](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/variables.md#9-secrets)).
- **OC-6.** If stdout is closed (`blokky run … | head`), the report is written only to the run dir and the exit code doesn't change.

## 2. Run dir files

The run dir is `<state dir>/runs/<run-id>/` (mode 0700; files 0600).

| File | Content |
|---|---|
| `report.json` | The full, untruncated report. Written to a temporary file and renamed atomically when the run ends (and on shutdown). |
| `events.jsonl` | The event log (§6). Append-only, flushed per event. |
| `logs/<node-file-name>.stdout`, `.stderr` | Raw output bytes of each leaf (`.out` alone with `stderr: merge`), capped (§7). |
| `scripts/<node-file-name>.<ext>` | Script files ([shells.md](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/shells.md)). |
| `captures/<node-file-name>` | `capture_file` contents. |

- **OC-7.** File names derive from node path ids, never from interpolated `name` values.
- **OC-8.** `blokky report <run-id>` and `blokky report --last` print a stored report to stdout with the same truncation as a run (§8); `--full` prints it untruncated. They never re-run anything.

## 3. The run report

### 3.1 Example

```json
{
  "schema_version": "0.1",
  "run_id": "01JC9X4M7QK3Z8V2W5T6Y1R0PA",
  "plan": { "sha256": "9f2c41…", "spec_version": "0.1", "source": "stdin" },
  "inputs": { "BRANCH": { "value": "feature/x", "source": "cli" } },
  "inputs_sha256": "4be0d7…",
  "status": "succeeded",
  "error": null,
  "started_at": "2026-11-04T01:02:03.120Z",
  "duration_ms": 1214530,
  "budget": { "limit_ms": 2700000, "source": "flag" },
  "outputs": { "RUN_ID": "12345678901" },
  "artifacts": {
    "run_dir": "/home/u/.local/state/blokky/runs/01JC9X4M7QK3Z8V2W5T6Y1R0PA",
    "tempdirs": [],
    "containers": [],
    "cleaned": false
  },
  "nodes": [
    { "id": "/", "name": "wait for CI on ${BRANCH}", "kind": "do", "pos": "2:1", "status": "succeeded",
      "duration_ms": 1214510, "children": [
      { "id": "/do/0", "name": "find run id", "kind": "exec", "pos": "11:5", "status": "succeeded",
        "iterations": { "count": 3, "ended_by": "condition" },
        "exit_code": 0, "stdout_tail": "12345678901" },
      { "id": "/do/1", "kind": "log", "pos": "17:5", "status": "succeeded",
        "message": "Watching CI run 12345678901" }
    ] }
  ],
  "truncation": null
}
```

### 3.2 Run-level fields

| Field | Type | Meaning |
|---|---|---|
| `schema_version` | string | The report schema version, `MAJOR.MINOR` (§9). |
| `run_id` | string | The run id (sortable, ULID-style). |
| `plan` | object | `sha256` of the plan bytes, `spec_version` from `blokky:`, `source` (`stdin` or the plan file's absolute path). |
| `inputs` | object | `NAME → { value, source }` in declaration order; `source` is `cli`, `mcp` or `default`. |
| `inputs_sha256` | string | SHA-256 of the RFC 8785 canonical JSON of the effective inputs map (`NAME → value`). |
| `status` | string | `succeeded`, `failed`, `budget_exceeded` or `interrupted` ([execution-model.md §8.3](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/execution-model.md#83-run-status-and-exit-code)). |
| `error` | object or null | The run's primary error (§5); null on success. |
| `started_at` | string | RFC 3339 UTC timestamp with milliseconds. |
| `duration_ms` | integer | Wall-clock duration of the run. |
| `budget` | object | `limit_ms` and `source` (`default`, `config`, `env`, `flag`, `inherited`). |
| `outputs` | object | `NAME → value`; `null` for unbound outputs; `{ "secret": true }` for secret ones. |
| `artifacts` | object | `run_dir`; `tempdirs` and `containers` (lists of paths and ids); `cleaned`: `false`, `true` or `"partial"`. |
| `nodes` | array | One root node entry with nested `children` (§3.3). |
| `warnings` | array | Run-level warnings (short budget, short secrets not masked, …) as `{ code, message }`. |
| `truncation` | object or null | null in `report.json` and when nothing was cut; otherwise what was cut (§8). |

### 3.3 Node entries

Node entries form a tree mirroring the plan; children appear in **plan order** under `children`.

| Field | Present | Meaning |
|---|---|---|
| `id` | always | The path id (`/`, `/do/0`, `/do/2/all/1`). |
| `name` | if set | The raw `name` template. Attempt and iteration entries carry the rendered name. |
| `kind` | always | The node's kind. |
| `pos` | always | `line:column` of the node in the plan. |
| `status` | always | `succeeded`, `failed`, `skipped`, `timed_out` or `cancelled`. |
| `ignored` | if true | The failure was ignored (`ignore_errors`). |
| `skip_reason` | if skipped | `condition` or `not_reached`. |
| `error` | if failed, timed out or cancelled | The node's error (§5). |
| `duration_ms` | if started | Wall-clock duration, `finally` included. |
| `attempts` / `iterations` | with repetition | `count`, `ended_by` (`condition`, `max`, `timeout`, `failure`), `last_condition` (`true`, `false`, `error`), and `items`: per attempt or iteration `{ index, name, item, status, duration_ms, error }`. |
| `children` | groups | Child node entries. |
| `finally` | if the node has `finally` | The `finally` list's node entries and its own `status`. |
| `message`, `level` | `log`, `fail`, `assert` | The rendered message (masked) and level. |
| `logs` | if any | `log` events emitted inside this node: `{ t, level, message }`. |
| `probes` | if any ran | `{ command, exit_code, duration_ms, result }` per executed probe. |

**Leaf entries** additionally carry:

| Field | Meaning |
|---|---|
| `program` | The resolved absolute path of argv[0] or of the shell. |
| `argv` | The argv (masked), for `exec`; the invocation, for `script`. |
| `shell` | For `script`. |
| `exit_code` | The exit code (an unsigned 32-bit integer on Windows). |
| `signal` | The terminating signal's name, on Unix, if any. |
| `killed_by` | `timeout`, `attempt_timeout`, `cancel`, `shutdown` or `budget`, if Blokky killed the leaf. |
| `termination` | How it was killed: `term`, `ctrl_break`, `kill`, `job` (Windows), and whether the graceful step was used. |
| `stdout_tail`, `stderr_tail` | Tails (§7). |
| `tails_truncated` | `true` if a tail doesn't start at the beginning of the output. |
| `log_truncated` | `true` if the log cap was reached. |
| `descendants_held_output` | `true` if a descendant held a pipe open past the leaf's exit. |
| `survivors` | Processes still alive after the reap timeout, as `{ pid, command }`. |
| `captures` | Names of the variables bound (never values). |

- **OC-9.** Captured values appear in the report only through `outputs`. Display strings, tails and logs are masked.
- **OC-10.** The non-deterministic fields are: timestamps, durations, the winners of `any` and `race`, completion order (visible only through timestamps), `survivors`, and process ids. Everything else is a function of the plan, inputs, environment and process behaviour.

## 4. The pre-run envelope

When a run fails before it starts ([run-control.md](run-control.md), RC-1), stdout carries:

```json
{
  "schema_version": "0.1",
  "status": "invalid",
  "phase": "usage",
  "errors": [
    { "code": "BK_INPUT_MISSING",
      "message": "required input BRANCH is missing: blokky run --input BRANCH=<value> --expect-sha256 9f2c41… wait-ci.blokky.yaml",
      "detail": { "input": "BRANCH", "command": "blokky run --input BRANCH=<value> --expect-sha256 9f2c41… wait-ci.blokky.yaml" } }
  ]
}
```

- **OC-11.** `status` is always `invalid`. `phase` names the first failing step in the order Blokky checks an invocation (arguments, `--expect-sha256`, config, plan, input values, approval; blokky-cli `cli.md`): `usage`, `approval` (for a sha mismatch), `config`, `plan`, `usage` (for input values) or `approval`. The exit code is that step's ([errors.md §2](errors.md#1-phases-and-exit-codes)).
- **OC-12.** `errors` lists every error found, each an error object (§5); plan errors carry `pos` and, where the error has one, `fix` (the corrected YAML snippet).
- **OC-13.** There is no `run_id`, no run dir and nothing to clean.

## 5. The error object

```json
{
  "code": "BK_RETRIES_EXHAUSTED",
  "message": "gave up after 3 attempts",
  "node": "/do/0",
  "detail": { "attempts": 3 },
  "causes": [ { "code": "BK_EXIT", "message": "exit code 1", "detail": { "exit_code": 1 } } ],
  "context": null
}
```

| Field | Meaning |
|---|---|
| `code` | A built-in `BK_` code or a user code ([errors.md](errors.md)). |
| `message` | Human-readable, masked, display-sanitized. Not a contract. |
| `node` | The id of the node the error belongs to (absent for run-level errors without a node). |
| `pos` | `line:column`, for plan errors. |
| `fix` | A corrected YAML snippet, for targeted plan errors. |
| `detail` | Machine-readable fields listed per code in errors.md. |
| `causes` | Nested errors: the last attempt's error, aggregated child errors (in plan order), an attached `finally` error. |
| `context` | Extra context, for example an assertion's `message` and `code` on a condition error. |

- **OC-14.** A run-level `error` is the root node's error, or the budget/shutdown/internal error ([errors.md §6](errors.md#3-run-codes)).

## 6. The event log

`events.jsonl` has one JSON object per line:

```text
{"seq":1,"t":"2026-11-04T01:02:03.120Z","ev":"run_start","run_id":"01JC9X4M…","plan_sha256":"9f2c41…","inputs":{"BRANCH":{"value":"feature/x","source":"cli"}},"inputs_sha256":"4be0d7…"}
{"seq":2,"t":"2026-11-04T01:02:03.131Z","ev":"node_start","node":"/do/0","name":"find run id"}
{"seq":3,"t":"2026-11-04T01:02:04.402Z","ev":"iteration_end","node":"/do/0","iteration":0,"exit_code":0,"condition":false}
{"seq":41,"t":"2026-11-04T01:22:17.650Z","ev":"run_end","status":"succeeded"}
```

- **OC-15.** Every event has `seq` (monotonic, starting at 1, no gaps), `t` (RFC 3339 UTC with milliseconds) and `ev`. The first event of a run's log carries `schema_version`.
- **OC-16.** Event types: `run_start`, `run_end`, `node_start`, `node_end`, `attempt_start`, `attempt_end`, `iteration_start`, `iteration_end`, `leaf_start` (program, pid), `leaf_end` (exit code, signal), `kill` (target, signal or method, reason), `probe` (command, exit code, result), `condition` (node, construct, result), `log` (level, message), `capture` (node, names), `warning`, `mode` (`budget`, `shutdown`, `force`), `finally_start`, `finally_end`. New types MAY be added; consumers MUST ignore unknown types and fields.
- **OC-17.** Readers MUST tolerate a truncated last line (the log is flushed per event and may be cut by a crash).
- **OC-18.** Events carry masked values only.

## 7. Leaf output

- **OC-19.** **Tails** keep the end of each stream: the last 50 lines, then at most the last 4 KiB of those, cut at a UTF-8 character boundary; decoded with the leaf's `encoding`, invalid sequences as U+FFFD; masked. Free config keys set both limits ([ADR 0003](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0003-execution-defaults.md)).
- **OC-20.** **Logs** keep the raw bytes, masked while streaming. At `max_log_bytes` (default 100 MiB per leaf) or the per-run cap (1 GiB), logging stops with a final truncation marker line, `log_truncated` is set and a warning goes to stderr; pipes are still drained into the tails.

## 8. Truncation of the stdout report

- **OC-21.** The stdout report is limited to the report byte budget (default 32 KiB, a free config key). `report.json` is never truncated.
- **OC-22.** If the serialized report is larger, Blokky applies these steps in order, re-measuring after each, until it fits:
  1. **Collapse succeeded subtrees:** the children of a `succeeded` node whose subtree contains no failure, warning or ignored failure are replaced by `"collapsed": <count>`, deepest subtrees first, then in reverse plan order.
  2. **Aggregate iterations:** `items` of attempts and iterations keep the first 3, the last 3 and every failed one, the rest summarized as `"omitted": <count>`.
  3. **Shrink tails:** tails are cut to 1 KiB, then 256 bytes, keeping the end; failed leaves are shrunk last.
  4. **Drop `logs` and `probes`** of succeeded nodes.
  5. **Drop node entries** below depth 2 except the path to each failure.
- **OC-23.** The steps are deterministic: the same report always truncates the same way. `truncation` records the steps applied and the counts removed, and always includes `"full": "<path to report.json>"`.

## 9. Versioning of the contract

- **OC-24.** The report, the pre-run envelope and the event log share `schema_version` (`MAJOR.MINOR`, starting at `0.1`). Adding fields, event types or enum values bumps the minor version; anything else bumps the major. While the spec is 0.x, minor versions MAY also break.
- **OC-25.** Consumers MUST ignore unknown fields.
