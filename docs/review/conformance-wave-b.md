# Spec gaps from conformance wave B

Status: decided 2026-11-07: E-1 and E-2 option (a), and the batch C-1 to C-5, as proposed. Applied as [specs ADR 0022](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0022-wave-b-gaps.md). Three writer agents wrote the wave B run-semantics cases (T-0062): kinds and status, leaf processes, and variables at run time. Three checker agents then read every case against the spec, and the wrong cases have been fixed. Before listing a gap, the main agent checked its premise against the cited text and ADRs. One reported gap was dropped as a misreading: EM-46 already says that an `any` whose children all failed with `ignore_errors` fails. **E** items are choices; **C** items have one clear answer and are proposed as a batch.

## Decisions

- **E-1. Nodes in an `if` branch that didn't run.** RS-3 says the result mirrors the plan, but no skip reason fits these nodes: `not_reached` means "the parent ended early". Options:
  - **(a) they are present, `skipped` with a new skip reason `not_taken`** (recommended: the tree stays complete, and a reviewer sees the path not taken);
  - (b) they are absent from the result.
- **E-2. A `finally` list with a failing node.** Only `do` has `fail_fast`. EM-67 already treats a `finally` list as a sequence for `return`, but nothing says whether a failure stops the rest of the list. The same question applies to `then` and `else`. Options:
  - **(a) `then`/`else` stop at the first failure, like `do`; `finally` runs all its nodes and fails at the end with the first failure** (recommended: cleanup should try every step);
  - (b) all three stop at the first failure, like `do`;
  - (c) all three run every node.

## Batch of corrections

- **C-1. Captures.**
  - VA-21's NUL check is on the decoded text (U+0000), not on the bytes, so UTF-16 output can be captured. CR/LF trimming happens after decoding.
  - VA-22: field captures run the VA-21 checks first, in VA-21's order, NUL included.
  - `BK_CAPTURE_PATH` `detail.path` is the path as written in the plan.
- **C-2. Compact JSON** (VA-23, VA-55, SY-86, TK-12, TK-13) means RFC 8259 text with:
  - no insignificant whitespace;
  - short escapes where they exist, `\u00XX` for other control characters, non-ASCII characters as themselves;
  - numbers as written, and object members in input order.
- **C-3. Results.**
  - RS-6: a node that never started has `id`, `kind`, `name` (if set), `status`, `skip_reason` and the probes its `when` ran, and no `duration`.
  - Repetition entries are listed in index order.
  - The run has an `id` (results.md §2, which the glossary already promises), and the result document gets `id` and `left_running` (`[{ program, pid }]`, for `keep_descendants: detach`).
  - A node's `children` key lists its children's path ids in plan order, so cases can assert plan order (RS-3, EM-13).
  - A group's error is its child's error (EM-66) in code, `detail` and `node`. Causes the group attaches (EM-51, EM-52) belong to the group's error only; the child's recorded error is unchanged.
- **C-4. Leaves.**
  - In `invocation`, element 0 is argv[0] as written; `program` holds the resolved path.
  - `BK_SPAWN_NOT_FOUND` `detail.program` is argv[0] as written.
  - `BK_SPAWN_FAILED` `detail.reason` is one of `not_executable`, `permission_denied`, `bad_format` or `blocked`.
  - `BK_SIGNAL` `detail.signal` is the signal's name (`SIGTERM`).
  - `stdin` text and `{ var }` values are passed as UTF-8 bytes exactly, with no newline added; `{ file }` passes the file's bytes.
  - The drain bound (EM-20) is at least 1s, as the reap timeout is (EM-32).
  - A `return` message is recorded at level `info`.
- **C-5. Conformance format.**
  - CF-22 `after:` is in plan time.
  - CF-40: cases with `spawn`, `actions` or `after_run` default to `clock: [real]`, like cases with `script` leaves, because process start-up doesn't scale.
  - CF-57: conformance runs use the language defaults (`max_capture_bytes` and the other configurable bounds).
  - TK-16 is unchanged: `alive` on a missing file exits 124, a broken case; the clock rule above keeps that from flaking.
  - The testkit gets `symlink TARGET LINK` (Unix; 123 on Windows), so EM-69's no-link-following rule becomes testable.
  - New exemptions: RS-4 (cases match subsets), VA-53 (static views aren't in the run result).
