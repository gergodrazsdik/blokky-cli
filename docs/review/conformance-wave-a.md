# Spec gaps from conformance wave A

Status: decided 2026-11-07: D-1, D-2 and D-4 option (a), and the batch B-1 to B-10, as proposed; D-3 withdrawn (below). Applied as [specs ADR 0021](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0021-wave-a-gaps.md). Five writer agents wrote the wave A validation cases (T-0061) in five areas: syntax text/YAML/keys, syntax grammars/values, reference rules, the generated validity matrix, and variables at validation. Five checker agents then read every case against the spec. Wrong cases have been fixed. What remains are places where the spec doesn't decide an outcome, so the cases leave it out. **D** items are choices for the maintainer. **B** items have one clear answer and are proposed as a batch.

## Decisions

- **D-1. One mistake, several rule IDs.** Many checks have two or three IDs. Examples: RF-7/VA-36/SY-85, RF-6/VA-37, RF-15/VA-48/SY-39, RF-23/RF-27/VA-38, RF-9/RF-27/SY-76, RF-20/SY-45/SY-46, RF-16/VA-35, RF-19/VA-49. In addition, RF-3 covers what RF-4, RF-12, RF-28, RF-30, RF-31 and RF-32 say more precisely. As a result, `detail.rule` isn't determined. Options:
  - **(a) The reference.md §8 rule is canonical** (recommended).
    - The other statements say "reported as RF-n".
    - Overlaps inside §8 get a precedence: the specific rule wins over RF-3; RF-7 over RF-26 when an input or an enclosing scope is involved; RF-27 for a template in the wrong field over RF-23 for an undefined name; RF-9 for `${}` in a literal field over RF-27.
  - (b) Merge the restatements into one rule each.
  - (c) Cases accept any of the IDs (`$one_of`).
- **D-2. Which code.** `BK_PLAN_UNKNOWN_KEY`, `BK_PLAN_INVALID` and `BK_PLAN_VALUE` overlap.
  - For example, `level` on `exec` is "a key not valid at its position", but RF-32 makes it INVALID.
  - Other unclear cases: an empty list (`BK_PLAN_VALUE` or RF-21), a regex outside the subset (RF-22 or a grammar error), the `BLOKKY_` prefix, a `BK_` user code, a reference in a literal field, a bad name used as a key.

  Proposal (a) (recommended):
  - **UNKNOWN_KEY**: a key that no kind accepts in that kind of mapping.
  - **INVALID**: a language key in the wrong place, and every rule relating keys, values or scopes.
  - **VALUE**: anything decided by one value alone: grammar, type, empty value or list, a regex outside the subset or not compiling, reserved prefixes.
  - Both INVALID and VALUE carry `detail.rule`.
  - RF-21 and RF-22 become VALUE.
- **D-3. A loop on a leaf bounded only by `timeout`.** *Withdrawn after the decision: the premise was a misreading. ADR 0018 (S-9) made only `defaults.leaf_timeout` per-attempt; a node's own `timeout` always limits the whole node. reference.md §4.3 now names `defaults.leaf_timeout` instead of an ambiguous "it".* On a leaf with `while`/`until`, `timeout` limits each iteration (reference.md §4.3, ADR 0018), yet RF-5 accepts it as the loop's bound, so the loop can run forever. Options:
  - **(a) RF-5: a leaf `while`/`until` needs `max`** (recommended).
  - (b) On a leaf, `timeout` with `while`/`until` is the total limit again, and only `retry` keeps the per-attempt meaning.
- **D-4. A `return` with no enclosing sequence.** Examples: a root `return`, a root `if` whose `then` returns, a `return` under a root `all`. Options:
  - **(a) It is a plan error under RF-29** ("a `return` has an enclosing sequence, not reached through `all`/`any`/`race`") (recommended).
  - (b) The root node always counts as a sequence, so the `return` ends the run successfully.

## Batch of corrections

- **B-1. Positions (SY-1).**
  - Columns are 1-based code points. A UTF-8 BOM isn't counted. Lines end at LF, CRLF or a lone CR.
  - Whole-text errors (encoding, size) are at 1:1. The depth error is at the first node that is too deep.
  - A quoted scalar is located at its opening quote. An error inside a string (`${X}`, a regex) is located at the value's start.
  - A missing value is located at its key. In a conflict between two keys, the later key in document order is reported.
- **B-2. Invalid text.**
  - Invalid UTF-8 is one error, at the first bad byte, with `detail.offset` (SY-7).
  - A UTF-16/32 BOM gives only the SY-6 error.
  - Character-lint errors stop processing before YAML parsing (SY-10), so no `BK_PLAN_YAML` follows them.
  - `detail.codepoint` is written `U+XXXX`.
- **B-3. Details and messages.**
  - `BK_PLAN_VERSION` always carries `detail.supported`.
  - For a multi-part synonym (`loop`, `retries`), `detail.suggestion` is the key (`for`, `retry`), and the message carries the rest.
  - SY-30's candidate set is every key valid in that mapping, in the order of the schema's `properties` for that mapping.
  - A targeted error (SY-91) replaces the generic error for the same mistake.
- **B-4. Warnings.** They stay unobservable in 1.0. They have no codes and no result key, and SY-8 joins the exemptions. Revisit them with the CLI's output contract.
- **B-5. Grammar details.**
  - Durations: zero parts inside a duration are fine (`1h0m`), but a duration of zero is only `0s` (SY-58).
  - Globs: `{a,b}` is a plan error, not literal characters (SY-73).
  - `$${` means a literal `${` in every field, so it's never the "`${` in a literal field" error (SY-84).
  - Regex: an escape of a character outside the listed ones (`\q`) is outside the subset (SY-61).
  - JSON paths: no leading zeros in an index (SY-67).
  - Escaped control characters are shown as `\uXXXX`, in uppercase (SY-98).
- **B-6. Names and inputs.**
  - The `BLOKKY_` prefix and a `BK_` user code are `BK_PLAN_VALUE` (SY-52, ER-1).
  - A literal marked secret is RF-19, reported as a targeted error rather than as an unknown key.
  - An `--input` name that breaks the name grammar is `BK_INPUT_SYNTAX`. A well-formed undeclared name, including one that differs only in case, is `BK_INPUT_UNKNOWN`.
  - An unset `from_env` in `vars` is reported together with the plan errors, at its entry, as `BK_FROM_ENV_UNSET`.
  - errors.md's 4 KiB and 32 KiB input limits are normative, and VA-14 follows them.
- **B-7. Masking.** Masking follows the value. A non-secret variable whose value equals a secret's value is masked too, as RS-2 and VA-51 already imply; VA-47 and variables.md §8 say so.
- **B-8. Binding rules.**
  - RF-6 lists `index` and `carry`, as VA-37 does.
  - Shadowing by `index` or `carry` is RF-7.
  - Two sequential loops may use the same `as`, because loop scopes are separate (RF-26).
  - RF-8 ignores loop-scoped names: they never reach the `all`'s scope.
- **B-9. Editorial and schema.**
  - The reference.md §5.8 example has `collect` reversed (`{ PAGE: PAGES }`).
  - The §4 modifier table leaves `return` out of the no-process kinds.
  - §2.6 lacks `race`'s "except inside `for:`".
  - §2.10 should read "an enclosing `retry`".
  - The SY-89 row for `[ -f x ]` has the wrong result.
  - The schema accepts `log` with `for` and `attempt_timeout`, but §7 says "—".
  - §7 should note that `log` × `collect` is effectively "—".
- **B-10. Conformance format.**
  - `inputs` becomes a run-outcome key (§3.6 already maps it).
  - Checker: a string `exec` no longer counts as the `exec` feature; a root that isn't a mapping skips the feature check; a service's array `exec` counts as `exec`.
  - `_typos.toml` ignores `plan_base64` values.
  - New exemptions: SY-4, SY-8, SY-31, SY-60, SY-75, SY-90, SY-94, SY-95, SY-97, VA-15, ER-2.
  - SY-9's 4 MiB cap and the 128-level depth boundary need generated plans, which come with the runner.

## Skipped cells

The matrix has no valid case for `container` on eight kinds: the suite doesn't name an image, and CP-36 needs one that contains `sh`, `sleep` and `kill`. They come with wave E (containers).
