# Spike 4: schema and editor behaviour

Spike plan row 4 ([spike-plan.md](../spike-plan.md)). Question: how does yaml-language-server (the server behind VS Code's YAML extension) type unquoted scalars and validate them against `schema/blokky.schema.json`, and do completion and hover give useful text? Rules: SY-23 to SY-27, SY-89, SY-95.

Date: 2026-11-07. Spike code: `spikes/schema-editor/` on branch `spikes/schema-editor` (unmerged).

## Setup

- yaml-language-server 1.25.0 (npm), which parses with eemeli/yaml 2.8.3 and validates JSON Schema 2020-12 natively. Schema: blokky-specs `main` at `1.0.0-draft.5`.
- `lsp_probe.py` (uv script) drives the server over stdio: `initialize`, settings via `workspace/configuration` (`yaml.schemas: { <schema file URI>: "*.blokky.yaml" }`, `yaml.yamlVersion`, schema store off), `didOpen` per file, collects `publishDiagnostics`, then `textDocument/completion` and `textDocument/hover` at fixed positions.
- `run.sh` runs 45 case files (`cases/`) and all 57 example plans from blokky-specs, once with `yamlVersion: 1.2` (the default) and once with `1.1`. Raw results: `out/*.json`.
- `typing.cjs` calls the server's own parser (`yamlParser07.parse`) to show the JSON type it gives each plain scalar (`out/typing.md`).
- `patch.jq` applies the proposed schema changes to a copy, re-run on the affected cases (`out/patched-1.2.json`).

## How the server types plain scalars

The schema validates the server's typed value, not the source text. Both YAML versions turn numbers into JSON numbers, so the original spelling is lost before validation.

| Plain scalar | yamlVersion 1.2 (default) | yamlVersion 1.1 |
|---|---|---|
| `1.10` | number 1.1 | number 1.1 |
| `1.0` | number 1 | number 1 |
| `on`, `yes`, `y` / `off`, `no`, `n` | string | boolean true / false |
| `True`, `TRUE` | boolean true | boolean true |
| `010` | number 10 | number 8 (octal) |
| `08` | number 8 | number 8 |
| `0x10` | number 16 | number 16 |
| `0o10` | number 8 | string |
| `1e3` | number 1000 | number 1000 |
| `3.0`, `+1` | number 3, number 1 | same |
| `1_000` | string | number 1000 |
| `~`, `null`, `Null`, empty | null | null |
| `30`, `30s` | number 30, string | same |

## Cases

Diagnostics are identical for 1.2 and 1.1 except where marked. "Blokky" is what the spec requires; "Server" is what the editor shows. **FP** = the editor flags a valid plan; **FN** = the editor is silent on an invalid one.

| # | Case | Blokky (spec) | Server, yamlVersion 1.2 | Verdict |
|---|---|---|---|---|
| 01 | inputs `default: 1.10` | string `1.10` (SY-26, SY-43) | no diagnostic; typed as 1.1 | OK (scalar accepts numbers) |
| 02 | `default: on` / `yes` / `no`; `in: [y, n]` | strings | no diagnostic (1.1: booleans, still accepted) | OK |
| 03/04 | `fail_fast: yes` / `on` | SY-53 error | `Incorrect type. Expected "boolean".` **1.1: silent** | OK in 1.2, FN in 1.1 |
| 05 | `fail_fast: True` | SY-53 error | silent (core schema: `True` is boolean) | FN |
| 06/07/08/09/10 | `max: 010`, `0x10`, `1e3`, `3.0`; `concurrency: 08` | SY-54 error | silent (numbers 10, 16, 1000, 3, 8) | FN |
| 38 | `ok_exit: [0, 0x1]` | SY-54/SY-96 error | silent | FN |
| 43 | `backoff: { factor: 1.50 }` | valid decimal | silent | OK |
| 11 | inputs `default: ~` / `null` / `Null` | valid strings | `Incorrect type. Expected one of string, number, boolean.` | FP |
| 12 | `in: [none, null, ~]`; `exec: [echo, null, ~]` | valid strings | same error on `in` items; `exec` items silent | FP (in), OK (exec) |
| 40/41 | `env:` / `set:` values `1.10`, `on`, `~` | valid strings | error on `~` only | FP on `~` |
| 13 | `exec: [ -f x ]` | SY-89 warning | silent | FN (warning only) |
| 14 | `script: [ -f x ]` | SY-89 error | `Incorrect type. Expected "string".` | OK, generic text |
| 15 | `timeout: 30` | SY-57 error, fix-it `30s` | `Incorrect type. Expected "string".` | OK, unhelpful text |
| 16 | `timeout: 30s` | valid | silent | OK |
| 17 | `timeout: 1.5s` | SY-57 error | `String does not match the pattern of "^(?=[0-9])…$"` | OK, raw regex in message |
| 18 | `blokky: 1.0` | valid | silent (number 1) | OK |
| 19 | `blokky: 1.10` | version 1.10 (SY-51) | silent (number 1.1) | OK by luck |
| 20/22 | `blokky: 2`, `blokky: 1e0` | SY-51 error | silent (any number passes) | FN |
| 21 | `blokky: 1.0.0` | SY-51 error | pattern error | OK |
| 23 | `timeout:` (empty) | SY-20 error | `Incorrect type. Expected "string".` | OK, generic text |
| 24 | inputs `A:` (empty) | SY-20 / SY-41 | `Incorrect type. Expected "input".` plus `Matches a schema that is not allowed.` | OK, confusing text |
| 25 | `exec:` (empty) | SY-20 | `Expected "string \| array".` | OK |
| 26 | `name: { a: b }` | error | `Expected one of string, number, boolean.` | OK |
| 27 | `exec: { cmd: make, timeout: 5m }` | SY-91 targeted error | `Expected "string \| array".` | OK, no fix-it |
| 28 | input `description: 1.0` | valid string | `Incorrect type. Expected "string".` | FP |
| 29 | input `description: yes` | valid string | silent; **1.1: `Expected "string"`** | OK in 1.2, FP in 1.1 |
| 30 | `lock: 2024` | valid lock name | `Expected "string \| object(Blokky plan)".` | FP |
| 31 | `exec: echo done # tidy` | SY-89 warning | silent | FN (warning only) |
| 32 | `matches: "^\d+$"` | SY-89 error | YAML error `Invalid escape sequence \d` | OK |
| 33 | `exec: "a" b` | SY-89 error | YAML error `Unexpected scalar at node end` | OK |
| 34 | `retry: 3` | SY-91 targeted error | `Value must be true \| "true".` | OK, no fix-it |
| 35 | duplicate `exec:` | SY-18 | `Map keys must be unique` | OK |
| 36/37 | anchors/aliases, `!!str` tag | SY-14, SY-15 | silent | FN (parser-level, expected) |
| 39 | `exec: [sleep, 1.10, on, 010]` | strings `1.10`, `on`, `010` | silent | OK (values reach Blokky as written) |
| 42 | `when: { var: A, equals: 1.10 }` | compares `1.10` | silent | OK |
| 44 | `ignore_errors: 1` | SY-53 | `Expected "boolean \| string".` | OK |
| ex | 57 example plans | valid | **no diagnostics**, in 1.2 and 1.1 | OK |

## Completion and hover

- **Keys:** completion lists every valid key at the root and in nested nodes, in schema order, without already-present keys. Most items have no documentation: only keys whose `$def` has a `description` show text (durations, conditions, `scalar`, `setMap`).
- **Values:** enums complete well (`shell`, `level`). Boolean fields offer four items, `"true"`, `"false"`, `true`, `false`, because `bool` is `boolean` or the strings `"true"`/`"false"`. `doNotSuggest` on the string branch did not remove them (it applies to properties only).
- **Hover:** the heading is always the root `title`, "Blokky plan" (or "Blokky plan || Blokky plan" for `anyOf` keys), then the `$def` description if any, then allowed values. For `fail_fast` it shows only `"false"`, `"true"`. A `description` placed next to a `$ref` (allowed in 2020-12) does show on hover, so per-key text works without restructuring the `$defs`.
- **Messages:** `errorMessage` and `patternErrorMessage` (VS Code JSON extensions) are honoured on a `$def`: case 15 becomes "A duration is a string with units: 30s, 5m, 1h30m. A bare number needs a unit (SY-57)." An `errorMessage` on an `anyOf` parent was **not** used (case 19/20 kept `Expected "string"`).

## Schema association pitfall

`yaml.schemas` globs are matched with picomatch with dot segments disabled: `*.blokky.yaml` does **not** match a plan under any dot directory (`.github/blokky/deploy.blokky.yaml`, or this worktree's `.claude/` path). There is no diagnostic; the file is silently unvalidated. Adding `**/.*/**/*.blokky.yaml` fixes it (verified). A `# yaml-language-server: $schema=…` modeline works anywhere, and is a comment, so Blokky accepts it.

## Answers

1. **Typing:** the server types every unquoted scalar with YAML core-schema rules before validating. `1.10` is 1.1, `010` is 10 (8 in 1.1), `~`/`null` are null. The schema's "also accept JSON numbers and booleans" design (SY-95 note in the schema description) works for string fields that `$ref` `scalar`: `default: 1.10`, `exec` items, `env`/`set` values give no false errors.
2. **What the editor can't see:** spelling inside numbers and booleans is gone (`010`, `0x10`, `1e3`, `3.0`, `True`, `blokky: 2`). The schema can't fix this; only Blokky's own validator (SY-24, SY-25, SY-51) catches them. The editor is a convenience, not the validator (in line with the security posture).
3. **yamlVersion:** 1.2 is better on every case. 1.1 hides `yes`/`on` in boolean fields (FN) and breaks `description: yes` (FP).
4. **Completion/hover:** structurally useful (keys, enums), textually thin: almost no per-key descriptions.
5. **Example plans:** all 57 validate clean in both modes.

## Proposed schema changes (specs PR)

Each was tested on a patched copy unless marked.

1. **String fields that take free text accept any scalar** (fixes FP 28, 29 and a numeric `matches: 123`):

   ```json
   "input": { "properties": { "description": { "$ref": "#/$defs/scalar" } } }
   ```

   The same for condition `matches`/`not_matches` and input `matches` (untested, same mechanism).
2. **Pattern fields that can look numeric accept integers** (fixes FP 30; untested): `lock` name, `lock.name`, `tempdir.prefix`:

   ```json
   { "anyOf": [ { "type": "string", "pattern": "^[a-z0-9][a-z0-9._-]{0,63}$" },
                { "type": "integer", "minimum": 0 } ] }
   ```

   Caveat: `lock: 010` passes as 10 while Blokky reads `010`, which is also a valid lock name, so nothing is lost.
3. **Readable messages** on grammar `$defs` (tested on `duration`):

   ```json
   "duration": { "...": "...",
     "errorMessage": "A duration is a string with units: 30s, 5m, 1h30m. A bare number needs a unit (SY-57).",
     "patternErrorMessage": "Durations are <integer><unit> parts in h, m, s, ms order: 500ms, 10s, 1h30m. No fractions: 1500ms, not 1.5s (SY-56, SY-57)." }
   ```

   Likewise `patternErrorMessage` for `varName`, `envName`, `userCode`, lock names and `specVersion`.
4. **Per-key descriptions** next to each `$ref` in `root.properties` and `node.properties` (tested on `fail_fast`), e.g.

   ```json
   "fail_fast": { "$ref": "#/$defs/bool",
     "description": "Stop the other children of all/any/race as soon as one fails: true or false." }
   ```

   Also `title` on `$defs` so hover headings name the field rather than "Blokky plan". Text is non-normative and must not restate rules in a way that can drift from reference.md; one sentence per key plus the rule ID.

Not proposed: adding `null` to `scalar`. An empty value (`timeout:`, SY-20) is also null, so accepting null would hide the more common mistake to rescue the rare `default: ~`. Not proposed: restricting the `specVersion` number branch to `enum: [1]` (tested): it catches `blokky: 2` but hover then shows "Allowed values: `1`", `blokky: 1.10` becomes an error, and Blokky reports SY-35 anyway.

## Proposed spec changes

- **SY-89:** add a row: plain `~`, `null`, `Null`, `NULL` in a string field. Valid (a string), but editors show a type error. Detection: warning with the fix-it to quote (`'~'`). Optional: a matching row for plain numbers with a leading zero or a fractional `.10` in string fields (`default: 1.10` displays as 1.1 in tools), as a SHOULD-quote style note in SY-90 rather than a warning.
- **SY-95** (or the schema's description): state that editors validate typed values, so the schema cannot detect number/boolean spelling (`010`, `True`, `1e3`), and Blokky's validator is authoritative.

## Recommended editor settings (for user docs)

VS Code `settings.json` (Red Hat YAML extension):

```json
{
  "yaml.yamlVersion": "1.2",
  "yaml.schemas": {
    "https://<published schema URL>/blokky.schema.json": ["*.blokky.yaml", "**/.*/**/*.blokky.yaml"]
  }
}
```

- Keep `yaml.yamlVersion` at `1.2` (the default); never `1.1`.
- Include the dot-directory glob, or put `# yaml-language-server: $schema=<url>` on the first line of plans in dot directories.
- Run `blokky validate` (or the hook) for the rules the editor can't see.

## Open points

- **Published schema URL:** `$id` is a URN (`urn:blokky:schema:1.0:plan`), which editors can't fetch. User docs need an HTTPS URL (raw GitHub per tag, or SchemaStore registration for `*.blokky.yaml`, which would make the association automatic). Track as a new task.
- **Hover/completion in VS Code itself:** verified through the language server only; no screenshots. The VS Code extension uses the same server, but its UI rendering was not checked.
- **Duplicate boolean completions** (`"true"` and `true`): no schema keyword removes them; accept, or drop the string branch of `bool` (then a quoted `'true'` becomes an editor FP).
- **JetBrains IDEs** use their own YAML/JSON-schema engine; not tested.
- **Untested proposals:** 1 beyond `description`, and 2; the specs PR should re-run `spikes/schema-editor/run.sh` against the changed schema.
