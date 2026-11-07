# Tech stack

Status: **draft** (T-0067, 2026-11-07). Decisions: [ADR 0001](../adr/0001-baseline.md) (.NET 11, Native AOT, YAML tree walking, `TimeProvider`, `LibraryImport`), [ADR 0007](../adr/0007-runtime-plan.md), [ADR 0008](../adr/0008-spike-findings.md) (MVP spike findings). Architecture: [architecture.md](architecture.md).

## 1. Runtime and SDK

- **.NET 11**, target framework `net11.0`. .NET 11 is a release candidate today; the SDK is pinned in `global.json` to the current RC and moved to the GA feature band (`11.0.100`) when it ships, with `rollForward: latestFeature`.
- `global.json` also sets `"test": { "runner": "Microsoft.Testing.Platform" }` (§7).
- CI installs the SDK from `global.json` (`actions/setup-dotnet` with `global-json-file`), so local and CI builds use the same band.

## 2. Native AOT

Native AOT is a hard requirement (brief §2).

- `blokky` and `blokky-testkit` set `PublishAot`; libraries set `IsAotCompatible`, which turns on the trim, AOT and single-file analyzers.
- **AOT and trim warnings are build errors** (`TreatWarningsAsErrors`, no suppressions without a comment and a tracker ID).
- `JsonSerializerIsReflectionEnabledByDefault=false`, so a missed source-generated type fails in tests, not in production.
- `InvariantGlobalization=true` (proposed): the language compares ordinally and defines its own case mapping (CO-12). Spike 9 ran with it: .NET's casing APIs differ from CO-12 in either globalization mode, so CO-12 comes from a generated table and nothing depends on ICU.
- Integration, conformance and smoke tests run against the **published** AOT binary, never the JIT build ([validation-strategy.md](validation-strategy.md)).
- Linux binaries link glibc dynamically. The supported floor is **glibc 2.36** (Debian 12+, Ubuntu 24.04, Fedora 37+, RHEL 10): release binaries are built in a **Debian 12 container**, so they reference no newer glibc symbols, and 2.36 has the `pidfd_open` wrapper the launcher uses. No `linux-musl` build yet ([ADR 0008](../adr/0008-spike-findings.md)).

## 3. Libraries

| Need | Choice | Notes |
|---|---|---|
| YAML | **YamlDotNet**, `Scanner` and `Parser` only, behind one adapter | No deserializer, no reflection; AOT-clean (spike 3). Duplicate keys, aliases, anchors, tags and complex keys are rejected by Blokky's AST builder, not by YamlDotNet. Positions come from `Mark.Index` and Blokky's own line map. The adapter bounds token and event loops, maps every exception to `BK_PLAN_YAML` and tolerates odd event sequences (YamlDotNet 18.1.0 defects, F-0025). Version pinned; upgrades rerun the suite and the fuzzer ([ADR 0008](../adr/0008-spike-findings.md)). |
| CLI parsing | **System.CommandLine** 2.x | Response files off. The brief's parser rules (one spelling per option, no `--opt=v` or `--opt:v`, no bundling) must hold. Default ([ADR 0008](../adr/0008-spike-findings.md)): System.CommandLine, replaced by a small hand-written parser for the fixed command set only if it can't reject `--opt=value`. `hook check` reuses the same parser. |
| JSON | **System.Text.Json** source generation | One `JsonSerializerContext` per contract: report, pre-run envelope, events, `leaves`, validator protocol, config. Config is read with duplicate properties disallowed. Streams use `Utf8JsonWriter` (events, flushed per event). |
| Time | **`TimeProvider`** | Every timer and timestamp goes through it (§4). |
| Regex | `System.Text.RegularExpressions`, `RegexOptions.NonBacktracking` | Never with the pattern passed through: Blokky's own subset parser validates the pattern (SY-61, SY-62) and emits a .NET pattern over a byte-mapped value, with `(?i)` expanded from the CO-12 table (spike 9). The automata limit is raised at startup to match the spec's size bound. |
| Run ids | ULID, implemented in Core | A few dozen lines; no package. |

Dependencies stay few: each new package needs an Apache-2.0-compatible license, AOT compatibility without warnings, and a reason a small in-repo implementation won't do.

## 4. Time

- Core takes a `TimeProvider` everywhere it waits or measures: `timeout`, `attempt_timeout`, `sleep`, `every`, `backoff`, grace, drain, reap, `finally_timeout`, the run budget and the cleanup deadline.
- **Unit tests** use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`).
- **Conformance** clock modes (CF-39): `real` is `TimeProvider.System`; `scaled` is a provider whose timers fire after 1/*k* of their duration and which reports durations in plan time (CF-40); `virtual` jumps to the next deadline while no process runs. The clock mode can be selected **only in test builds**; release binaries always use the system clock ([ADR 0008](../adr/0008-spike-findings.md)).
- Wall-clock timestamps (`t` in events) come from the same provider, so tests can freeze them.

## 5. Output

- stdout is written as raw UTF-8 bytes through `Console.OpenStandardOutput()`, not `Console.Out`, so no encoding layer or BOM interferes (OC-1).
- A closed stdout is detected on write and doesn't change the exit code (OC-6).

## 6. Platform calls

Where the BCL can't deliver the semantics, Blokky calls the OS directly (brief §1.4).

- **`LibraryImport` only** (source-generated marshalling). No `DllImport`, no runtime marshalling (`DisableRuntimeMarshalling` on the assembly), blittable structs, `SafeHandle` for owned handles.
- Errors through `SetLastError = true` and `Marshal.GetLastPInvokeError()`, mapped to Blokky errors with the errno name.
- One `Interop` class per OS and library (`libc` on Linux and macOS, `kernel32` on Windows), each member marked `[SupportedOSPlatform]`; callers guard with `OperatingSystem.IsLinux()` and the like. The platform-compatibility analyzer (CA1416) is an error.
- No native helper library of Blokky's own: the spikes did everything through `LibraryImport` into libc.
- Uses on Linux: `posix_spawn` with its attributes and file actions, `pidfd_open`, `poll`, `waitpid`, `kill`, `prctl(PR_SET_CHILD_SUBREAPER)`, `flock`, `openat`/`mkdirat`/`unlinkat`/`fstatat`/`fdopendir`. Expected on Windows: Job Objects, `CreateProcessW` with `STARTUPINFOEX`, `GenerateConsoleCtrlEvent`, `LockFileEx`. Details in [process-control.md](process-control.md).

## 7. Tests

- **Framework: TUnit** ([ADR 0008](../adr/0008-spike-findings.md)): source-generated tests on Microsoft.Testing.Platform, which also run as a Native AOT test app, so the platform tests can exercise P/Invoke code under AOT.
- `FakeTimeProvider` for time; a fake `IProcessLauncher` for the executor.
- Golden files are plain files in the test project, updated by a recipe and reviewed in the diff ([validation-strategy.md](validation-strategy.md)).

## 8. Repository layout

```text
blokky-cli/
  global.json                 # SDK pin, test runner
  Directory.Build.props       # UseArtifactsOutput, Nullable, TreatWarningsAsErrors, analyzers, Deterministic
  Directory.Packages.props    # central package management, transitive pinning
  nuget.config                # nuget.org only, package source mapping
  blokky.slnx
  src/Blokky.Core/            # library, IsAotCompatible
  src/Blokky.Cli/             # the `blokky` executable, PublishAot
  test/Blokky.Testkit/        # the `blokky-testkit` executable, PublishAot
  test/Blokky.Core.Tests/     # unit, golden and executor tests
  test/Blokky.Platform.Tests/ # real-process launcher tests (AOT test app)
  test/Blokky.Cli.Tests/      # CLI tests against the published binary
  test/Blokky.Conformance/    # conformance runner and adapter
  conformance.lock            # blokky-specs tag and SHA (ADR 0004)
```

- `UseArtifactsOutput`: build output in `artifacts/bin/<project>/<config>/`, publish output in `artifacts/publish/<project>/<config>_<rid>/`; CI and recipes point there.
- Central package management: versions only in `Directory.Packages.props`. Lock files (`packages.lock.json`) are committed and CI restores in locked mode.
- **Analyzers:** .NET analyzers at `AnalysisLevel` `latest-recommended`, `EnforceCodeStyleInBuild` with the repo's `.editorconfig`, nullable reference types on, and the banned-API analyzer to keep invariants mechanical: `Process.Start` outside the launcher, `DateTime.Now`/`UtcNow` and `Task.Delay` without a `TimeProvider`, `Console.Out` for stdout, reflection-based JSON.
- Formatting: `dotnet format --verify-no-changes` in CI.

## 9. Open points

None. The former open points (command-line parsing, clock-mode selection, the glibc floor and musl, the test framework) were decided in [ADR 0008](../adr/0008-spike-findings.md) and are recorded above.
