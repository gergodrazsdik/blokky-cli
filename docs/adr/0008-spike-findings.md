# 0008: Decisions from the MVP spikes

- Status: accepted
- Date: 2026-11-07
- Questions: the MVP spike findings (tracker T-0030; decision record [spikes-mvp.md](../review/spikes-mvp.md))
- Builds on: [ADR 0004](0004-platform-invocation-conformance.md) (launcher criteria), [ADR 0007](0007-runtime-plan.md) (runtime plan)

## Context and problem statement

The MVP spikes (1, 3, 4, 5, 6, 7, 8L, 9, 12 and 14 of the [spike plan](../design/spike-plan.md)) ran on WSL on 2026-11-07. Their reports are in [design/notes](../design/notes/). They answer the spike questions and raise choices for the CLI: the YAML front end, the pwsh floor, the lifeline, the test framework, the Linux baseline, the release gate, where the schema is published, and a set of design defaults the design docs had left open.

The maintainer decided the main choices by questionnaire in two rounds, then T-1 and the batch of the decision record, all on 2026-11-07. Language changes from the same record are in blokky-[specs ADR 0027](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0027-spike-findings.md); this ADR records the CLI side.

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| YAML front end | **YamlDotNet's scanner and parser behind an adapter**; a hand-written parser for the subset |
| pwsh minimum | 7.2; **7.4, CI tests 7.4 and the current release**; 7.6 |
| Lifeline | **the per-run watchdog; watchdog loss stops the run**; watchdog plus `PR_SET_PDEATHSIG`; restart a lost watchdog |
| Test framework | **TUnit**; xUnit v3 |
| Linux baseline | **glibc 2.36 or later, no musl build yet**; an older glibc; glibc and musl |
| Release gate | **100% of the in-scope cases on each RID**; known failures allowed with a tracker ID |
| Schema publication | **a versioned raw GitHub URL plus `blokky schema`**; SchemaStore registration |
| Colour codes in leaf output (T-1) | **(a) `NO_COLOR=1` for every leaf, `TERM=dumb` when unset**; (b) `TERM=dumb` for pwsh leaves only; (c) nothing |
| Design defaults | **as proposed in the decision record's batch** |

## Decision outcome

### YAML front end (spike 3)

- **YamlDotNet's `Scanner` and `Parser` only**, no `Deserializer`, no representation model, nothing reflection-based. It passed every `syntax/text` and `syntax/yaml` case with exact positions under Native AOT.
- **One adapter** wraps YamlDotNet; nothing else in Core sees its types. The adapter:
  - computes every position from `Mark.Index` with Blokky's own line map (LF, CRLF, lone CR; columns in code points), because `Mark.Line`/`Mark.Column` count UTF-16 units and treat U+2028/U+2029 as line breaks;
  - relocates known error positions (a quoted scalar followed by text is reported at its opening quote);
  - guards against the five defects found in YamlDotNet 18.1.0 (below);
  - is pinned to one YamlDotNet version; upgrades rerun the suite and the mutation fuzzer.
- **Defects and their guards:**
  1. `Scanner(skipComments: true)` yields `Error` tokens forever on input such as `log: a: b`: token and event loops are bounded and stop at the first `Error` token.
  2. Some malformed input throws exceptions other than `YamlException`, without marks (`InvalidOperationException`, `ArgumentException`): every exception except out-of-memory maps to `BK_PLAN_YAML`.
  3. An empty key in a nested block mapping gives a key event with no value event, and a wrong mark: the tree builder doesn't assume key/value pairs.
  4. A plain scalar containing U+2028 fails to parse (YAML 1.1 line-break rules): the character lint rejects U+2028 and U+2029 first (SY-12, [specs ADR 0027](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0027-spike-findings.md)).
  5. A leading U+FEFF becomes part of the first key: the encoding pass strips the UTF-8 BOM before YamlDotNet sees the text.
- **Reported upstream** to YamlDotNet (tracker F-0025). The guards stay until a fixed version is pinned and the suite and fuzzer pass without them.
- A hand-written subset parser stays the fallback, decided after the full syntax suite and fuzzing run on the real validator.

### pwsh (spikes 12 and 14)

- The supported minimum is **pwsh 7.4**. CI tests 7.4 and the current release, each from the official package on its own runtime. Blokky doesn't probe the version.
- The prologue scanner follows SH-17 as amended in [specs ADR 0027](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0027-spike-findings.md).

### Lifeline (spike 5)

- **The lifeline is the per-run watchdog** (`blokky __watchdog`, [process-control.md](../design/process-control.md#lifeline-when-blokky-is-killed-rc-6)). `PR_SET_PDEATHSIG` and .NET's `KillOnParentExit` are not used: they reach only the direct child, and PDEATHSIG belongs to the spawning thread.
- **Watchdog loss stops the run.** `blokky` watches the watchdog (pidfd). If it dies before `bye`, the run can no longer keep RC-6, so `blokky` stops it like an external stop: every running node is cancelled with `BK_CANCELLED`, `detail.reason` `lifeline_lost`, and the report is written. The watchdog is not restarted ([run-control.md](../contracts/run-control.md) RC-6).

### Tests

- **TUnit** on Microsoft.Testing.Platform, which also runs as a Native AOT test app, so platform tests exercise P/Invoke code under AOT ([tech-stack.md](../design/tech-stack.md#7-tests)).

### Linux baseline

- **glibc 2.36 or later** (Debian 12+, Ubuntu 24.04, Fedora 37+, RHEL 10). Linux binaries are built in a Debian 12 container, so they link against glibc 2.36 symbols. 2.36 is also the first glibc with the `pidfd_open` wrapper the launcher uses.
- **No musl** (`linux-musl-*`) build yet.

### Release gate

- A release ships only when **100% of its in-scope conformance cases pass on each RID** it ships: every case that isn't skipped as not supported, and every not-supported check. There is no known-failures allowance; a spec problem is fixed in the spec first ([validation-strategy.md](../design/validation-strategy.md#release-gate)).

### Schema publication

- Editors fetch the plan schema from a **versioned raw GitHub URL** of blokky-specs: `https://raw.githubusercontent.com/gergodrazsdik/blokky-specs/<spec tag>/schema/blokky.schema.json`.
- **`blokky schema`** prints the schema of the spec version the binary implements, for offline use.
- [install.md](../install.md#editor-support) gives the URL, the modeline and the recommended VS Code settings.

### Colour codes in leaf output (T-1, option a)

- Every leaf gets `NO_COLOR=1`, and `TERM=dumb` when its environment has no `TERM`. The plan's `env` can override either. This is a new entry in the variables Blokky sets (spec VA-44, [specs ADR 0027](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0027-spike-findings.md)).
- It keeps ANSI colour codes out of captures and logs (pwsh writes them to redirected stderr when `TERM` looks like a colour terminal). It is display hygiene, not a control.

### Design defaults

These are recorded in the design docs and can change later without a new ADR:

| Area | Default | Doc |
|---|---|---|
| Command-line parsing | System.CommandLine, with a hand-written parser for the fixed command set only if System.CommandLine can't reject `--opt=value` | [tech-stack.md](../design/tech-stack.md#3-libraries) |
| Clock modes | Selectable only in test builds; release binaries always use the system clock | [tech-stack.md](../design/tech-stack.md#4-time) |
| Fuzzing | SharpFuzz with libFuzzer | [validation-strategy.md](../design/validation-strategy.md#6-fuzzing) |
| Conformance pin | `conformance.lock` is bumped by hand, in a PR, when a spec draft is tagged | [ci-cd.md](../design/ci-cd.md) |
| SBOM | SPDX, generated by syft | [ci-cd.md](../design/ci-cd.md#3-release) |
| Tag check (T-0040) | `git verify-tag` against the maintainer's GPG key as published on GitHub | [ci-cd.md](../design/ci-cd.md#3-release) |
| Testkit log | The testkit logs every process it starts, `spawn --escape` intermediates included | [testkit.md](../design/testkit.md) |

### Run-control contract

[run-control.md](../contracts/run-control.md) takes the Linux mechanisms from spikes 1, 5 and 8L:

- RC-3 names `posix_spawn` with `POSIX_SPAWN_SETSID`, `POSIX_SPAWN_SETSIGDEF` and `POSIX_SPAWN_SETSIGMASK`;
- RC-5 says when `descendants_held_output` is set, and that output written until the tree is stopped is captured;
- RC-6 states the Linux guarantee and its gaps, and that watchdog loss stops the run (`lifeline_lost`); macOS and Windows are pending;
- RC-22 (new): escapees, the subreaper, and the reserved `BLOKKY_RUN_*` marker variables.

### Consequences

- **Good:** the front end, the lifeline and the launcher are settled for 0.1, with spike evidence behind each, and the design docs have no open choices left that block T-0068 or T-0069.
- **Good:** a single release gate (100% per RID) keeps the published pass rate honest.
- **Neutral:** YamlDotNet stays a dependency with known defects behind guards; the adapter keeps a later switch to a hand-written parser local.
- **Neutral:** glibc 2.36 excludes Debian 11, Ubuntu 22.04 and RHEL 9 until a musl or older-glibc build is added.
- **Bad:** if the watchdog dies (for example a kill of all the user's processes), the run stops even though its leaves might have finished. Stopping is the only behaviour that keeps RC-6 honest.

## More information

- Decision record: [review/spikes-mvp.md](../review/spikes-mvp.md). Spike reports: [design/notes](../design/notes/).
- Language changes: blokky-[specs ADR 0027](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0027-spike-findings.md).
- Open after this ADR: the report status, exit code and `killed_by` value of a run stopped by `lifeline_lost`, and the report field for escapee pids (tracker F-0026).
- Tracker: T-0030; F-0025 (YamlDotNet defects to report upstream).
