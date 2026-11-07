# Run control

Status: **draft**. Collected on 2026-11-05 from blokky-specs, where these rules were part of the execution model, containers and shells docs ([ADR 0006](../adr/0006-phase-order-and-incremental-cli.md)). Former spec rule IDs are given in brackets; the language keeps their observable effect (blokky-specs [execution-model.md](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/execution-model.md) §7, EM-55 to EM-57).

This document is normative for how `blokky` controls a run: what it creates, how it reports, how it bounds and stops runs, and the process-control mechanisms behind the language's guarantees. Normative keywords are used as defined in [blokky-specs syntax.md §1](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/syntax.md#1-conventions). Rules carry IDs (`RC-n`).

## 1. Run lifecycle

`blokky run` goes through these phases:

1. **Pre-run.** Read the plan, run the character lint, parse, validate (schema, static rules, lint at severity `error`), load config, and bind inputs.
2. **Start.** Create the run id, the run dir and the registry entry, and take the run's advisory lock.
3. **Execute** the root node.
4. **Finish.** Write `report.json` (atomically), print the report on stdout, release the lock, and exit.

- **RC-1** [EM-1]. Any error in the pre-run phase ends the run with the pre-run envelope on stdout and the matching exit code ([cli.md](../cli.md#exit-codes)). No run id, run dir, process, probe or validator call exists at that point, and there is nothing to clean.
- **RC-2** [EM-3]. Once the start phase begins, a report is always written, including after failures, timeouts, budget exhaustion and shutdown (but not after a SIGKILL of `blokky` itself).

## 2. Process control

- **RC-3** [EM-15]. Every leaf runs in its own process group and session (Unix) or its own Job Object (Windows), so the leaf and everything it starts can be killed as a tree. On Linux the leaf is started with `posix_spawn` and `POSIX_SPAWN_SETSID`, so it leads its own session and group from its first instruction, and with `POSIX_SPAWN_SETSIGDEF` (every signal) and an empty `POSIX_SPAWN_SETSIGMASK`, so it doesn't inherit the dispositions and mask of `blokky`'s runtime (which ignores `SIGPIPE`) ([ADR 0008](../adr/0008-spike-findings.md)).
- **RC-4** [EM-18]. stdout and stderr are read concurrently into bounded tail buffers (last 50 lines, at most 4 KiB per stream) and into the leaf's log files (capped by `max_log_bytes` and the per-run cap). Pipes are always drained, even after a log cap is reached.
- **RC-5** [EM-20]. After the leaf's direct child exits, its pipes are read for at most `drain_timeout` (default 2s, a free config key). If the drain bound ends before EOF on either stream (stdout or stderr), the report flags `descendants_held_output`. The tree is then stopped (RC-7, EM-68), and the pipes are closed only after that: output written until the tree is stopped is captured.
- **RC-6** [EM-22]. The lifeline: all descendants die no later than run end, including when `blokky` itself is killed with SIGKILL.
  - **Linux guarantee.** Each run has a watchdog process (`blokky __watchdog`) in its own session, which holds the read end of a lifeline pipe whose write end only `blokky` holds; `blokky` registers each leaf's process group with it. When `blokky` is killed with SIGKILL, the watchdog SIGKILLs, within milliseconds, every registered group and every process carrying the run's marker variable (RC-22), which covers nested runs. While `blokky` lives, it is a child subreaper (RC-22), so escapees are stopped at run end too.
  - **Gaps (Linux).** A process that both left its leaf's group and cleared its environment is stopped only while `blokky` lives (through the subreaper). Nothing is stopped if the watchdog is killed together with `blokky` (for example a kill of all the user's processes, or the OOM killer).
  - **Watchdog loss.** If the watchdog dies while the run is going on, `blokky` stops the run: every running node is cancelled with `BK_CANCELLED`, `detail.reason` `lifeline_lost`, `finally` runs, and the report is written. The watchdog isn't restarted.
  - `PR_SET_PDEATHSIG` is not used ([ADR 0008](../adr/0008-spike-findings.md)). macOS (spike M) and Windows (spikes 2 and 5W) are pending.
- **RC-7.** Graceful stop is SIGTERM on Unix and CTRL_BREAK on Windows (when the leaf has a console); the forced kill is SIGKILL on Unix and `TerminateJobObject` on Windows.
- **RC-22** [EM-22]. **Escapees.** A process that left its leaf's process group (`setsid`, a daemon-style double fork) is an escapee. On Linux, `blokky` finds escapees two ways:
  - every leaf's environment gets a per-run marker variable `BLOKKY_RUN_<run id>=1`, found in `/proc/<pid>/environ`; one variable per run, so a nested run's leaves keep the outer run's marker;
  - `blokky` sets `PR_SET_CHILD_SUBREAPER` at start, so orphaned descendants are reparented to it, and every live descendant of `blokky` that isn't a current leaf or the watchdog is an escapee, even one that cleared its environment.

  Escapees found for a leaf are stopped after its group (graceful stop, grace period, forced kill, RC-7) and listed by pid in the leaf's result. Names starting with `BLOKKY_RUN_` are reserved for the run id (`BLOKKY_RUN_ID`, VA-44) and these markers; plans can't set them (VA-44).

## 3. Run budget, cleanup deadline and shutdown

These implement the language's **external stops** (EM-55 to EM-57).

### 3.1 Run budget

- **RC-8** [EM-34]. The run budget is the smallest of the explicit sources (config, `BLOKKY_MAX_DURATION`, `--max-duration`, and the remaining budget inherited by a nested run); with no explicit source it is the fallback 1m50s ([ADR 0002](../adr/0002-cli-run-lifecycle.md)).
- **RC-9** [EM-35]. At **budget − `cleanup_reserve`** (default 30s), `blokky` enters **budget mode**: it cancels every running node, runs `finally` lists, writes the report and exits with 8. Leaves killed this way carry `killed_by: budget`; the run's report status is `budget_exceeded`.
- **RC-10** [EM-54]. **Short budgets run without a reserve.** If the run budget is at or below `cleanup_reserve`, the effective reserve is zero: budget mode starts at the budget minus the report slice (RC-12), so cleanup gets no time (grace periods and `finally` are cut to zero and processes are force-killed), and the run ends at its budget. `blokky` warns on stderr at run start (blokky-specs [ADR 0010](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0010-variables-details.md)).
- **RC-11.** Every leaf gets `BLOKKY_MAX_DURATION`, the remaining run budget as a duration rounded down to whole milliseconds, so a nested `blokky run` inherits it.

### 3.2 Single cleanup deadline

- **RC-12** [EM-37, EM-38]. Entering budget, cancel-all or shutdown mode sets one **cleanup deadline**: the end of the budget (budget mode) or now + `cleanup_reserve` (shutdown). From then on, every sub-timer (grace period, `finally_timeout`, `drain_timeout`, reap timeout) is clamped to `min(own, deadline − report slice)`, where the **report slice** is a fixed 2s kept for writing the report.

### 3.3 Shutdown mode

- **RC-13** [EM-39]. SIGINT, SIGTERM and SIGHUP (Unix) and console control events (Windows) put `blokky` into **shutdown mode**: cancel all nodes, run `finally`, write a partial report, exit with 128 + the signal number (130 for SIGINT and for Ctrl-C/Ctrl-Break on Windows; 129 for the console close, logoff and shutdown events). Leaves killed this way carry `killed_by: shutdown`; the report status is `interrupted`.
- **RC-14** [EM-40]. A **second** interrupt skips the remaining `finally` work and force-kills everything; the report is still written.
- **RC-15** [EM-41]. Leaves run in their own sessions, so a Ctrl-C in `blokky`'s terminal reaches only `blokky`, which then stops the leaves itself.
- **RC-16** [EM-42]. If stdout is closed (`blokky run … | head`), the report is written to the run dir only and the exit code is unchanged.

The report's run status maps the language's run status ([blokky-specs results.md](https://github.com/gergodrazsdik/blokky-specs/blob/main/spec/results.md)): `succeeded` and `failed` as they are; `stopped` becomes `budget_exceeded` or `interrupted` by cause.

## 4. Privilege and containers (later)

- **RC-17** [CP-28]. A `become` leaf is started as `<tool> -n -u USER <absolute blokky> __exec`; the trampoline reads argv, the environment and the script text from an inherited pipe. *Provisional — verify in spike*, including tools that close extra file descriptors.
- **RC-18** [CP-27]. A user-only config key chooses the elevation tool; when unset, `blokky` uses `sudo` if it resolves on `PATH`, else `doas`.
- **RC-19** [CP-31]. When `blokky` runs with euid 0, the user-level config is root's own: root's passwd home is used and `XDG_CONFIG_HOME`/`HOME` are ignored. The file MUST be root-owned and not group- or world-writable, else it is a config error. The invoking user's config is not read; repo configs still apply.
- **RC-20** [CP-33]. Strict mode flags raw `sudo`, `doas`, `su`, `pkexec` and `runas` in leaves (`raw-privilege-escalation`); a root run without `defaults.become` gets the `root-run` lint warning [CP-32].
- **RC-21** [CP-11]. How the trampoline binary becomes available inside a container is *provisional — verify in spike*.
