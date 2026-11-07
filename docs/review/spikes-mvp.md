# Decisions from the MVP spikes

Status: decided 2026-11-07: rounds 1–2 as listed, T-1 option (a), and the batch, as proposed. Applied as cli [ADR 0008](../adr/0008-spike-findings.md) and [specs ADR 0027](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0027-spike-findings.md) (T-0030). Spike reports: [design/notes](../design/notes/).

The maintainer decided rounds 1 and 2 by questionnaire on 2026-11-07:

- the YAML front end is YamlDotNet behind an adapter;
- the pwsh minimum is 7.4, and CI tests 7.4 plus the current release;
- regex sizes are bounded in the spec;
- the watchdog is the lifeline: if it dies the run fails, and PDEATHSIG is not used;
- tests use TUnit;
- the Linux baseline is glibc 2.36 or later, with no musl build yet;
- a release ships only when 100% of its in-scope cases pass;
- the schema is published at a versioned raw URL.

This page lists the rest as a batch (S items) and one choice (T-1).

## Choice

- **T-1. Colour codes in captured output.** pwsh writes ANSI colour codes into captured stderr when `TERM` looks like a colour terminal, even without a terminal. `TERM=dumb` stops it on every version; `NO_COLOR` works only on 7.6. Other tools behave alike. Options:
  - **(a)** Blokky sets `NO_COLOR=1` for every leaf, and `TERM=dumb` when the leaf's environment has no `TERM` (recommended). This is a new entry in the variables Blokky sets (VA-44), and the plan's `env` can override either.
  - (b) Only pwsh leaves get `TERM=dumb`.
  - (c) Nothing; plans set `TERM` or `NO_COLOR` themselves.

## Batch

**Spec (blokky-specs):**

- **Provisional markers:** EM-22, EM-68 and SH-29 drop "provisional". In EM-68, "stopped" means no live process of the grouping is left.
- **Executable resolution:**
  - SH-25: empty and relative `PATH` entries are skipped.
  - SH-26: a relative path is joined to the cwd as written, without collapsing `..`.
  - SH-29: argv[0] is the name as found.
- **Spawn failures:** `BK_SPAWN_FAILED` `bad_format` covers a file the OS can't execute, with no fallback through `/bin/sh`.
- **Run-owned files:** EM-69: a removal stopped by a mount point is reported as partial.
- **Plan text:**
  - SY-12: U+2028 and U+2029 are rejected by the character lint.
  - SY-7: the offset counts from the first raw byte, BOM included.
  - SY-17: an empty key is a rejected complex key.
  - SY-20: an empty sequence item (`-`) is an empty value.
- **Regex:**
  - SY-61: the lexical edges (a bare `]` or `}` is literal; `-` is literal at a class's start or end; `\f` and `\v`; the escapes allowed inside classes).
  - `(?i)` doesn't change `\w`, `\d` or `\s`.
  - A class is case-folded before negation.
  - CO-12: case mapping follows Unicode 16.0.
- **PowerShell:**
  - SH-17, the prologue:
    - keywords are matched case-insensitively;
    - when a line closes a block comment, the prologue goes after `#>`;
    - a `param` block written over several lines, or after attributes, is detected;
    - top-level named blocks (`begin`, `process`, `end`, `clean`, `dynamicparam`) are a plan error;
    - a script with no statements gets no prologue.
  - SH-18 notes the 7.4+ opt-in `$PSNativeCommandUseErrorActionPreference`.
  - SH-20 becomes normative: the implicit exit code is 0, `throw` gives 1, and `exit N` gives N mod 256.
- **Conformance:** new cases for ı/I, `(?i)[a-z]` against ſ, `\B` on `aéb`, an astral `.`, the regex size bound, and the prologue edge cases.
- **Editor guidance:** SY-89 gets a row for an unquoted `~` or `null` in a string field. SY-95 says editors can't see how numbers and booleans were spelled.
- **Schema:**
  - input `description` (and the regex fields) accept any scalar;
  - lock names and `tempdir.prefix` also accept integers;
  - `errorMessage` text on the grammar `$defs`;
  - a short description on every key.

**CLI (blokky-cli):**

- **Run-control contract:**
  - RC-3 names `posix_spawn` with `SETSID` and the signal reset.
  - RC-6 states the Linux guarantee (watchdog, marker, subreaper) and its gaps.
  - A new rule on escapees and the reserved `BLOKKY_RUN_*` variables.
  - RC-5 states when `descendants_held_output` is set.
  - Watchdog loss stops the run with reason `lifeline_lost`.
- **Install docs:** the schema URL, and the recommended editor settings (`yaml.yamlVersion: 1.2`, plus a glob that covers dot directories such as `.github/`).
- **Design defaults** (recorded in the design docs, can change later):
  - command-line parsing uses System.CommandLine, with a hand-written fallback only if `--opt=value` can't be rejected;
  - the clock mode can be selected only in test builds;
  - fuzzing uses SharpFuzz;
  - the pinned conformance version is bumped by hand;
  - SBOMs are SPDX, made by syft;
  - the tag key check (T-0040) uses the maintainer's published GPG key;
  - the testkit logs every process it starts, `spawn --escape` intermediates included.
