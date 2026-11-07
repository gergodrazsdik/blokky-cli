# Spec gaps from conformance wave D

Status: decided 2026-11-07: H-1 and H-2 option (a), and the batch L-1 to L-6, as proposed. Applied as [specs ADR 0024](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0024-wave-d-gaps.md), which adds EM-70 (node durations) and CF-65 (the no-result outcome). Three writer agents wrote the wave D cases (T-0064): time limits and repetition timing, cancellation and external stops, and modifier order, locks and services. Three checker agents read every case against the spec and found no wrong expectations; one fragile duration bound and a duplicate lock case between two areas are fixed. Each gap's premise was checked against the cited text. **H** items are choices; **L** items have one clear answer and are proposed as a batch.

## Decisions

- **H-1. A node whose own `attempt_timeout` fired, and which then failed.** The status table (execution-model.md §8.1) says such a node is `timed_out`, but a merged case expects a retry whose attempts all timed out to end `failed` with `BK_RETRIES_EXHAUSTED`. Options:
  - **(a) `timed_out` only when the node's own error is a timeout code** (`BK_TIMEOUT_TOTAL`, `BK_TIMEOUT_ATTEMPT`); a retry or loop that ends with `BK_RETRIES_EXHAUSTED` or `BK_LOOP_MAX_REACHED` is `failed`, and its repetition entries ended by `attempt_timeout` are `timed_out` (recommended: the status matches the error, and the merged case stands).
  - (b) `timed_out` whenever the node's own `attempt_timeout` fired in any attempt.
- **H-2. When `finally_timeout` fires.** No code, `killed_by` value or status is defined for it. Options:
  - **(a) a new code, `BK_TIMEOUT_FINALLY`**, attached to the node's error like any `finally` error; the `finally` node that was stopped is `cancelled` with `killed_by: finally_timeout` (recommended: a hung cleanup is told apart from a slow body);
  - (b) reuse `BK_TIMEOUT_TOTAL`, with `killed_by: finally_timeout`.

## Batch of corrections

- **L-1. Statuses and errors.**
  - Timeout codes (errors.md §5.2): a per-attempt `defaults.leaf_timeout` fails the attempt with `BK_TIMEOUT_ATTEMPT` and `killed_by: attempt_timeout`. The `BK_TIMEOUT_TOTAL` row says "except on repeating leaves (ADR 0018 S-9)".
  - When the node's own `timeout` fires during an attempt, the attempt that was interrupted is the last attempt, and its error, `BK_CANCELLED`, is the one nested (EM-53).
  - `ended_by` gets `cancelled`, for a repetition ended by cancellation or an external stop. `count` includes the attempt or iteration that was cancelled.
  - A `for` stopped by `fail_fast` ends with `ended_by: failure`. One that ran all items ends with `items`, even if some failed.
  - For a `stopped` run, the run's error is the root's `BK_CANCELLED`, and `detail.reason` names the cause.
  - `ignored` is absent unless `ignore_errors` hid a failure.
- **L-2. Durations.** A node's duration runs from its start (after its `when` passed) to its completion, and includes lock wait, setup, services and `finally`. A leaf's duration ends as EM-68 says.
- **L-3. Repetition timing.** In `while`, the `every` wait comes first, then the condition check, right before each iteration.
- **L-4. Services.**
  - The `services` result lists the services in list order. Services that never started are absent.
  - A service that exits before it is ready ends `exited`, with its `exit_code`. One stopped by a timeout or cancellation during startup ends `never_ready`.
  - `ready_after` is measured from the node's start (results.md, as in CF-26).
  - When services fail to start (EM-63), the body and `finally` nodes are `skipped` with `not_reached`, and `finally_status` is absent.
  - EM-15's console requirement also covers services and probes, so their graceful stop works on Windows.
- **L-5. Locks and tempdirs.**
  - Two nodes of the same run that use the same lock name exclude each other, as two runs do; the order among waiters is unspecified (EM-60, reference.md §4.4).
  - `tempdir: { remove: false }`, the default, keeps the directory.
- **L-6. Conformance format.**
  - A case with a `kill` action may use `expect: { no_result: true }`, optionally with `files` and `invocations`, and checks what survives with `after_run`. Such cases list the `kill` feature, so implementations without that support skip them (CF-12).
  - The testkit's `sleep` gets `--tag TEXT`, which prefixes each recorded signal line, so cases can show EM-65's reverse stop order.
  - New exemption: EM-32's survivor clause (the testkit can't make a process that survives a forced kill).
  - Partial notes: EM-60's per-user scope needs a second OS user, and `graceful: none` needs a program that detaches from its console.
