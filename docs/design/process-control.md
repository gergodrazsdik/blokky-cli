# Process control

How blokky-cli starts leaves, stops process trees, keeps the lifeline when it is killed itself, drains output, and handles files the run owns. It implements the run-control contract ([run-control.md](../contracts/run-control.md)) and the spec rules it cites (EM-15, EM-20 to EM-22, EM-26, EM-68, EM-69, SH-25 to SH-29).

Status: Linux is designed from the MVP spikes ([spike-launcher](notes/spike-launcher.md), [spike-paths-delete](notes/spike-paths-delete.md)), decided in [ADR 0008](../adr/0008-spike-findings.md), and verified on WSL; CI (`ubuntu-24.04`) runs are still to do. Windows (0.6, spikes 2, 5W, 8W) and macOS (spike M, CI only) are pending. All OS calls go through `LibraryImport`; leaves never use `System.Diagnostics.Process`.

## The launcher abstraction

`IProcessLauncher` (see [architecture.md](architecture.md)) has one implementation per OS family behind the same contract:

| Operation | Contract |
|---|---|
| `Start(leaf)` | Start the resolved absolute program with the given argv, environment, cwd and stdio; the leaf is the leader of its own OS grouping from its first instruction. |
| `WaitExit(leaf)` | Observe the direct child's exit without reaping it, so the grouping's id isn't reused while it is being stopped. |
| `StopTree(leaf, grace)` | Graceful request to the whole grouping, the grace period, a forced kill, and wait until no live process is left in it. |
| `FindEscapees(run)` | Processes that left their leaf's grouping (best effort, EM-22). |
| `Lifeline(run)` | Stop the run's leaves if `blokky` itself dies. |

## Linux

### Launch

- **`posix_spawn` with `POSIX_SPAWN_SETSID`** (glibc ≥ 2.26): the leaf is session and group leader from its first instruction (`pgid == sid == pid`), so there is no window in which the group doesn't exist. A trampoline that calls `setsid` itself was racy in the spike and is not used.
- **`POSIX_SPAWN_SETSIGDEF` for all signals and `POSIX_SPAWN_SETSIGMASK` empty.** The .NET runtime ignores `SIGPIPE`, and an ignored disposition survives `execve`; without the reset every leaf would ignore `SIGPIPE`.
- **`posix_spawn_file_actions_adddup2`** for stdin (`/dev/null` or the `stdin` pipe), stdout and stderr. Every other descriptor `blokky` opens is `O_CLOEXEC`.
- **The spawn attributes** are opaque buffers allocated natively and used only through libc's accessors.
- **argv[0] is passed as written** (spike 6): `program` in the result is the absolute path found on `PATH` or against the cwd (SH-25, SH-26, SH-29), not a symlink's target, and the process starts under that path, so multi-call binaries and shebang scripts see their own name. Bare names never reach an API that searches the cwd (.NET `Process` does).
- **A file the OS can't execute** (no shebang, wrong format) fails with `ENOEXEC`; there is no fallback through `/bin/sh` (`BK_SPAWN_FAILED`, `bad_format`).

### Waiting and reaping

- **Exit** is observed with `pidfd_open` + `poll` (Linux ≥ 5.3; glibc wrapper ≥ 2.36).
- **Reaping:** the leader is reaped with `waitpid(pid)` only after its group is empty. `blokky` does all reaping itself, including `waitpid(-1, WNOHANG)` for orphans it adopts as subreaper.

### Stopping a tree

1. `kill(-pgid, SIGTERM)` (EM-26).
2. Poll `/proc` until no live, non-zombie process has the pgid, or the grace period (default 5s) ends.
3. `kill(-pgid, SIGKILL)` and poll until the group is empty. "Stopped" means no live process is left, not "signals sent" (EM-68).
4. Stop the escapees found for the leaf (below) the same way, and list them by pid in the leaf's result (RC-22).

### Escapees

A process that left its leaf's group (`setsid`, a daemon-style double fork) is found two ways:

- **The run marker:** every leaf's environment gets `BLOKKY_RUN_<runid>=1` (one variable per run, so nested runs keep the outer marker), found in `/proc/<pid>/environ`. Names starting with `BLOKKY_RUN_` are reserved (RC-22, VA-44).
- **The subreaper:** `blokky` sets `PR_SET_CHILD_SUBREAPER` at start, so orphaned descendants are reparented to it instead of init. Every live descendant of `blokky` that isn't a current leaf is an escapee, even one that cleared its environment.

With both, no process survived in 50 of 50 spike runs. Without the subreaper, a daemon that cleared its environment and called `setsid` survived every time.

cgroup v2 (`cgroup.kill`) stops everything, escapees included, but only in a delegated cgroup. It isn't available by default under WSL or (probably) on GitHub runners, so it is a later enhancement: detected, never required.

### Lifeline when `blokky` is killed (RC-6)

- **A per-run watchdog,** `blokky __watchdog`, started with `POSIX_SPAWN_SETSID` so terminal and group signals miss it. It reads a lifeline pipe on stdin whose write end only `blokky` holds (`O_CLOEXEC`).
- **Registration:** `blokky` writes `+<pgid>` after each spawn and `bye` at the end.
- **On EOF without `bye`,** the watchdog SIGKILLs every registered group and every process carrying the run's marker. With ten nested `blokky` runs, nothing was alive 1s after `kill -9` of the top one, in 50 of 50 runs.
- **Not used:** `PR_SET_PDEATHSIG` alone kills only the direct child, and it belongs to the spawning thread (it fired when a short-lived spawner thread exited). It added nothing on top of the watchdog in the spike. .NET 11's `KillOnParentExit` has the same reach.
- **Guarantee documented for RC-6:** groups and marked processes are killed within milliseconds. Not covered: a process that both left its group and cleared its environment (stopped only while `blokky` lives, through the subreaper), and anything if the watchdog is killed too (a kill of all the user's processes, the OOM killer).
- **Watchdog loss:** `blokky` watches the watchdog with a pidfd. If it dies before `bye`, `blokky` stops the run: every running node is cancelled with `BK_CANCELLED`, `detail.reason` `lifeline_lost`, `finally` runs and the report is written. The watchdog isn't restarted ([ADR 0008](../adr/0008-spike-findings.md), RC-6).

### Output and completion order (EM-20, EM-68)

- **Readers:** stdout and stderr are read on dedicated readers that `poll` with a short timeout and a cancel flag. Closing a descriptor doesn't wake a blocked `read` on Linux.
- **Completion order** after the direct child exits:
  1. drain for the drain bound (default 2s, at least 1s);
  2. if a stream hasn't reached EOF by then, `descendants_held_output` is set;
  3. stop the tree;
  4. close the read ends (output written until the tree is stopped is captured);
  5. bind captures;
  6. start the next sibling.
- **Measured:** a held pipe costs at most drain + grace plus the stop polling (1.5s with a 1s drain and 0.5s grace). The next sibling never overlapped a dying descendant in 50 runs.

### Files the run owns (EM-69)

- **One fd-relative native module** creates and removes everything the run owns: `mkdirat`, `openat(O_CREAT | O_EXCL | O_NOFOLLOW)`, and recursive removal with `openat(O_DIRECTORY | O_NOFOLLOW)`, `fstatat(AT_SYMLINK_NOFOLLOW)`, `unlinkat` and an `st_dev` check.
- **No path-based .NET file APIs** are used for run-owned files: under a concurrent swap-in attack, `Directory.Delete(recursive)` deleted outside files in 79 of 100 runs, while the fd-relative remover damaged nothing in 500.
- **A mount point** inside the tree is left in place, and the removal reports it as partial.

## Windows (0.6)

Pending spikes 2, 5W, 8W and C:

- a Job Object per leaf, with race-free assignment;
- a hidden console per leaf (EM-15 MUST) and CTRL_BREAK as the graceful stop;
- the lifeline through the job's kill-on-close;
- drain;
- UTF-8 output code pages;
- reparse-point-safe deletion by handle.

## macOS (later, CI only)

Pending spike M:

- `posix_spawn` setsid support;
- no `PDEATHSIG` or subreaper (kqueue `NOTE_EXIT` or a watchdog only);
- `/private/var` paths.

## Open points

- **CI:** runs of the spike tests on `ubuntu-24.04`, and whether GitHub runners have a delegated cgroup.
- **Escapees in the result:** the report field for escapee pids, and whether escapees get `killed_by` (F-0026).
- **A run stopped by `lifeline_lost`:** its report status, exit code and the leaves' `killed_by` value (F-0026).
- **Cost:** `/proc` scan cost with many parallel leaves (0.3 brings `all`/`any`/`race`).
- **cgroup v2:** how it composes with containers (spikes 11/13).
