# Spec gaps from conformance wave C

Status: decided 2026-11-07: G-1 and G-2 option (a), and the batch K-1 to K-7, as proposed. Applied as [specs ADR 0023](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0023-wave-c-gaps.md), which also states that a backslash-escaped `$` or backtick is literal (SH-5). Three writer agents wrote the wave C cases (T-0063): condition subjects and operators, composites and probes, and shells. Three checker agents then read every case against the spec. The wrong cases have been fixed; most were columns counted before the plan's indentation was stripped, and the case checker now catches positions that land on whitespace. **G** items are choices; **K** items have one clear answer and are proposed as a batch.

## Decisions

- **G-1. Glob semantics left open** (reference.md §5.7, no rule IDs). Four points are open: whether directories match, whether `*`/`**` enter dot-directories, whether a symlink to a file matches, and what a `..` that leaves the cwd does. Options:
  - **(a)** (recommended):
    - a glob matches files and directories, as shells do;
    - `*` and `**` never enter or match dot-entries unless the pattern segment starts with `.`;
    - a symlink is matched as an entry but never followed into;
    - a literal `..` segment is a plan error (`BK_PLAN_VALUE`).

    These rules get IDs SY-99 to SY-102.
  - (b) files only; everything else as in (a).
- **G-2. How shells and programs are resolved through symlinks** (SH-29, results.md `program`). Options:
  - **(a) Resolution doesn't follow symlinks.** `program` is the path found on PATH, and the process starts under that path, so a symlinked tool keeps its name (recommended: what `bash -c` does, and what testkit aliases need).
  - (b) `program` is the canonical path, and runners must create testkit aliases by copying or hard-linking.

## Batch of corrections

- **K-1. Rule IDs and codes.**
  - CO-4 and SY-91 overlap; SY-91 is reported. CO-8 and SY-53 overlap; SY-53 is reported.
  - A probe shape violation is `BK_PLAN_INVALID` with rule CO-26. The schema gets `shell` only with `script` in probes.
  - A node used as a condition (`when: { exec: … }`) is `BK_PLAN_INVALID` at the kind key.
  - The operator table's "valid subjects" column becomes rule CO-47.
  - `ignore_case` also works on the negations (`not_equals`, `not_in`, `not_contains`, `not_matches`) (CO-12).
  - A missing subject or operator is CO-4's "anything else", not CO-1.
  - One mistake is one error (SY-3), so a `{ var: X }` in a literal field is RF-9 alone. Cases may use `exhaustive: true` there, and `syntax/values/reference-in-literal-field` is tightened to match.
  - New rule IDs for statements that lack them:
    - the subject table (§2), for example `env` sees the effective environment and `path` is relative to the evaluating scope's cwd: CO-48;
    - the meaning of `exists`, `is_file` and `is_dir`: CO-49;
    - "`not_*` negates its operator": CO-50.
- **K-2. Positions.**
  - A missing operator is reported at the subject key.
  - An error in a block scalar is reported at the first character of its first content line.
- **K-3. Condition errors.**
  - An unset environment variable is reason `undefined_variable`.
  - Each CO-38 bullet names its `detail.reason`.
  - CO-40 covers "any condition error caused by the `result:` value".
  - `defined`/`not_defined` on JSON that isn't valid is a condition error (`json_invalid`); only a missing path counts as undefined.
  - A loop or retry ended by a condition error has `ended_by: failure`.
- **K-4. Probes.**
  - A probe's `invocation` follows the leaf rules (masked, element 0 as written).
  - The `probes` list is in execution order.
  - A probe that hits its timeout gets the grace period, like a leaf (EM-26).
  - A bare probe's stdout isn't decoded or checked.
  - The first readiness check happens right after the service process starts; later ones come 250ms after the previous check ends.
- **K-5. `host:`.** A branch decided statically by `host:` is `pruned`, not `not_taken` (CO-7 wins over E-1).
- **K-6. Shells.**
  - **`.bat`/`.cmd` (SH-10):** a literal argv[0] ending in `.bat` or `.cmd` (case-insensitive) is a plan error, `BK_PLAN_VALUE` with rule SH-10. SH-27's extension trial applies to bare names only.
  - **Backslashes (SH-5, SH-7):** a backslash-escaped character counts as quoted; a trailing lone backslash is an SH-7 error.
  - **Windows quoting (SH-9):** quoting follows `ProcessStartInfo.ArgumentList` (the Microsoft C runtime rules), normatively.
  - **Script `invocation`:** element 0 is the resolved shell path.
  - **`param(` (SH-17):** the check applies only when the shell is named `pwsh` or `powershell`, by `shell:` or `defaults.shell`; it doesn't apply to a detected shell.
  - **Windows prologue (SH-34):** the prologue states its encoding setting in full, `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false);` (each statement followed by a space), after `$ErrorActionPreference = 'Stop';`.
- **K-7. Conformance format.**
  - CF-57: conformance runs use no implementation configuration, such as shell paths.
  - The testkit gets `chmod MODE PATH` (Unix), so `path_access` errors become testable.
  - New exemptions:
    - CO-10 and CO-15 (reported under RF-14 and RF-22);
    - CO-32 (advice) and CO-37 (a warning);
    - SH-8 and SH-19 (static views), SH-20 (provisional) and SH-30 (a MAY);
    - SH-11's "readable only by the run's user" part (file modes aren't portable).
