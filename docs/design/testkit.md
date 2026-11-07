# Testkit

Status: **draft** (T-0067, 2026-11-07); built in T-0068. Normative interface: blokky-specs [`conformance/testkit-interface.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/conformance/testkit-interface.md) (`TK-n`). Runner contract: blokky-specs [`conformance/README.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/conformance/README.md) §8 (`CF-n`). Test-first order: [ADR 0007](../adr/0007-runtime-plan.md). Decisions after the spikes: [ADR 0008](../adr/0008-spike-findings.md).

`blokky-testkit` is the fake program conformance cases run instead of real tools (CF-4). This doc covers how blokky-cli implements it and how the conformance runner sets it up.

## 1. Build

- One project, `test/Blokky.Testkit`, published with Native AOT as a single executable `blokky-testkit` (`blokky-testkit.exe` on Windows), one per RID. It needs no runtime, config or network (TK-1).
- It doesn't reference `Blokky.Core`: the testkit must not share bugs with the implementation it tests. Duration and size parsing (TK-2) are reimplemented from the spec grammar.
- Same rules as `blokky`: AOT and trim warnings are errors; OS calls through `LibraryImport` ([tech-stack.md](tech-stack.md#6-platform-calls)).
- It implements every command for the platforms Blokky targets in that release: Linux for 0.1, Windows for 0.6, macOS with the macOS port. Commands that a platform doesn't support exit with 123 (TK §3).

## 2. Implementation notes

| Area | Approach |
|---|---|
| Arguments | A hand-written parser over the fixed command table: options anywhere after the command, value in the next argument, `--` ends options (TK-1). `argv` takes no options (TK-12). Usage errors print one `blokky-testkit:` line and exit 125. |
| Output | Raw stdout/stderr streams, flushed after every write, so a merged pipe sees the write order (TK-4). |
| Invocation log | When `TESTKIT_LOG` is set: build the JSON line first, take an exclusive lock on the log file (`flock` / `LockFileEx`), append it in one write, release (TK-5). Failure exits with 124 before acting. |
| Aliases | The invoked name is the basename of `argv[0]` without `.exe`; the directory is that of the resolved executable (`Environment.ProcessPath`). Under any name but `blokky-testkit`, read `<name>.testkit-args` from that directory and insert its lines before the arguments (TK-6). |
| Signals | `PosixSignalRegistration` for SIGTERM, SIGINT and SIGHUP (and CTRL_BREAK/CTRL_C on Windows), with cancellation to ignore them (`sleep --ignore-term`) and a recorder for `--record-signals` (TK-14). `die` resets the disposition and raises the signal with `kill(getpid(), sig)` (TK-21). |
| `spawn` | Starts its descendants as new testkit processes with the null device or inherited streams (TK-15). `--escape setsid` and `double-fork` are done by the child itself after start (`setsid()`, then start the grandchild and exit), so the testkit never calls `fork()` in a multi-threaded runtime. `breakaway` uses `CREATE_BREAKAWAY_FROM_JOB` (0.6). |
| `alive` | `kill(pid, 0)` on Unix, `OpenProcess` plus exit-code check on Windows (TK-16). |
| `info` | `open("/dev/tty")` for `tty`; `fstat` on fd 0 for `stdin` (TK-17). |
| `count`, `flaky` | Read, increment and write the state file under an exclusive lock (TK-19). |
| `listen` | Binds `127.0.0.1` and, where available, `::1`; accepts and closes connections (TK-20). |

`spawn --escape` needs an intermediate process (the child that calls `setsid()` and starts the grandchild). The testkit logs **every process it starts**, the intermediates included, each with its own TK-5 line ([ADR 0008](../adr/0008-spike-findings.md)); cases that count invocations are checked against that when the testkit is built.

## 3. Unit tests

The testkit gets its own tests (TUnit, [tech-stack.md](tech-stack.md#7-tests)), run against the published binary: every command and option of the interface, exit codes 123–125, alias expansion, concurrent log appends (no interleaved lines), and `TESTKIT_TIME_SCALE`. A wrong testkit would make conformance cases pass or fail for the wrong reason.

## 4. Per-case environment (CF-56 to CF-58)

The conformance runner ([validation-strategy.md](validation-strategy.md)) prepares each case:

1. **Work dir:** a fresh, empty directory, filled from the case's `files` (CF-20). It is the run's cwd.
2. **Case root outside the work dir:** `bin/`, `home/`, `tmp/` and the invocation log live next to the work dir, not in it, so `files` expectations only see what the plan wrote.
3. **`bin/`:** `blokky-testkit` and one alias per `programs` entry, each with its `.testkit-args` file (CF-21). The runner **hard-links** (or copies) the testkit into each case's `bin/`; it doesn't symlink, because TK-6 resolves a symlink's directory to the shared testkit dir, where parallel cases' alias files would collide.
4. **Base environment**, built from nothing; nothing is inherited from the runner's own environment (CF-57):
   - `PATH`: the case's `bin/` first, then the directories of the shells the OS needs (`sh`, `bash`, `pwsh` as found when the runner starts);
   - `HOME` (the case's `home/`, so `blokky` finds no user config and keeps its state dir under it), `TMPDIR` (the case's `tmp/`); on Windows also `SystemRoot`, `PATHEXT`, `COMSPEC`, `USERPROFILE`, `TEMP`, `TMP`;
   - `TESTKIT_LOG` (the case's log file) and `TESTKIT_TIME_SCALE` (1, or 1/*k* in scaled mode);
   - the passthrough list per requirement, configured in the runner: for `pwsh`, `DOTNET_ROOT` when pwsh is a .NET tool; for `container-engine` (0.7), `DOCKER_HOST` and `XDG_RUNTIME_DIR`;
   - the case's `env`, where `null` removes a variable (CF-19).
5. **No implementation configuration:** default bounds, no config files, no shell paths (CF-57). The empty `HOME` ensures it.

## 5. Clock modes (CF-39, CF-40)

| Mode | Testkit | `blokky` |
|---|---|---|
| `real` | `TESTKIT_TIME_SCALE=1` | the system clock |
| `scaled` | `TESTKIT_TIME_SCALE=1/k` | timers fire after 1/*k*; durations reported in plan time |
| `virtual` | unchanged (cases opt in only when no process waits) | jumps to the next deadline while no process runs |

- The runner uses the fastest mode both the case and the adapter support. *k* is a runner setting (proposed default 10 on Linux, 1 on Windows) and stays at most 20; cases with `script`, `spawn`, `actions` or `after_run` default to `real` (CF-40).
- The clock mode can be selected only in test builds of `blokky`; release binaries always use the system clock ([ADR 0008](../adr/0008-spike-findings.md), [tech-stack.md](tech-stack.md#4-time)). So the release gate, which runs the binaries about to be released ([ci-cd.md](ci-cd.md#3-release)), uses `real`; the faster modes run against test builds. The adapter declares only the modes the binary implements (CF-54); `virtual` may come after 0.1.

## 6. Shipping

- **Never in release archives.** The release workflow builds and packages only `blokky`, and checks the archive contents against an explicit list ([ci-cd.md](ci-cd.md)).
- CI publishes the testkit per RID as a workflow artifact for the conformance jobs only.
- Other implementations of the language can build their own testkit from the interface; blokky-cli's is not published as a product.
