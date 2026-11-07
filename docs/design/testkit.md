# Testkit

Status: **draft** (T-0067, 2026-11-07); built in T-0068. Normative interface: blokky-specs [`conformance/testkit-interface.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/conformance/testkit-interface.md) (`TK-n`). Runner contract: blokky-specs [`conformance/README.md`](https://github.com/gergodrazsdik/blokky-specs/blob/main/conformance/README.md) §8 (`CF-n`). Test-first order: [ADR 0007](../adr/0007-runtime-plan.md). Decisions after the spikes: [ADR 0008](../adr/0008-spike-findings.md).

`blokky-testkit` is the fake program conformance cases run instead of real tools (CF-4). This doc covers how blokky-cli implements it and how the conformance runner sets it up.

## 1. Build

- One project, `test/Blokky.Testkit`, published with Native AOT as a single executable `blokky-testkit` (`blokky-testkit.exe` on Windows), one per RID. It needs no runtime, config or network (TK-1).
- `just publish` publishes it next to `blokky`: **`artifacts/publish/Blokky.Testkit/release_<rid>/blokky-testkit`** (for example `release_linux-x64`). The conformance runner and `just test-aot` (`BLOKKY_TESTKIT`) take it from there. The JIT build in `artifacts/bin/Blokky.Testkit/debug/` needs its `.dll` beside it, so only the published binary can be copied or hard-linked into a case's `bin/`.
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
| Signals | `PosixSignalRegistration` for SIGTERM, SIGINT and SIGHUP (and CTRL_BREAK/CTRL_C on Windows), with cancellation to ignore them (`sleep --ignore-term`) and a recorder for `--record-signals` (TK-14). A request that isn't cancelled ends the process by that signal, as the runtime's default does. `sleep` registers its handlers right after alias expansion, before the log line (whose lock can make it wait) and before `--pid-file` is written, so a request sent once either exists is handled. `--ignore-term-for` counts from the entry into managed code, not from the registration. The remaining window: a request that arrives during the runtime's start-up, before the handlers exist (a few milliseconds for the AOT binary), still ends the process; a sender that waits for the log line or the pid file first is past it. `die` sets `RLIMIT_CORE` to 0 (no core file in the work dir), resets the disposition and raises the signal with `kill(getpid(), sig)` (TK-21). |
| `spawn` | Starts its descendants with `posix_spawn` (default signal dispositions, empty mask), with the null device or inherited streams (TK-15); the testkit never calls `fork()` in a multi-threaded runtime. `--escape setsid` uses `POSIX_SPAWN_SETSID`. `--escape double-fork` starts an intermediate `blokky-testkit spawn --escape setsid … -- …` that starts the real child in a new session and exits at once, so the child is reparented. `--wait` reports a child killed by signal N as 128 + N. `breakaway` uses `CREATE_BREAKAWAY_FROM_JOB` (0.6); until then `spawn` exits with 123 on Windows. |
| `alive` | Linux: `/proc/<pid>/stat`, where a zombie counts as not alive (it has exited and only waits to be reaped); macOS: `kill(pid, 0)`; Windows: `OpenProcess` plus exit-code check (TK-16). |
| `info` | `open("/dev/tty")` for `tty`. For `stdin` on Linux: `isatty(0)` for `terminal`, else the target of the `/proc/self/fd/0` link (`/dev/null` or none: `null-device`; `pipe:` or `socket:`: `pipe`; anything else: `file`); macOS uses `isatty` and .NET's redirection check (TK-17). |
| `count`, `flaky` | Read, increment and write the state file under an exclusive lock (TK-19). |
| Exact bytes | On Linux, arguments come from `/proc/self/cmdline` and `spawn` passes `/proc/self/environ` on, so text that isn't valid UTF-8 reaches stdout, files and descendants unchanged (TK-3). JSON output (`argv`, `env`, the log) carries the decoded text. |
| Shared files | The log, `--pids`, `--record-signals` and state files are written under `flock`, with .NET's own advisory lock on open turned off (`System.IO.DisableFileLocking`), so concurrent testkits wait instead of failing. |
| `listen` | Binds `127.0.0.1` and, where available, `::1`; accepts and closes connections (TK-20). |

`spawn --escape double-fork` needs an intermediate process (the child that starts the grandchild in a new session and exits). The testkit logs **every process it starts**, the intermediates included, each with its own TK-5 line ([ADR 0008](../adr/0008-spike-findings.md)); cases that count invocations are checked against that when the testkit is built.

## 3. Unit tests

The testkit gets its own tests (`test/Blokky.Testkit.Tests`, TUnit, [tech-stack.md](tech-stack.md#7-tests)), named after the TK rule they cover. `just test` runs them against the JIT build; `just test-aot` against the published binary: every command and option of the interface, exit codes 123–125, alias expansion, concurrent log appends (no interleaved lines), and `TESTKIT_TIME_SCALE`. A wrong testkit would make conformance cases pass or fail for the wrong reason.

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
6. **Leftover processes:** `TESTKIT_LOG` names the case's own log, so the entry `TESTKIT_LOG=<path>` marks every process of the case (no extra variable, so CF-57 holds). On Linux `blokky` runs in its own session and process group, started through util-linux `setsid`, which execs in place (.NET's `CreateNewProcessGroup` is Windows-only). After it exits, the runner scans `/proc/*/environ` for marked processes: if none is left (three quick scans), the outcome is read at once; otherwise after a settle time (300 ms × the timing factor), so a late process that logs within it is counted. A marked process still alive then is a started process for the no-process checks (CF-23, the CF-12 not-supported check). After the checks, `after_run` included, the runner SIGKILLs every marked process until none is left, before it deletes the case's directories and releases its ports (CF-14). A process that cleared its environment isn't found, but it can't write to the case's log either.
7. **Shared files:** the runner reads the log under a blocking shared `flock`, with .NET's lock on open turned off, as the testkit does; it never sees a partly written line.

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
- CI builds the testkit per RID inside the conformance job (`just conformance` runs `just publish`); it is never uploaded as a release asset.
- Other implementations of the language can build their own testkit from the interface; blokky-cli's is not published as a product.
