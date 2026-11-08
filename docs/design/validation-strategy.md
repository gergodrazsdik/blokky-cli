# Validation strategy

Status: **draft** (T-0067, 2026-11-07). Source: brief §6, [ADR 0004](../adr/0004-platform-invocation-conformance.md) (conformance checkout), [ADR 0007](../adr/0007-runtime-plan.md) (test-first, conformance-gated releases), [ADR 0008](../adr/0008-spike-findings.md) (release gate, fuzzing, pwsh versions). Suite format: blokky-specs [`conformance/README.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/conformance/README.md) (`CF-n`).

How blokky-cli shows that a build implements the language and its own contracts. The conformance suite measures the language; the other layers cover what the suite can't see (messages, the JSON contracts, CLI behaviour, robustness, AOT).

## 1. Layers

| Layer | Tests | Runs against | When |
|---|---|---|---|
| Unit | YAML front end, AST builder, static rules, feature gate, lint, input binding, config loader, invocation builder, report truncation | JIT build | every PR |
| Golden | error messages and fixes; report, envelope, events, `leaves`, validator request | JIT build | every PR |
| Executor | modifier order, cancellation, cleanup deadline, budget and shutdown modes | fake launcher, `FakeTimeProvider` | every PR |
| Platform | launcher, lifeline, drain, safe deletion, advisory lock with real processes | AOT test app, testkit | every PR, per OS |
| CLI | commands, flags, exit codes, channels, run dir, `clean`/`gc`, signals | published AOT `blokky` | every PR, per OS |
| Conformance | the blokky-specs suite at the pinned tag | published AOT `blokky` and testkit | every PR, per OS; gates releases |
| Fuzzing | YAML front end and plan reader | JIT build with instrumentation | short on PRs, long on a schedule |
| AOT smoke | publish and run | published binary per RID | every PR, release |

The CI wiring is in [ci-cd.md](ci-cd.md); the testkit and per-case setup in [testkit.md](testkit.md).

## 2. Conformance suite

- **Checkout:** the suite comes from blokky-specs at the tag and SHA in `conformance.lock` (ADR 0004). Locally, `just conformance` uses `../blokky-specs` when present and warns if it differs from the pin.
- **Runner:** `test/Blokky.Conformance`, a .NET console app (JIT; it isn't shipped). It implements CF-53 to CF-63: overrides, requirements, feature skips with the not-supported check, clock modes, per-case environment, matching, `actions` (`stop` by SIGTERM to `blokky`, `kill` by SIGKILL) and `after_run`. Process-level needs (`kill`, `after_run`, per-OS timing) build on spikes 1, 5 and 8L (T-0068). `just conformance [specs] [rid] [options]` publishes `blokky`, builds the runner and runs the suite (`--help` lists the options: `--filter`, `--timing-factor`, `--keep`, …). The not-supported check runs `blokky run` on the plan without the case's inputs (inputs aren't features, and a malformed `--input` is a usage error reported before the plan is read); for a validation case, reporting the case's own expected plan errors instead (exit 3, phase `plan`) also passes it. "No process started" is checked with the testkit's log and with the case's leftover processes ([testkit.md](testkit.md#4-per-case-environment-cf-56-to-cf-58)), so cases that would run and not-supported checks alike need `blokky-testkit`: without it they are skipped with `requirement: testkit`. Cases with `stop` actions are skipped with `requirement: external stop` when the adapter can't stop a run (CF-55).
- **Adapter:** runs `blokky run` with the plan on stdin and each input as `--input`, reads the pre-run envelope or `report.json`, and maps it to the result document (CF-2). It only maps: a field it would have to infer is a report gap, fixed in the contract ([architecture.md](architecture.md#5-errors-and-results)).
- **Declared support** comes from Core's feature table (CF-12, [architecture.md](architecture.md#3-feature-support-gate)): the cases a release doesn't support are skipped, and their not-supported check (`BK_PLAN_NOT_SUPPORTED`, no process started) must pass.
- **Output:** JSON lines per case (id, outcome, reason, cited rules, first mismatch) plus totals and the **pass rate** (CF-63), and a JUnit file for the CI UI. The pass rate counts not-supported skips as not passed, so it measures language coverage.

### Later-feature validation pass

The validator checks the whole language, but CF-12 skips every case whose features aren't supported, so validation cases for later features would go unmeasured. The runner adds a non-normative **later-feature validation pass** ([ADR 0009](../adr/0009-validator-delivery.md)):

- It judges every validation case whose features aren't all validated, on the not-supported check's own `blokky validate` invocation. Such a case passes when the expected errors are reported, matched as in a normal run; `BK_PLAN_NOT_SUPPORTED` errors are allowed in addition (and don't count for `exhaustive`). The invocation passes no inputs: inputs are checked only after the plan step, which the unsupported features end, so cases that expect input errors are *not applicable* in this pass.
- It also judges every case whose plan is valid and that the not-supported check covers: run and no-result cases (on their `blokky run` invocation), and validation cases that expect only input errors. Their plan may get no plan error but `BK_PLAN_NOT_SUPPORTED`; any other fails the case in this pass. CF-12 asks only that `BK_PLAN_NOT_SUPPORTED` is among the errors, so without this a wrong error would hide behind the skip. A run case passes this pass when its plan validates clean; an input-error case stays *not applicable*.
- The report shows it as a second number next to the CF-63 pass rate (`later-feature validation: P of J pass, N pending a rule family, F fail`), and each case's JSON line carries `later` and `later_reason`. A `fail` there fails the run, and CI.
- **Pending rule families.** While the static rule families were being written (T-0082 to T-0085), many cases expected errors nothing reported yet. They were marked *pending* through an explicit, reviewed list in the runner, `PendingRuleFamilies`; since T-0085 it is empty, so every judged validation case must pass, and the mechanism stays for rules added later. Its entries are keyed by rule ID (`RF-3`), error code (`BK_FROM_ENV_UNSET`) or case id, each naming the task that implements it. A case is pending when it fails only because expected errors of listed entries are missing: every expected error of an unlisted rule must still be reported, and an exhaustive case may report no error it doesn't expect (reported errors are matched one to one against distinct expected ones, as in a normal run). Anything else is a fail.
- **The pending list and the validated table.** A feature enters the validated table only once none of the validation cases that list it still depends on a pending entry; otherwise an invalid plan using it would validate clean. A unit test checks this against the suite (and, with an injected entry, that the check works while the list is empty). The pass's own unit tests use an injected list too, so deleting live entries never breaks them.
- **Why not an allow-list of cases.** An allow-list hides a case whole, whatever goes wrong in it, and nothing makes it shrink. The pending list names what is missing, not which cases may fail: a regression in an implemented rule fails even inside a pending case, and the list only shrinks: an entry is reported as stale and fails the run (full runs only) when, in every case it applies to, the case's own errors of that entry are reported, so each rule-family PR deletes its entries as it lands. A unit test fails on an entry that applies to no case at all.

### Release gate

A release is published only when **100% of its in-scope cases pass on each RID** it ships (ADR 0007, [ADR 0008](../adr/0008-spike-findings.md)):

- every case that isn't skipped passes, on every RID the release ships;
- every not-supported check passes;
- no case ends as `error`.

The release notes and the ROADMAP feature matrix state the spec version, the supported features, and the pass rate per OS. Bumping `conformance.lock` to a newer spec draft is done by hand, in a PR like any other, and must keep the gate green for the features already released.

There is no known-failures allowance: a case that fails because of a spec problem is fixed in the spec first ([ADR 0008](../adr/0008-spike-findings.md)).

## 3. Unit and golden tests

- **Parser and validator:** one test per error path, asserting code, `detail`, line and column. Conformance cases already cover the language's plan errors; unit tests add the internal edges (the encoding pass, nesting limits, error collection across stages, the feature gate's ordering).
- **Golden messages:** messages aren't a contract, but they are what an agent reads first. Golden files fix the message text and the `fix` snippet for every plan, input and CLI error, so changes are deliberate and reviewed in the diff.
- **Golden JSON:** the report, the pre-run envelope, events, `leaves` and the validator request are rendered for a fixed set of plans with a frozen clock and fixed run ids, then compared byte for byte and validated against the schemas in `schema/` ([output-contract.md](../contracts/output-contract.md), [leaves-and-validation.md](../contracts/leaves-and-validation.md)). Truncation (OC-22, OC-23) gets golden cases for each step.
- **Updating goldens:** one recipe rewrites them; CI never does.

## 4. Executor tests

The executor runs against a fake `IProcessLauncher` (scripted exits, output, hangs, signals) and `FakeTimeProvider`. These tests cover what is slow or flaky with real processes: modifier-layer order (EM-4), the cleanup deadline and clamping (RC-12), budget mode and short budgets (RC-9, RC-10), the second interrupt (RC-14), and status propagation. They don't replace conformance runs; they make failures quick to locate.

## 5. CLI tests

Against the published AOT binary, using the testkit:

- exit codes per command and the pre-run precedence ([cli.md](../cli.md#precedence-before-a-run-starts));
- stdout carries exactly one JSON document; stderr rules with and without a terminal; a closed stdout (OC-1 to OC-6);
- signals to `blokky`: SIGINT, SIGTERM, SIGHUP give 130, 143, 129 and an `interrupted` report (RC-13);
- run dir layout and modes, `report --last`, `clean`, `gc`, `BK_RUN_IN_PROGRESS`;
- the argv parser's rejections (`--opt=v`, response files, unknown flags);
- shells: `script` with real `bash`, `sh` and `pwsh` (installed versions pinned in CI; pwsh 7.4, the supported minimum, and the current release, [ADR 0008](../adr/0008-spike-findings.md)).

## 6. Fuzzing

Target: the plan reader up to the validated AST, since it takes untrusted bytes. Properties:

- no unhandled exception, hang or stack overflow on any input;
- every rejection is a `BK_PLAN_*` error with a position inside the input;
- the same input gives the same errors.

Seeds: every case plan and example from blokky-specs. Short runs on PRs, longer runs on a schedule, crashes become unit tests. The engine is **SharpFuzz** with libFuzzer ([ADR 0008](../adr/0008-spike-findings.md)). Spike 3's mutation fuzzer (45,880 mutants, no hang or crash after the adapter's guards) shows the properties to keep, and YamlDotNet upgrades rerun it.

## 7. AOT smoke tests

For each RID: publish with warnings as errors, then run `blokky version`, `blokky schema` and `blokky validate` on every blokky-specs example (features the release doesn't support must give `BK_PLAN_NOT_SUPPORTED`). Binary size is recorded per build, not gated.

## 8. Later

- Windows in 0.6 and macOS with its port add their RIDs to every per-OS layer.
- An agent-usage evaluation (can agents write correct plans from the docs and errors) is planned, not designed.
