# Tech stack

Status: **draft** (T-0067, 2026-11-07). Decisions: [ADR 0001](../adr/0001-baseline.md) (.NET 11, Native AOT, YAML tree walking, `TimeProvider`, `LibraryImport`), [ADR 0007](../adr/0007-runtime-plan.md). Architecture: [architecture.md](architecture.md).

## 1. Runtime and SDK

- **.NET 11**, target framework `net11.0`. .NET 11 is a release candidate today; the SDK is pinned in `global.json` to the current RC and moved to the GA feature band (`11.0.100`) when it ships, with `rollForward: latestFeature`.
- `global.json` also sets `"test": { "runner": "Microsoft.Testing.Platform" }` (§7).
- CI installs the SDK from `global.json` (`actions/setup-dotnet` with `global-json-file`), so local and CI builds use the same band.

## 2. Native AOT

Native AOT is a hard requirement (brief §2).

- `blokky` and `blokky-testkit` set `PublishAot`; libraries set `IsAotCompatible`, which turns on the trim, AOT and single-file analyzers.
- **AOT and trim warnings are build errors** (`TreatWarningsAsErrors`, no suppressions without a comment and a tracker ID).
- `JsonSerializerIsReflectionEnabledByDefault=false`, so a missed source-generated type fails in tests, not in production.
- `InvariantGlobalization=true` (proposed): the language compares ordinally and defines its own case mapping (CO-12). To confirm with spike 9's `(?i)` results.
- Integration, conformance and smoke tests run against the **published** AOT binary, never the JIT build ([validation-strategy.md](validation-strategy.md)).
- Linux binaries link glibc dynamically; the oldest supported glibc follows from the build image (open, §9).

## 3. Libraries

| Need | Choice | Notes |
|---|---|---|
| YAML | **YamlDotNet**, event level (`Parser`, `IParser`) | No deserializer, no reflection. Events give marks (line, column) for every node. Duplicate keys, aliases, anchors, tags and complex keys are rejected by Blokky's AST builder, not by YamlDotNet. Pending spike 3, which may require a different front end. |
| CLI parsing | **System.CommandLine** 2.x | Response files off. The brief's parser rules (one spelling per option, no `--opt=v` or `--opt:v`, no bundling) must hold; whether System.CommandLine can enforce all of them is to be checked before 0.1 (open, §9). `hook check` reuses the same parser. |
| JSON | **System.Text.Json** source generation | One `JsonSerializerContext` per contract: report, pre-run envelope, events, `leaves`, validator protocol, config. Config is read with duplicate properties disallowed. Streams use `Utf8JsonWriter` (events, flushed per event). |
| Time | **`TimeProvider`** | Every timer and timestamp goes through it (§4). |
| Regex | `System.Text.RegularExpressions`, `RegexOptions.NonBacktracking` | Only after the portable-subset validator (SY-61, SY-62). Pending spike 9. |
| Run ids | ULID, implemented in Core | A few dozen lines; no package. |

Dependencies stay few: each new package needs an Apache-2.0-compatible license, AOT compatibility without warnings, and a reason a small in-repo implementation won't do.

## 4. Time

- Core takes a `TimeProvider` everywhere it waits or measures: `timeout`, `attempt_timeout`, `sleep`, `every`, `backoff`, grace, drain, reap, `finally_timeout`, the run budget and the cleanup deadline.
- **Unit tests** use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`).
- **Conformance** clock modes (CF-39): `real` is `TimeProvider.System`; `scaled` is a provider whose timers fire after 1/*k* of their duration and which reports durations in plan time (CF-40); `virtual` jumps to the next deadline while no process runs. How the runner selects a mode in the release binary is open (§9).
- Wall-clock timestamps (`t` in events) come from the same provider, so tests can freeze them.

## 5. Output

- stdout is written as raw UTF-8 bytes through `Console.OpenStandardOutput()`, not `Console.Out`, so no encoding layer or BOM interferes (OC-1).
- A closed stdout is detected on write and doesn't change the exit code (OC-6).

## 6. Platform calls

Where the BCL can't deliver the semantics, Blokky calls the OS directly (brief §1.4).

- **`LibraryImport` only** (source-generated marshalling). No `DllImport`, no runtime marshalling (`DisableRuntimeMarshalling` on the assembly), blittable structs, `SafeHandle` for owned handles.
- Errors through `SetLastError = true` and `Marshal.GetLastPInvokeError()`, mapped to Blokky errors with the errno name.
- One `Interop` class per OS and library (`libc` on Linux and macOS, `kernel32` on Windows), each member marked `[SupportedOSPlatform]`; callers guard with `OperatingSystem.IsLinux()` and the like. The platform-compatibility analyzer (CA1416) is an error.
- No native helper library of Blokky's own, unless spike 1 shows one is needed.
- Expected uses: `posix_spawn` and file actions, `setsid`, `kill`/`killpg`, `prctl(PR_SET_PDEATHSIG)`, `flock`, `openat`/`unlinkat`/`fstatat`; Windows Job Objects, `CreateProcessW` with `STARTUPINFOEX`, `GenerateConsoleCtrlEvent`, `LockFileEx`. Final list in [process-control.md](process-control.md).

## 7. Tests

- **Framework: TUnit** (proposed): source-generated tests on Microsoft.Testing.Platform, which also run as a Native AOT test app, so the platform tests can exercise P/Invoke code under AOT. xUnit v3 is the alternative. To confirm with the maintainer (§9).
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

- **System.CommandLine vs the parser rules:** can it reject `--opt=v`, `--opt:v` and other alternative spellings, and parse `hook check`'s command lines identically? If not, a small hand-written parser for the fixed command set.
- **Clock mode selection in the release binary** (CF-39): an environment variable, a hidden option, or a test-only build flavour. Clock modes aren't a security boundary (resource bounds are ergonomics), but a hidden knob in shipped binaries needs a decision.
- **Oldest supported glibc** for the Linux binaries (build image choice), and whether a `linux-musl` build is wanted.
- **Test framework:** TUnit or xUnit v3.
