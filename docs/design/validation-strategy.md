# Validation strategy

Status: **draft** (T-0067, 2026-11-07). Source: brief §6, [ADR 0004](../adr/0004-platform-invocation-conformance.md) (conformance checkout), [ADR 0007](../adr/0007-runtime-plan.md) (test-first, conformance-gated releases). Suite format: blokky-specs [`conformance/README.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/conformance/README.md) (`CF-n`).

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
- **Runner:** `test/Blokky.Conformance`, a .NET console app (JIT; it isn't shipped). It implements CF-53 to CF-63: overrides, requirements, feature skips with the not-supported check, clock modes, per-case environment, matching, `actions` (`stop` by SIGTERM to `blokky`, `kill` by SIGKILL) and `after_run`. Process-level needs (`kill`, `after_run`, per-OS timing) build on spikes 1, 5 and 8L (T-0068).
- **Adapter:** runs `blokky run` with the plan on stdin and each input as `--input`, reads the pre-run envelope or `report.json`, and maps it to the result document (CF-2). It only maps: a field it would have to infer is a report gap, fixed in the contract ([architecture.md](architecture.md#5-errors-and-results)).
- **Declared support** comes from Core's feature table (CF-12, [architecture.md](architecture.md#3-feature-support-gate)): the cases a release doesn't support are skipped, and their not-supported check (`BK_PLAN_NOT_SUPPORTED`, no process started) must pass.
- **Output:** JSON lines per case (id, outcome, reason, cited rules, first mismatch) plus totals and the **pass rate** (CF-63), and a JUnit file for the CI UI. The pass rate counts not-supported skips as not passed, so it measures language coverage.

### Release gate

A release is published once the conformance subset for its features passes (ADR 0007):

- every case that isn't skipped passes, on every RID the release ships;
- every not-supported check passes;
- no case ends as `error`.

The release notes and the ROADMAP feature matrix state the spec version, the supported features, and the pass rate per OS. Bumping `conformance.lock` to a newer spec draft is a PR like any other, and must keep the gate green for the features already released.

Open: whether a release may ship with a known failing case recorded against a spec issue (with a tracker ID), or a spec fix always comes first.

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
- shells: `script` with real `bash`, `sh` and `pwsh` (installed versions pinned in CI; minimum pwsh from spike 14).

## 6. Fuzzing

Target: the plan reader up to the validated AST, since it takes untrusted bytes. Properties:

- no unhandled exception, hang or stack overflow on any input;
- every rejection is a `BK_PLAN_*` error with a position inside the input;
- the same input gives the same errors.

Seeds: every case plan and example from blokky-specs. Short runs on PRs, longer runs on a schedule, crashes become unit tests. The engine (SharpFuzz with libFuzzer, or property-based generation) is open.

## 7. AOT smoke tests

For each RID: publish with warnings as errors, then run `blokky version`, `blokky schema` and `blokky validate` on every blokky-specs example (features the release doesn't support must give `BK_PLAN_NOT_SUPPORTED`). Binary size is recorded per build, not gated.

## 8. Later

- Windows in 0.6 and macOS with its port add their RIDs to every per-OS layer.
- An agent-usage evaluation (can agents write correct plans from the docs and errors) is planned, not designed.
