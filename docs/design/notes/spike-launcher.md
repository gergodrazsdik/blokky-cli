# Spike report: launcher, lifeline, drain (spikes 1, 5, 8L)

Status: done on WSL 2026-11-07; CI (`ubuntu-24.04`) not run yet. Branch `spikes/launcher` (unmerged), code in `spikes/launcher/` (`Program.cs`, `test.sh`).

Environment: Fedora 44 on WSL2, kernel 6.18.54.1-microsoft-standard-WSL2, glibc 2.43, .NET SDK 11.0.100-rc.3.26522.148, Native AOT `linux-x64` (2.3 MB binary, no trim/AOT warnings). All P/Invoke is `LibraryImport` into libc.

The spike binary `launcher run [flags] script...` runs each script as a leaf (`/bin/sh -c`), one after the other, in the EM-68 order, and prints one line per leaf. Flags: `--launch spawn|tramp|tramp-late`, `--lifeline none|pdeathsig|watchdog`, `--subreaper`, `--timeout`, `--grace`, `--drain` (ms), `--thread main|ephemeral`. `launcher probe-net` exercises the .NET 11 `Process` APIs. Rerun: `dotnet publish -c Release -r linux-x64 -p:PublishAot=true -o out && ./test.sh all 50`.

## Spike 1: launch path and tree kill

### Answer

- **`posix_spawn` with `POSIX_SPAWN_SETSID` is the launch path.** The child is a session and group leader from its first instruction (`pgid == sid == pid`), so there is no window in which the group doesn't exist. It needs glibc ≥ 2.26 (Ubuntu 24.04 has 2.39); `posix_spawnattr_t`/`posix_spawn_file_actions_t` are opaque buffers, so allocate them natively and use only the libc accessors.
- Also set `POSIX_SPAWN_SETSIGDEF` (all signals) and `POSIX_SPAWN_SETSIGMASK` (empty): the .NET runtime ignores `SIGPIPE`, and an ignored disposition survives `execve`, so without the reset every leaf would ignore `SIGPIPE`.
- **A trampoline that calls `setsid` itself (`tramp-late`) is racy**: until it runs, the leaf is in `blokky`'s group and `kill(-pid)` finds nothing. If a trampoline is used at all, it is for `PR_SET_PDEATHSIG`, launched with `POSIX_SPAWN_SETSID`.
- **.NET 11 RC `Process` can do the launch but isn't enough on its own.** New in 11 (reflection over the RC runtime): `ProcessStartInfo.StartDetached` (verified: `setsid`, `pgid == sid == child`), `KillOnParentExit` (CA1416: Linux, Windows, Android; not macOS), `StartSuspended`, `StandardOutputHandle`/`StandardErrorHandle`/`StandardInputHandle`, `InheritedHandles`, `SafeProcessHandle.Start/Signal/WaitForExit*`, `Process.Run*`/`ReadAll*`. Missing: signalling a group, observing exit without reaping (the zombie leader keeps the pgid from being reused), and coexisting with `waitpid(-1)` for orphans a subreaper adopts (the runtime reaps its own children from its SIGCHLD handler). `libSystem.Native` implements it with `vfork` + `setsid` + `prctl`.
- **Stopping the tree:** `kill(-pgid, SIGTERM)`, poll `/proc` until no live (non-zombie) process has that pgid or the grace period ends, `kill(-pgid, SIGKILL)`, poll again until empty. Exit of the direct child is observed with `pidfd_open` + `poll` (Linux ≥ 5.3, glibc wrapper ≥ 2.36), and the leader is reaped only after the group is empty.
- **Escapees** (left the group: `setsid`, daemon-style double fork + `setsid`) are found two ways, then stopped with SIGTERM, grace, SIGKILL and reported by pid:
  - an environment marker `BLOKKY_RUN_<runid>=1` added to every leaf's environment and found in `/proc/<pid>/environ` (one variable per run, so nested runs keep the outer marker);
  - `PR_SET_CHILD_SUBREAPER` on `blokky`: orphaned descendants are reparented to `blokky` instead of init, so every live descendant of `blokky` that isn't a current leaf is an escapee, even one that cleared its environment.
- **cgroup v2** stops everything, escapees included, but isn't available by default here (below).

### Evidence

Leaf (`test.sh tree`): a 3-level `sh` tree (level 1 ignores SIGTERM), `setsid sleep`, a plain double fork `( sleep & )`, a daemon `( setsid sleep & )`, and `( env -i setsid sleep & )`; timeout 500 ms, grace 300 ms; after each run every tagged `sleep` is checked with `pgrep`.

```text
sample: duration_ms=806 drain_ms=1000 stop_ms=51 held_output=true group_survivors_at_stop=0
        escapees=[1895947,1895951] escapees_alive_after=0 group_alive_after=0 killed_by=timeout
  survivors: 1895953 sleep 6014          # the env -i setsid daemon: no marker, reparented to init
tree [--launch spawn]: 50 runs, runs with survivors: 50 (always only the `env -i setsid` daemon)
tree [--launch spawn --subreaper]: 50 runs, runs with survivors: 0
```

`launcher run 'ps -o pid,ppid,pgid,sid -p $$'` → `pid 8 pgid 8 sid 8` (own session and group).

cgroup v2 (`systemd-run --user --scope -p Delegate=yes`, a leaf cgroup holding `sh` with a `setsid` and an `env -i setsid` daemon): `echo 1 > leaf1/cgroup.kill` → `populated 0`, no tagged process left. But the shells and agents on this WSL instance run in `0::/non-systemd`, a root-owned, non-delegated cgroup; `blokky` would have to re-exec itself under `systemd-run --user --scope` (needs the systemd user manager and D-Bus). On GitHub-hosted runners the job runs inside the runner service's cgroup, also not delegated (not verified).

### Recommended design

1. Launch with `posix_spawn(SETSID | SETSIGDEF | SETSIGMASK)` and `adddup2` for stdin (`/dev/null` or the `stdin` pipe), stdout, stderr; every other fd is `O_CLOEXEC`.
2. `pidfd_open` per leaf; wait with `poll`; reap with `waitpid(pid)` after the stop. `blokky` does its own reaping (no `System.Diagnostics.Process` for leaves), including `waitpid(-1, WNOHANG)` for adopted orphans.
3. `PR_SET_CHILD_SUBREAPER` at start, marker variable per run.
4. Tree stop as above; escapee stop after the group stop; report escapee pids.
5. cgroup v2 as a later enhancement when `blokky` already runs in a delegated cgroup (detect, don't require).

## Spike 5: lifeline when `blokky` gets SIGKILL

### Answer

- **`PR_SET_PDEATHSIG` alone doesn't meet EM-22**: it kills only the direct child. With ten nested hosts and a tree in the innermost leaf, the nested hosts die in a cascade, but the innermost leaf's grandchildren survive (3 left in every run).
- **PDEATHSIG belongs to the creating *thread*.** Spawned from a thread that exits 200 ms later, the leaf is killed at 201 ms (`thread=ephemeral`, empty stdout). If the thread is already gone when the child calls `prctl`, the signal attaches to another thread and fires only at process exit, so a trampoline must be spawned from one dedicated, long-lived spawner thread. `KillOnParentExit` in .NET 11 survived its spawning thread's exit (child alive 100 ms after it; how .NET avoids the hazard isn't documented and not verified).
- **A subreaper doesn't help when `blokky` dies**: orphans go to the next subreaper or init.
- **A watchdog process works**: `blokky __watchdog`, started once per run with `POSIX_SPAWN_SETSID` (so terminal and group signals miss it), reads a lifeline pipe on stdin whose write end only `blokky` holds (`O_CLOEXEC`). `blokky` writes `+<pgid>` after each spawn and `bye` at the end. On EOF without `bye` it SIGKILLs every registered group and every process carrying the run's marker. The marker also covers the window between spawn and registration, and nested runs (they inherit the outer marker).

### Evidence

`test.sh lifeline`: a chain of ten nested `launcher run` hosts, the innermost leaf a 2-level tree; once all `sleep`s are up, `kill -9` the top host, wait 1 s, count `sleep`s and hosts.

```text
lifeline [spawn,  none]:      50 runs, runs with survivors after 1s: 50 (12 left: the 9 nested hosts and the tree)
lifeline [tramp,  pdeathsig]: 50 runs, runs with survivors after 1s: 50 (3 left: the innermost tree)
lifeline [spawn,  watchdog]:  50 runs, runs with survivors after 1s: 0
lifeline [tramp,  watchdog]:  50 runs, runs with survivors after 1s: 0
thread=main:      duration_ms=507 stdout="survived\n"
thread=ephemeral: duration_ms=201 stdout=""          # PDEATHSIG fired on thread exit
net [StartDetached KillOnParentExit]: 0 0 → child, grandchild survive; 1 0 → both survive (own sid);
    0 1 / 1 1 / 1 1 ephemeral → child dies, grandchild survives
```

### Per-OS guarantee for RC-6 (Linux)

When `blokky` is killed with SIGKILL, its watchdog SIGKILLs, within milliseconds, every leaf process group it registered and every process that still carries the run's marker variable, which covers nested runs. Not covered: a process that both left its leaf's group and cleared its environment (caught only while `blokky` lives, through the subreaper), and anything when the watchdog is killed too (for example a kill of all the user's processes or the OOM killer).

## Spike 8L: drain and completion order

### Answer

Yes. `descendants_held_output` is detected as "the stream readers didn't reach EOF before the drain bound". The order drain → stop the tree → bind captures → next sibling holds, and a held pipe costs at most drain + grace (+ the stop polling).

Implementation notes: readers `poll` with a short timeout and a cancel flag (closing an fd doesn't wake a `read` blocked on it on Linux); pipe read ends are closed only after the tree stop, so output written during the grace period is still captured; "the tree is stopped" means no live process with the pgid and no live escapee, not just "signals sent".

### Evidence

```text
late output within drain (drain 1000):
  duration_ms=4 drain_ms=501 held_output=false stdout="early\nlate\n"
held pipe, holder ignores SIGTERM (drain 1000, grace 500):
  duration_ms=3 drain_ms=1000 stop_ms=508 held_output=true group_survivors_at_stop=2 stdout="x\n"
  wall ms: 1520, holder alive: 0
order (leaf A's descendant traps SIGTERM, sleeps 0.3 s, writes a_end; leaf B writes b_start):
  leaf=0 drain_ms=1000 stop_ms=315 held_output=true group_survivors_at_stop=2
  overlaps (a_end >= b_start): 0 of 50
```

## Changes proposed

Spec (blokky-specs PR):

- **EM-68**: drop *Provisional*. Step 3 reads "stopped" as "no process of the leaf's grouping is alive" (not "signals sent"); escapees found at this point are stopped in the same step.
- **EM-22**: drop *Provisional*; the three strengths match what Linux can do (MUST group, SHOULD on own kill, best effort for escapees).
- **EM-20**: no change (detection works as written).

Run-control contract (blokky-cli PR):

- **RC-3**: name the Unix mechanism: `posix_spawn` with `POSIX_SPAWN_SETSID`; signal dispositions and mask reset in the child.
- **RC-6**: replace *provisional* with the Linux mechanism and the guarantee above (watchdog, marker, subreaper); macOS (spike M) and Windows (spikes 2, 5W) pending.
- **New RC** (escapees): `blokky` is a child subreaper and marks every leaf's environment with a per-run variable; escapees found by either are stopped after the group and listed in the leaf's result. Document the marker variable as reserved (`BLOKKY_RUN_*`).
- **RC-5**: add that `descendants_held_output` is set when the drain bound ends before EOF on either stream, and that output written until the tree is stopped is captured.

## Open points

- CI (`ubuntu-24.04`) runs of `test.sh all 50`: not done; needs a workflow on this branch.
- The report field for escapee pids (name and shape) and whether escapees get `killed_by`.
- What `blokky` does if its watchdog dies (pidfd on the watchdog; fail the run or restart it).
- PDEATHSIG on top of the watchdog adds little (no difference in 50 runs) and costs an extra exec per leaf plus the thread rule: recommend leaving it out unless CI shows otherwise.
- `/proc` scans are O(processes) per stop; fine for tests, to measure with many parallel leaves.
- cgroup v2 path when delegated: detection and how it composes with containers (spike 11/13).
