# Leaves and validation

Status: **draft**. Moved from blokky-specs on 2026-11-05 ([ADR 0006](../adr/0006-phase-order-and-incremental-cli.md)); rule IDs are unchanged.

This document is normative for the `leaves` document (what a plan will execute, for humans, rule matchers and validators) and for the validator protocol. How `blokky hook check` uses them (canonical forms, auto-allow, runner-rules mode, approval records) is in blokky-cli `validation-and-permissions.md` ([blokky-cli ADR 0003](../adr/0003-validation-config-lint.md)). The JSON Schemas `schema/leaves.schema.json` and `schema/validator.schema.json` are normative for structure. Normative keywords are used as defined in [blokky-specs syntax.md §1](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/syntax.md#1-conventions). Rules carry IDs (`LV-n`).

## 1. Purpose and guarantees

- **LV-1.** The `leaves` document lists **everything a plan can execute** (every process it may start) and every **plan-dependent effect** (§5), for one concrete invocation: a plan, its inputs and the invocation cwd.
- **LV-2.** It is produced by **static analysis only**. `blokky leaves`, `blokky plan`, `lint`, `config show`, `schema`, `report` and `version` start no process, run no probe, create no file and call no validator.
- **LV-3.** It is rendered by the same **invocation builder** that `blokky run` uses, so what it shows is what runs (given the same environment and files).
- **LV-4.** Blokky doesn't decide what is allowed. The document exists so that the runner's permission system, `hook check` and a validator can decide.

## 2. The document

```json
{
  "schema_version": "0.1",
  "plan": { "sha256": "9f2c41…", "spec_version": "0.1", "source": "/repo/ci/wait-ci.blokky.yaml" },
  "cwd": "/repo",
  "host": { "os": "linux", "arch": "x64" },
  "inputs": { "BRANCH": { "value": "feature/x", "source": "cli" } },
  "inputs_complete": true,
  "inputs_sha256": "4be0d7…",
  "units": [ … ],
  "effects": [ … ],
  "pruned": [ "/do/3" ],
  "warnings": []
}
```

| Field | Meaning |
|---|---|
| `schema_version` | `MAJOR.MINOR`, starting at `0.1`; versioned like the report ([output-contract.md §9](output-contract.md#9-versioning-of-the-contract)). |
| `plan` | `sha256` of the plan bytes, `spec_version`, `source` (`stdin` or the absolute path). |
| `cwd` | The invocation cwd, which relative paths resolve against. |
| `host` | The `os` and `arch` used for `host:` pruning. |
| `inputs`, `inputs_complete`, `inputs_sha256` | As in the report; a missing required input makes `inputs_complete` false ([variables.md §4](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/variables.md#4-plan-inputs)). |
| `units` | Executable units in plan order (§3). |
| `effects` | Plan-dependent effects (§5). |
| `pruned` | Ids of nodes removed because a `host:` condition is false on this host. |
| `warnings` | Lint findings relevant to review (`{ rule, node, message }`). |

- **LV-5.** Conditional branches are **all** listed: both `then` and `else`, every `any` alternative, everything guarded by `when`, `finally` lists. Only statically false `host:` branches are pruned, and they are named in `pruned`.
- **LV-6.** Fixed runtime writes (the state dir, logs, report, script files) are documented here once and not listed per plan.

## 3. Units

A **unit** is one process the plan may start.

```json
{
  "id": "/do/2/all/0",
  "type": "exec",
  "node": "/do/2/all/0",
  "name": "watch run",
  "program": "/usr/bin/gh",
  "argv": ["gh", "run", "watch", { "var": "RUN_ID", "dynamic": true }, "--exit-status"],
  "invocation": ["/usr/bin/gh", "run", "watch", { "var": "RUN_ID", "dynamic": true }, "--exit-status"],
  "rendered": "gh run watch <dynamic:RUN_ID> --exit-status",
  "cwd": "/repo",
  "stdin": { "kind": "none" },
  "env_overrides": [ { "name": "GH_TOKEN", "from_env": "GH_TOKEN", "secret": true } ],
  "export": [],
  "context": { "in_finally": false, "conditional": false, "loop": null, "repetition": null },
  "flags": { "dynamic_exec": false, "exec_env_risk": false, "keep_descendants": false }
}
```

### 3.1 Unit types

| `type` | What it is | `id` |
|---|---|---|
| `exec` | An `exec` leaf. | the node's path id |
| `script` | A `script` leaf. | the node's path id |
| `probe` | A probe in a condition. | node id + `#` + construct and position, e.g. `/do/0#until`, `/do/4#if/all/1` |
| `service` (*later*) | A `services` entry. | node id + `#services/<index>` |
| `derived` (*later*) | A command Blokky issues itself: container engine calls, `sudo`/`doas` wrappers. | node id + `#derived/<index>` |

### 3.2 Fields

| Field | Meaning |
|---|---|
| `program` | The resolved absolute path of argv[0] (or of the shell), or `{ "unresolved": "<name>" }` if it can't be resolved on this host. |
| `argv` | For `exec`: the argv. For `script`: absent. |
| `script` | For `script`: `{ shell, text, file }`: the shell name, the exact text that runs (prologue included) and the script file path the run would use. |
| `invocation` | The complete argv passed to the OS (shell flags and script file for `script`; `derived` wrappers included). |
| `rendered` | A one-line, shell-equivalent rendering (§4). |
| `cwd` | The effective cwd (absolute), or a dynamic marker. |
| `stdin` | `{ kind: "none" }`, `{ kind: "literal", text }`, `{ kind: "var", var, dynamic }` or `{ kind: "file", path }`. |
| `env_overrides` | Every `env` entry in scope: `{ name, value }` for literals, `{ name, var, dynamic }` for variables, `{ name, from_env }` for environment references, each with `secret` if set. Secret values are never resolved. |
| `export` | Every `export` name in scope, with its value or a dynamic marker. |
| `context` | `in_finally`; `conditional` (inside a branch, guard or alternative); `loop` (`{ node, source, items, bound }` when inside a `for`/`while`/`until`, with static items listed and `items: null` when dynamic); `repetition` (`{ kind, max, timeout }` upper bounds); `container` and `become` (*later*). |
| `flags` | `dynamic_exec`, `exec_env_risk`, `keep_descendants` (§6). |
| `nested_plan` | For a nested `blokky run` (§6.3). |

### 3.3 Values

- **LV-7.** A value known statically (a literal, an input with a value, a default, a static loop item when listed per item) appears as a string.
- **LV-8.** A value known only at run time (a capture, a dynamic loop item, a tempdir path) appears as `{ "var": NAME, "dynamic": true }`. A missing required input appears as `{ "input": NAME, "missing": true }`.
- **LV-9.** Secret values never appear; secret references appear by source (`{ "from_env": "GH_TOKEN", "secret": true }`), and `blokky plan` shows them as `TOKEN ← env GH_TOKEN (secret)`.

## 4. The rendered form

The `rendered` string lets a human or a rule matcher read a unit like the equivalent call in the unit's shell.

- **LV-10.** `exec` units and probes render as a POSIX command line: words joined by spaces, each word that needs quoting in single quotes (`'…'`, with `'` written as `'\''`). argv[0] is shown as written in the plan (its resolved path is in `program`).
- **LV-11.** Environment overrides and exports in scope render as a prefix of assignments in the same quoting: `GH_TOKEN=<secret:env:GH_TOKEN> RUN=<dynamic:RUN_ID> gh run view …`.
- **LV-12.** `script` units render as the shell invocation of the script file with the same prefix (`bash --noprofile --norc -o pipefail /…/scripts/do-3.sh`); the script's text is in `script.text`, not in `rendered`. PowerShell units render with `$env:NAME='…';` assignments and PowerShell quoting (`'…'` with `''`).
- **LV-13.** Markers: `<dynamic:NAME>` for a run-time value, `<input:NAME>` for a missing input, `<secret:env:NAME>` or `<secret:var:NAME>` for a secret. A marker is never quoted, so a rule matcher can't mistake it for a literal.
- **LV-14.** Inputs with values render as their values, quoted for the unit's shell. A value that can't be rendered as one word (it contains a newline, or a character the shell can't quote) is replaced by `<input:NAME>`, and the unit can't be auto-allowed by rules mode.

## 5. Effects

An **effect** is a plan-dependent read or write that isn't a process. Every effect names its node and gives the path (absolute, or a dynamic marker).

| `kind` | Access | From |
|---|---|---|
| `path_check` | read (metadata) | `path:` subjects |
| `glob` | read (directory listing) | `for: { glob }` roots; `matches` lists a snapshot of the current matches, marked `snapshot: true` |
| `cwd` | read | every `cwd` that is dynamic or outside the invocation cwd |
| `stdin_file` | read | `stdin: { file }` |
| `tempdir` | write | `tempdir` (the temp root and `prefix`) |
| `capture_file` | write | `capture_file` (in the run dir) |
| `mount` (*later*) | read or write | container `mounts` and the default cwd mount |
| `readiness` (*later*) | network (loopback) | `services` `port:` checks |

- **LV-15.** Effects derived from inputs or variables (`cwd: { var: DIR }`, `stdin: { file: { var: F } }`) are listed with their concrete path when known, otherwise with a dynamic marker.

## 6. Flags and dynamic constructs

### 6.1 Flags

- **LV-16.** `dynamic_exec` is set on a unit that runs a variable as code: a `{ var }` element of an `exec` array that is an interpreter's code argument (`sh -c`, `bash -c`, `pwsh -Command`, `python -c`, `node -e`, …), or a `script` whose text matches one of the triggers below, or dynamic stdin into a known interpreter. In v0.x, `script` text is analysed with **token-level heuristics** for POSIX shells and PowerShell (formerly blokky-specs SH-30/SH-31), which also drive the script-hygiene lint rules. The triggers are:
  - a variable expansion in command position;
  - `eval`, `source`, `.` (as a command), or `sh`/`bash -c` with an expansion;
  - `iex` or `Invoke-Expression`;
  - an expansion piped into a known interpreter (`sh`, `bash`, `python`, `node`, `pwsh`, …).

  The heuristics may miss constructs; a missed case is a lint false negative, not a security failure, because `script` units are never auto-allowed by rules mode and validators see the full text. A hand-written POSIX lexer is planned later.
- **LV-17.** `exec_env_risk` is set on a unit whose `env_overrides` set an execution-influencing variable ([variables.md §7.3](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/variables.md#73-execution-influencing-variables)).
- **LV-18.** Concrete input values never clear a flag: caller-supplied data run as code is still dynamic code.

### 6.2 Constructs that force `ask`

The hook answers `ask` for a unit with any of these, unless a validator explicitly allows that unit (§7.4): `dynamic_exec`; `exec_env_risk`; a dynamic nested plan; dynamic stdin into an interpreter; a dynamic `cwd`; a missing required input. Dynamic argv elements and ordinary dynamic env values don't force `ask` by themselves (blokky-cli ADR 0003, Q-g).

### 6.3 Nested plans

- **LV-19.** A unit is a **nested invocation** if argv[0] resolves to the Blokky binary, or if a `script`'s text invokes `blokky`.
- **LV-20.** A nested `exec` invocation of `blokky run` with a static plan (a literal file path whose bytes are read now, with `--expect-sha256` if given) and literal `--input` values (or parent inputs known now) is expanded: `nested_plan` holds the child's own `leaves` document. Inputs are never inherited implicitly; a child with a missing required input is a static error in the parent's `leaves`.
- **LV-21.** Every other nested invocation is marked `nested_plan: { "dynamic": true }` and forces `ask`.

## 7. The validator protocol

([ADR 0004](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0004-validator-and-inputs.md))

### 7.1 Transport

- **LV-22.** The validator is an executable configured only in the user-level config (`validator.path`). For each request Blokky starts it with no arguments, in the invocation cwd, with Blokky's environment and no terminal; writes one request document to its stdin and closes it; and reads one response document from its stdout (at most 1 MiB). A bounded tail of its stderr is kept for diagnostics.
- **LV-23.** Its exit code is ignored. The process tree is killed when the timeout (`validator.timeout`, default 10s) fires.
- **LV-24.** `hook check` and `blokky validate` call the validator; `blokky run` never does (blokky-cli ADR 0003, Q-b).

### 7.2 Request

```json
{
  "schema_version": "0.1",
  "command": "hook check",
  "runner": "claude-code",
  "cwd": "/repo",
  "flags": { "max_duration": "1m50s", "expect_sha256": null, "cleanup": null, "verbose": false },
  "leaves": { … the leaves document … }
}
```

| Field | Meaning |
|---|---|
| `command` | `hook check` or `validate`. |
| `runner` | The runner name for `hook check` (`claude-code` in v0.1); absent for `validate`. |
| `cwd` | The invocation cwd. |
| `flags` | The allow-listed flags of the `blokky run` command line. |
| `leaves` | The complete `leaves` document (§2). |

### 7.3 Response

```json
{
  "schema_version": "0.1",
  "decision": "allow",
  "reason": "all units match the team policy",
  "units": [ { "id": "/do/1", "decision": "allow", "reason": "known CI poll" } ]
}
```

- **LV-25.** `decision` is `allow`, `deny` or `ask`; `reason` is REQUIRED for `deny` and `ask`. `units` is optional: per-unit verdicts `{ id, decision, reason }`.
- **LV-26.** Unknown fields are ignored. An unknown `decision`, a unit id that doesn't exist, or a missing required field makes the response invalid.

### 7.4 Combination

- **LV-27.** The verdict is:
  - `deny` if `decision` is `deny` or any unit verdict is `deny`;
  - otherwise `allow` if `decision` is `allow` **and** every unit that forces `ask` (§6.2) has an explicit unit verdict `allow`;
  - otherwise `ask`.

### 7.5 Failures

- **LV-28.** A timeout, a crash, a missing or non-executable path, malformed or oversized JSON, or an invalid response yields **`ask`**, with the failure as the reason (`BK_VALIDATOR_FAILED`). A validator failure never becomes `allow`, and never locks the agent out.
- **LV-29.** `blokky validate` exits 0 for `allow`, 5 for `ask` and 6 for `deny`, and prints the request's verdict and reasons as JSON.
