# Agent council review: blokky-specs 1.0.0-draft.3

- Status: findings verified; waiting for the maintainer's decisions (T-0051)
- Date: 2026-11-06
- Reviewed: blokky-specs tag `v1.0.0-draft.3` (`spec/`, `schema/`, `examples/`, `conformance/`, ADRs 0001–0016), against brief §1
- Tracker: T-0050; the maintainer review is T-0051

## How the review ran

Five reviewer agents, one lens each (brief §8 step 7), read the same material independently. They could challenge an ADR only with an argument the ADR doesn't already answer. A separate verifier per lens then tried to disprove every finding against the text: it checked quotes, counts and platform facts, and corrected severities.

| Lens | Findings | Blocker | Major | Minor | Confirmed | Partly | Refuted |
|---|---|---|---|---|---|---|---|
| Principles and minimalism (`MIN-`) | 12 | 0 | 3 | 9 | 6 | 6 | 0 |
| Agent as user (`AGT-`) | 14 | 0 | 5 | 9 | 10 | 4 | 0 |
| Security posture (`SEC-`) | 10 | 1 | 3 | 6 | 5 | 5 | 0 |
| Implementability (`IMP-`) | 17 | 0 | 12 | 5 | 10 | 7 | 0 |
| Spec quality (`SQ-`) | 23 | 0 | 10 | 13 | 17 | 6 | 0 |
| **Total** | **76** | **1** | **33** | **42** | **48** | **28** | **0** |

Severities are the verifier's. "Partly" means the core holds but a claim, count or proposal was wrong; the notes below give the correction.

The raw findings, the verifications and the briefs the agents worked from are in [council-draft.3/](council-draft.3/). The `minimalism.yaml` file holds a keep/simplify/cut verdict for each of 79 features, and `agent-user.yaml` holds the plan-writing exercise.

**What held up well:**

- The `exec` argv model and the per-shell injection notes.
- "Not a sandbox" is stated where it matters, and hygiene is never presented as enforcement.
- 60 of 79 features got a "keep" verdict, among them the core blocks, `capture`, `retry`, `until`, `for`, `collect`, `lock`, `tempdir`, `finally`, `race`, `set`, `vars`, and the `host` and `command` subjects.
- All 52 examples validate against the schema.

## How to read this report

The findings are consolidated into three groups:

1. **Feature decisions (D-n):** keep, shrink or cut a feature.
2. **Semantic decisions (S-n):** a rule needs a choice between real options.
3. **Corrections (C-n):** the answer is clear, and they can be accepted as a batch.

Each item lists the findings it merges, the options, and a recommendation. Accepted items become ADRs and `1.0.0-draft.4`.

## 1. Feature decisions

### D-1 `become` (blocker)

Findings: SEC-1 (blocker), IMP-5, MIN-1, SEC-2, SQ-17, IMP-17.

**The problem:** three lenses reached it independently. As specified, `become` is less safe than the same `sudo -n` in bash:

- CP-28 gives a `become` leaf the invoking user's full environment "whatever environment policy sudoers or doas.conf set", and resolves argv[0] on the invoking user's PATH.
- The transport is a trampoline, `sudo -n -u USER blokky __exec`. A sudoers rule must allow it, which is the same as allowing every command.
- Narrow sudoers rules that work in bash don't work with `become`, and sudo's log shows only the trampoline.

**What doesn't apply:** the verifier corrected the attacker framing. Anyone who can poison PATH can already call the allowed trampoline. What remains is real anyway: an honest plan can run the wrong program as root, root writes into the user's HOME, and sudo's command allowlist and logging are lost.

**Scope gaps:** it's undefined which user runs services, the engine CLI under `defaults.become` (CP-20) and Blokky's own host-side work. Locks run with and without sudo don't exclude each other (IMP-17).

**Options:**

- **(a) Cut `become` and `defaults.become` from 1.0** (ROADMAP candidate). Plans write `exec: [sudo, -n, …]`, which is exactly bash: sudo's rules, env policy, secure_path and log all apply. CP-21 to CP-35 and spike 11's privilege half go away. The cost is that the node's `env` is reset by sudo, as in bash, and a root script is a file passed to `sudo -n bash`. Both examples that use it (become-root, deploy-health-check) can be rewritten.
- **(b) Plain sudo transport for `exec` leaves.** Run `sudo -n -u USER -- <program> <args>` and keep the trampoline only for scripts. This reverses ADR 0005 Q17e. Env entries are refused or exposed on sudo's command line, and CP-29's file modes need another mechanism.
- **(c) Keep the trampoline, with a sudo-like environment** (env reset, a safe PATH for argv[0] lookup), and state honestly that sudoers must allow blokky and that sudo's log doesn't show the command. This is coherent and no example breaks, but the narrow-sudoers gap remains.

**Recommendation:** (a). It is the only option that is "the same as bash" by construction, and it removes the largest provisional area of the spec. Whatever is chosen, `sudo blokky run` (the root-drop mode, `invoker`) needs the scope rules of SEC-2/SQ-17 and the lock-owner rule of IMP-17.

### D-2 `container`

Findings: MIN-2, IMP-6, IMP-7, SEC-5 (part), SQ-18.

**The problem:** `container` is the largest modifier (CP-1 to CP-20), with the most provisional rules. By the spec's own terms it gives no security benefit. Implementing it needs an in-container helper for per-leaf process control (IMP-6), and Docker and Podman differ under the same text (IMP-7).

The verifier corrected some facts and costs:

- `podman exec --env-file` exists, so env values don't have to go on the command line.
- Podman machine shares `/Users` on macOS.
- Rewriting a container step as `docker run` leaves loses CP-8's in-container kill and CP-7's isolation of the engine CLI from the node's `env`.

**Options:**

- **(a) Cut it from 1.0.** Plans call the engine as a leaf.
- **(b) Shrink it.** Drop `env_pass` and `user` (no example uses them), keep the default cwd mount, and add the missing rules: env values never on a command line, engine-defined ownership and labelling, a container qualifier for EM-15/EM-26.
- **(c) Keep it as is.**

**Recommendation:** (b), and re-evaluate after the container spikes (brief spikes 11 and 13 plus IMP-7's engine matrix). If those show the helper is too costly, cut it in a later draft.

### D-3 `services`

Findings: MIN-3, AGT-13.

**Keep `services`:** it solves the brief's "background process plus a trap" problem. The proposal is to drop `depends_on` and `on_exit`, which ADR 0014 itself flagged for this review. Services would start in list order, each ready before the next, and stop in reverse order; a service that dies fails the node.

The verifier objected to also dropping the `output:` readiness subject: a port check isn't equivalent, because any process on the port satisfies it.

**Also to fix:**

- **Published ports:** CO-42 should warn that a published container port opens before the service inside is ready.
- **Contradiction:** EM-64 and reference §4.10 disagree about a service exiting during `finally`.

**Recommendation:** drop `depends_on` and `on_exit`, keep `output:`.

### D-4 `while`, `on_body_failure` and pagination (F-0008)

Findings: MIN-4, AGT-8, tracker F-0008.

**The problem:** both uses of `while` are waits that `until` writes directly. The only use of `on_body_failure` sets its default value. Pagination, the one job `until` can't do, isn't possible either:

- a `while` body can't see the previous iteration's captures (F-0008);
- loops have no iteration index;
- templates have no arithmetic.

**Options:**

- **(a) Cut `while` and `on_body_failure`.** `until` always continues after a failed body. Pagination is out of scope, and the docs point to tools that paginate themselves (`gh api --paginate`).
- **(b) Make pagination possible.** Expose the previous iteration's captures and an iteration index to `while`/`until`.

**Recommendation:** (a). (b) moves toward a scripting language (brief §1.1), and the realistic APIs agents use already paginate. The agent-user exercise reached the same answer for task 9.

### D-5 Smaller surface cuts

Each of these is independent:

| Item | Finding | Proposal | Verifier note | Recommendation |
|---|---|---|---|---|
| `for: { matrix }` | MIN-5 | Cut it; use nested `each` | `items` + `fields` also binds several names. Nesting breaks release-matrix's loop name `build ${OS}/${ARCH}` | **Keep** |
| `allow_empty` | MIN-6 | Make an empty `glob` succeed like the other sources | dotnet-build-test relies on the failing default | **Keep** |
| `keep_blank` | MIN-6 | Cut it; no use | — | **Cut** |
| `max_items` | MIN-6 | Apply it to every source, or move it to configuration | ADR 0003 Q8's reason isn't answered | **Apply it to every source** |
| `jitter` | MIN-7 | Cut it; also make `backoff.factor` an integer, which removes the decimal grammar | release-matrix has 4 parallel uploads, a small herd | **Cut `jitter`, keep a decimal factor** |
| `max_log_bytes` | MIN-8 | Move it to implementation configuration (ADR 0013 scope) | — | **Cut** |
| `encoding` values | MIN-8, IMP-11 | Keep only `utf-8` and `utf-16le` | IMP-11: Windows code pages need a rule (S-12) | **Keep only those two** |
| `zsh`, `fish` | MIN-9 | Cut from 1.0 (ROADMAP candidates) | fish appears in no example; zsh in one macOS step that bash covers | **Cut** |
| `{ var: X }` in `set`/`vars` | MIN-10 | Drop it; `'${X}'` is the same | It is one of the three secret sources in VA-49 | **Keep** (cheap, carries `secret`) |
| `race` | MIN-12 | Keep; show the deadline pattern as an idiom | — | **Keep** |

## 2. Semantic decisions

### S-1 Polling a JSON endpoint ends on one bad response

Finding: AGT-3 (confirmed).

**The problem:** `until: { result: stdout, json: .status, equals: ok }` fails the whole node on one non-JSON response, such as a proxy page or an empty body during startup. A condition error ends the loop (CO-22, CO-40), and `defined` isn't allowed with `result:`. deploy-health-check has the same latent bug: with `ignore_errors`, one bad 200 response triggers an immediate rollback.

**Options:**

- **(a)** In `until`, a condition error from the `result:` subject (invalid JSON, a path that doesn't resolve) counts as "not done yet" and is recorded in the iteration's result.
- **(b)** Allow `defined` with `result:` + `json:`, where invalid JSON counts as undefined.
- **(c)** Keep the rule and document a probe pattern.

**Recommendation:** (a). Polling is the case `until` exists for.

### S-2 A retried group can't retry a child's timeout

Finding: SQ-1 (confirmed).

**The problem:** EM-66 (ADR 0016) makes a group carry its child's error code, and `BK_TIMEOUT_TOTAL` is "never" retried. So `retry` on a group never retries a child that hit its own `timeout`, even with an explicit condition.

**Recommendation:** decide "never" by origin, not by code. It applies to the retrying node's own `timeout`, ancestor limits, cancellation and external stops. A descendant's timeout inside an attempt is an ordinary attempt failure.

### S-3 Cancelling a group child in a join

Finding: IMP-1 (confirmed).

**The problem:** EM-32 defines "confirmed" only for leaves ("reaped and its tree killed"). A cancelled group child's `finally` may run for up to 5 minutes (EM-31), while the join must complete after grace plus reap.

**Recommendation:** a cancelled child is confirmed when it has completed, including its `finally`, bounded by its `finally_timeout`. The grace plus reap bound applies per process. Add a timeline row, and a spike (new topic).

### S-4 The order of steps when a leaf ends

Finding: IMP-2 (partly).

**The problem:** the steps after a leaf's direct child exits aren't ordered: draining output, stopping descendants, binding captures, starting the next sibling. Implementations will differ on captures, durations and whether the next sibling can start while descendants are still in their grace period. The verifier noted that `race` winners are already fixed by EM-20.

**Recommendation:** a normative sequence:

1. The direct child exits.
2. Output is drained until EOF or `drain_timeout`.
3. The remaining tree is stopped, unless `keep_descendants`.
4. Captures and status are fixed, and the next sibling starts.

Verify it in brief spike 8.

### S-5 Stopping descendants per OS, and daemons (F-0009)

Findings: IMP-3 (confirmed), tracker F-0009.

**The problem:** EM-22 is one OS-independent guarantee. Its real strength differs:

- Windows has Job Objects.
- Linux needs a subreaper or a watchdog.
- macOS has nothing comparable.
- The case where the implementation itself is killed needs a watchdog process.

Separately, F-0009: a plan can't start a long-lived daemon such as Docker Desktop.

**Recommendation:** split EM-22:

- **MUST:** stop the processes in the leaf's own OS grouping (process group on Unix, job on Windows);
- **SHOULD:** the same when the implementation is killed (blokky-cli documents per OS how strong this is);
- **best effort:** processes that leave the grouping.

For F-0009, `keep_descendants: true` could also exempt a leaf's grouping from the end-of-run stop. That would be an explicit, visible way to start a daemon. Decide whether daemons are in scope at all.

### S-6 `for` with node-level `concurrency` or `fail_fast`

Finding: AGT-6 (confirmed).

**The problem:** on a node with `for`, node-level `fail_fast` and `concurrency` mean the inner group, while `for.fail_fast` means the loop. A `for` + `do` plan with `fail_fast: false` validates and silently leaves the loop fail-fast.

**Recommendation:** forbid node-level `concurrency` and `fail_fast` on a node that has `for`, and write the inner group as a child. Enforce this in the schema (C-1).

### S-7 Plan error or warning for undefined variables

Findings: IMP-10, SQ-6 (both confirmed).

**The problem:** whether a variable is "definitely bound" decides between a plan error and a warning, but the analysis isn't defined. It has holes: `when`, `ignore_errors`, `fail_fast: false` sequences, and a `race`/`any` that succeeds through skipped children. The parallel-collision rule covers only direct children's captures.

**Options:**

- **(a) Syntactic.** A reference is a plan error only if no binding of the name exists on any path from the root. Everything else is a run-time `BK_UNDEFINED_VARIABLE`, with an optional warning.
- **(b) A fully specified analysis** in variables.md §6.

**Recommendation:** (a). It is cheap, the same in every implementation, and testable. Extend the parallel-collision rule to any binding in the subtree.

### S-8 `collect` when the loop fails, and JSON items

Findings: SQ-11 (confirmed), AGT-7 (partly).

**The problem:** it's unstated whether `collect` binds its list when the `for` node fails. VA-55 says "like a capture", which implies no; ADR 0014 and 0016 promise `null` for failed iterations, which implies yes. Also, collected JSON captures are strings. The verifier showed that a nested `for: { items }` can read them, so this costs one loop level but isn't a dead end.

**Recommendation:** bind always, with `null` for failed, skipped and cancelled iterations, as an explicit exception to VA-18, because summaries after a failure are the main use. Keep items as strings and say so.

### S-9 What `defaults.leaf_timeout` limits

Finding: SQ-3 (partly).

**The problem:** probes have three conflicting definitions:

- syntax §4.3: the full `leaf_timeout`;
- CO-29: the smaller of 30s and `leaf_timeout`;
- the EM §5 table: reports `BK_TIMEOUT_TOTAL`.

It's also unclear whether `leaf_timeout` limits each attempt of a repeating leaf or the whole loop.

**Recommendation:**

- A probe's limit is its own `timeout`, otherwise the smaller of 30s and `leaf_timeout`, and hitting it is a condition error.
- For repeating leaves, `leaf_timeout` applies per attempt, like an implicit `attempt_timeout`.

### S-10 `tempdir` removal

Finding: AGT-5 (confirmed).

**The problem:** the overview says cleanup "never needs a hand-written `rm -rf`". But temp dirs are never deleted during a run, only by the implementation's `clean`. An agent either leaves clones of private repos on disk or adds the `rm -rf` the overview says isn't needed.

**Options:**

- **(a)** Reword the overview.
- **(b)** Also add an opt-in `tempdir: { remove: true }`, which deletes the directory at node end, after `finally`.

**Recommendation:** (a) and (b). Removing scratch clones without a reviewable `rm -rf` is one of the brief's stated benefits.

### S-11 `detail.rule` and rule IDs (F-0010)

Findings: SQ-2, AGT-11 (confirmed), tracker F-0010.

**The problem:** `detail.rule` holds RF-n IDs in reference §8 and ADR 0016, but slugs (`bounded-loop`) in errors.md and the conformance example. syntax.md has no rule IDs.

**Recommendation:** rule IDs everywhere: give syntax.md `SY-n` IDs, use them in `detail.rule`, and fix the conformance example. Pin the conformance feature-id vocabulary at the same time.

### S-12 Regex dialect, glob grammar, JSON path grammar and Windows code pages

Findings: IMP-8, AGT-4, IMP-9, SQ-5, SQ-4, IMP-11.

**Regex (IMP-8, confirmed locally):** defining it as ".NET NonBacktracking" ties the language to one library. `\d` matches Arabic-Indic digits, and `(?i)k` matches the Kelvin sign. The conformance suite already had to define a portable subset. **Recommendation:** a portable RE2-like subset:

- no lookaround, backreferences or atomic groups;
- ASCII `\d`, `\w`, `\s`;
- `$` matches only at the end of the value.

blokky-cli validates the pattern and translates it.

**Glob (AGT-4, IMP-9, SQ-5, confirmed):** there is no grammar, so whether `docs/**/*.md` matches `docs/README.md` is undecidable, and .NET's FileSystemGlobbing documents only `*` and `**`. **Recommendation:**

- `*` within a segment, `?`, `[...]`;
- `**` as a whole segment matching zero or more directories;
- no braces;
- matching is ordinal and case-sensitive on every OS.

Add conformance cases.

**JSON path (SQ-4, partly):** the grammar exists in ADR 0002 Q2 but not in the spec. **Recommendation:** move it into syntax.md §6 (this is a correction).

**Windows output code pages (IMP-11):** pwsh and many native tools write redirected output in the OEM/ANSI code page, so UTF-8 captures of non-ASCII output fail. **Recommendation:** implementations SHOULD give each Windows leaf UTF-8 output (code page 65001, plus the PowerShell prologue's `OutputEncoding`). Verify in a spike.

### S-13 `from_env` as a second input channel; honest wording on what is static

Findings: SEC-3 (partly), SQ-23.

**The problem:**

- The overview and reference §9 say everything a plan can execute is "determinable from the plan and its inputs before it runs". The glossary adds "except values only known at run time".
- Since ADR 0015, `from_env` in `vars`/`set` is an unchecked input channel. For example, `DEPLOY_DIR=/ blokky run` and `DEPLOY_DIR=.` have the same plan and inputs hash.
- The verifier noted that results §3.1 does record argv and the program. What's missing is the leaf's cwd, its env entries, and `from_env` values used elsewhere.

**Recommendation:** use one honest wording everywhere: the programs, the shape of every argv and environment, and the source of every dynamic value are static, while the values themselves may be known only at run time. Record each leaf's cwd and the `from_env` values a run used (masked when secret). Define the phase of a `vars` `from_env` error as validation (SQ-23).

### S-14 `host:` pruning in static views

Finding: SEC-10 (confirmed).

**The problem:** pruned branches disappear from static views, so a review on another machine shows less than the plan can run. It's also unstated whether `arch` means the OS's or the process's architecture under emulation.

**Recommendation:** static views show pruned branches, marked as pruned with their condition. Define `arch` as the OS's native architecture.

## 3. Corrections

These are clear fixes, listed by area. **Recommendation:** accept them as a batch. Where a correction depends on an F- or S-decision, it follows that decision.

### C-1 Schema

Findings: AGT-1, SQ-14, SQ-16, IMP-14.

- **Errors at the wrong key:** restructure the node and root schemas so an error is reported at its own key. Today `allOf` + `unevaluatedProperties` reports `retry: 3` as "'exec', 'retry' were unexpected".
- **Rules the schema can express:** move RF-4, 5, 12, 18, 25 and 28, the §5.2 sibling table and the "`for` only" cells into the schema. Rename reference §8 "Rules beyond the schema" (ADR 0001 already says so).
- **Contradictory validity rules (SQ-14):**
  - `on_body_failure` is called invalid on `any`, yet allowed through `while`/`until`;
  - `race` has `concurrency` "—" but `fail_fast` "for only";
  - add a `for` column to §5.2;
  - add RF rules: `finally_timeout` only with `finally`, `collect` only with `for`, `if` must have `then`.
- **Exit codes:** an exit-code type covering 0 to 4 294 967 295 for `ok_exit`, `true_exit` and `false_exit`. Today the integer branch stops at 2^31−1 while the string branch accepts 10 digits.

### C-2 Documentation agents rely on

Findings: AGT-2, AGT-9, AGT-10, AGT-12, AGT-14, SQ-19, MIN-12.

- The fix-it for `${X}` in an `exec` argument names the `set` template first (AGT-2).
- The fix-it for `retry: 3` becomes `max: 4`, with "max counts attempts, including the first" (AGT-9).
- The overview's subject list adds `command`, plus `port`/`output` as service-only (AGT-10).
- Errors raised inside loops carry `iteration` and `item` (AGT-12).
- An "Idioms" table: one canonical form per intent (wait until a command succeeds, poll output, is a tool installed, Windows vs Unix script, an optional step, a deadline with a custom code), with the error each produces (AGT-14, MIN-12).
- Canonical forms: declare the `exec` string form canonical for a static argv with no quoting, and add a canonical-forms table (SQ-19). The other route is to convert the 9 example lines that use it.

### C-3 Security and posture wording

Findings: SEC-2, SEC-4, SEC-5, SEC-6, SEC-7, SEC-8, SEC-9, SQ-7.

- **Template output used as code (SEC-4):** a template result passed as code (`bash -c`, `-e`, `eval`) is string interpolation into code. State this in VA-57 and shells §6 (ADR 0015's sentence overstated it). Static views show the template of a variable in a code position.
- **Secrets (SEC-5):**
  - a secret in an `exec` element is on the command line, visible to other local users, so implementations SHOULD warn;
  - container env secrets never go on the engine's command line;
  - values derived from a secret are secret.
- **Allowing `blokky run` (SEC-6):** say that a runner rule allowing `blokky run` without the hook or validator allows arbitrary code, like allowing `bash`. Mention `-ExecutionPolicy Bypass` for `powershell`.
- **Run-owned files (SEC-7):** an observable rule that run-owned files are created new, never reusing an existing path or link, and that removal never follows links. The brief already requires this. Also define what the scripts dir that containers see may hold.
- **Control characters (SEC-8):** rendered `name`/`log` text and recorded items are display-sanitized like error messages. This is labelled as hygiene, not a control.
- **Environment variable list (SEC-9):** the list of variables that influence execution is illustrative, not complete. Add `npm_config_*`, `PSModulePath`, `PAGER` and similar.
- **Routes into a process (SQ-7):** list `stdin` as the fourth route by which a variable reaches a process.
- **Option-like values in `exec` (maintainer, 2026-11-06):** a `{ var: X }` element is one argument, but a value that starts with `-` still reaches the program as an option. Add guidance: constrain inputs with `matches` (no leading `-`) and pass `--` where the program supports it. This is hygiene, not a control.
- **Which user runs what (SEC-2):** state the user for every process the plan starts. This depends on D-1.

### C-4 Execution model and results clarifications

Findings: SQ-8, SQ-9, SQ-12, SQ-13, SQ-15, SQ-18, SQ-22, IMP-4, IMP-15, IMP-16, IMP-17.

- **`finally` deadlines (SQ-8):** EM-25 states the exception EM-31 implies: a `finally` list's deadline is its own `finally_timeout`.
- **Retry table (SQ-9):** one complete "retried" table in errors.md, with a yes/no for every code, and reference §5.5 points to it.
- **`ignore_errors` (SQ-12):** a node's own timeout shows as `timed_out` with `ignored: true`. `ignore_errors` on the root is a plan error.
- **Captures of a failed attempt (SQ-13):** they are evaluated best-effort for retry conditions only, and a capture error leaves the name undefined.
- **Skipped nodes (SQ-15):**
  - RS-6 keeps the probe record of a node skipped by a `when` probe (CO-31);
  - a node pruned by `host:` is `skipped` with reason `pruned`.
- **Container `cwd` (SQ-18):** CP-1 gets "except CP-14", and the example cites CP-14.
- **Normative keywords (SQ-22):** capitalize the lower-case requirements in results, VA-51, reference §4.3 and shells §6.
- **Windows graceful stop (IMP-4):** each Windows leaf SHOULD get its own hidden console or process group so CTRL_BREAK can be delivered. When none exists, the run result records `graceful: none`.
- **PowerShell prologue (IMP-15):** SH-17's plan error also covers attributes before `param`, and execution-policy failures surface as `BK_SPAWN_FAILED`.
- **Fixed timings (IMP-16):** readiness polling becomes "at most 250ms between checks", the reap timeout is implementation-defined (at least 1s), the jitter RNG MAY be seedable, lock waiters are served in no particular order, and `drain_timeout` and lock polling join the test-clock list.
- **Lock owner (IMP-17):** under `sudo blokky run`, the lock owner is the invoking user, and the lock store is local.

### C-5 Examples

Findings: SQ-10, SQ-20, MIN-11, AGT-3.

- **when-guard (SQ-10):** add a `defined` guard. Today it fails outside CI instead of skipping.
- **dotnet-build-test (SQ-20):** the header says vulnerabilities are checked after failed tests, but the failing assert stops the run first. Fix the order or the comment.
- **changed-files-lint (MIN-11):** use the `command:` subject instead of `any` + `assert` + a `command -v` probe.
- **deploy-health-check:** follows S-1.

### C-6 Conformance format

Findings: IMP-12, IMP-13.

- **Base environment (IMP-12):** a passthrough list per requirement. A pwsh installed as a .NET tool needs `DOTNET_ROOT`. The verifier found that rootless Podman works with a stripped environment.
- **Case actions (IMP-13):**
  - add a `kill` action and `after_run` checks, needed for EM-22, EM-60 and locks surviving a crash;
  - list the rules that can't be tested through the result, with a reason (CF-51 wording);
  - let the runner widen timing tolerances on Windows.

### C-7 Mechanical fixes

Finding: SQ-21. Twelve items (11 confirmed) are listed in `spec-quality.yaml`, including:

- the `vars` synonym entry for `inputs`;
- the broken VA-56 anchor;
- unexplained gaps at SH-31, CP-31 and CP-33;
- CP-10 cited where CP-14 is meant;
- conflicting definitions of "group" and of `ended_by`.

ADR 0010's EM-54 citation needs a note, not an edit.

## 4. Spikes proposed

The implementability lens proposed 11 spikes. The verifier mapped six onto spikes the brief already plans; the rest are new.

| Topic | Rules | New or merge |
|---|---|---|
| Group children in joins (S-3) | EM-31, EM-32, EM-58 | **new** |
| Leaf completion sequence (S-4) | EM-18, EM-20, EM-21, EM-24 | merge into brief spike 8 |
| Tree kill and lifeline per OS (S-5) | EM-3, EM-15, EM-21, EM-22 | merge into brief spikes 1 and 5 |
| Windows graceful stop | EM-15, EM-26, EM-30 | merge into brief spike 2 |
| `become` transport and sudoers | CP-26 to CP-30 | brief spike 11; depends on D-1 |
| In-container helper | CP-4, CP-8, CP-15 | merge into brief spikes 11 and 13; depends on D-2 |
| Engine matrix: Docker and Podman, SELinux, rootless, desktop VMs | CP-7, CP-9 to CP-11, CP-16, CP-17 | **new**; depends on D-2 |
| Regex subset on NonBacktracking under AOT (S-12) | CO-13, CO-14, RF-22 | merge into brief spike 9 |
| Windows output code pages (S-12) | VA-20, VA-21, SH-12, SH-17 | **new** |
| PowerShell prologue scanner and execution policy | SH-13, SH-17, SH-20 | **new** |
| Conformance runner: kill action, post-run checks, Windows timing (C-6) | CF-22, CF-40, CF-57, EM-60 | **new** |

The acceptance criteria for S-3 and S-4 assume the proposed rules, so those decisions come first.

## 5. The agent-user exercise

The reviewer wrote plans for 10 tasks after reading only `overview.md`, `syntax.md` and `reference.md`, then graded them against the rest of the spec. All 10 passed the schema.

| Task | Result | Why |
|---|---|---|
| 1. `npm test` with retry on timeout only | correct | Right only because §5.2 says `max` counts attempts; the fix-it would have given one retry fewer (AGT-9) |
| 2. Poll a JSON health endpoint | semantic error | One non-JSON response ends the poll (S-1) |
| 3. Temp dir, clone, build, cleaned up | not expressible | Temp dirs aren't removed (S-10); `rm -rf` in `finally` works |
| 4. Lint `docs/**/*.md`, 4 at a time | correct | But glob semantics are undefined (S-12) |
| 5. pnpm if installed, else npm | correct | `command:` is hard to find from the overview (AGT-10) |
| 6. Postgres service, then `dotnet test` | semantic error | A published port opens before Postgres is ready (D-3) |
| 7. Fail if the branch is `main` | correct | — |
| 8. PowerShell on Windows, bash elsewhere | correct | Fails where only Windows PowerShell 5.1 exists |
| 9. Paginate `gh api` | not expressible | D-4 |
| 10. Two parallel builds, first failure cancels | correct | — |

The reviewer also fed the schema 12 typical mistakes. Three of them that the reference forbids were accepted, and every error message blamed the wrong key (C-1).

## 6. Maintainer additions

Raised by the maintainer while reading the spec next to this report (tracker F-0011, F-0012). Both are decided; they join the ADRs for `1.0.0-draft.4`.

### D-6 `return`: end a sequence early (F-0011, decided 2026-11-06)

A "nothing to do" check today wraps the rest of the plan in an `else` ([changed-files-lint](https://github.com/gergodrazsdik/blokky-specs/blob/main/examples/scenarios/changed-files-lint.blokky.yaml) lines 24–26). Decision: add a `return` kind.

```yaml
do:
  - exec: [git, diff, --name-only, --diff-filter=ACMR, { var: RANGE }, --]
    capture: CHANGED
  - when: { var: CHANGED, empty: true }
    return: "No files changed since ${BASE}; nothing to lint"
  - name: choose how to run ruff
    # ...
```

- `return` ends the nearest enclosing `do` sequence successfully: the plan's top-level `do`, a `do` group, or a `for` body (where it ends the iteration, so it acts as `continue`). Siblings after it are `skipped` (a new `skip_reason`). Its value is a display string, logged like `log`.
- `if` branches are transparent: a `return` in `then` or `else` ends the `do` that contains the `if`, so `if`/`then: [return]` works without `when`.
- No `break` for loops.
- A `return` whose nearest sequence would be reached through `all`, `any` or `race` is a plan error.
- A `return` without `when` that isn't inside an `if` branch is a warning (the nodes after it never run). This is a static check on the plan tree.
- To settle in the ADR: `return` inside `finally`, and in `while`/`until` bodies (ends the iteration, then the condition is checked again); declared `outputs` still unbound at a plan-level `return` are an error, as at the normal end; the effect on the undefined-variable check (S-7) is the same as a `set` in one `if` branch.

### C-8 `command:` checks that the program is executable (F-0012, decided 2026-11-06)

`{ command: NAME, exists: true }` is true only if `NAME` resolves to a file that exists **and** is executable for the current user (on Windows: the `.com`/`.exe` rule), matching the spawn-error rule in reference.md §5. A path (`/bin/sh`, `./tools/gen`) is already valid under CO-45; `NAME` stays a literal (CO-46).

### Decisions on D-1 to D-5 (2026-11-06)

The maintainer's principle for this batch: a feature that no example uses is a gap in the examples, not a reason to cut it. Cuts need a cost the feature doesn't repay.

- **D-1 `become`:** cut `become` and `defaults.become`, and don't list them as ROADMAP candidates: not planned. Plans call `sudo -n` as in bash. The root-drop mode (`sudo blokky run`) still needs the scope rules of SEC-2/SQ-17 and the lock-owner rule of IMP-17.
- **D-2 `container`:** keep, and decide again after the container spikes.
  - Only compatible images are supported: the image provides `sh`, `sleep` and `kill` (no helper shipped). An image without them fails at container start, naming what is missing.
  - Env values never go on a command line: an env file (mode 0600) passed to `exec --env-file` (Podman; Docker 23.0+).
  - Blokky never relabels host paths; ownership and labelling of mounts are engine-defined, and the run result says when labelling was disabled for Blokky's mounts.
  - Keep `env_pass` as a shorthand (like `export`) and add an example that uses it.
  - Drop `container.user`. The implementation maps the invoking user into the container where the engine allows it (rootless Podman `--userns=keep-id`, rootful Docker `--user` with the invoking UID), so non-root build images can write the cwd mount. Provisional; the engine-matrix spike verifies it with non-root build images, and `user` comes back only if plans need to choose.
- **D-3 `services`:** drop `depends_on`; services start in list order, each ready before the next, and stop in reverse order. Keep `on_exit` (helper services such as a mail catcher or a port-forward) and the `output:` readiness subject. A service that exits after the body ended (during `finally`) is recorded, not a failure (resolves the EM-64 / reference §4.10 contradiction). Add CO-42's published-port note.
- **D-4 `while`, `on_body_failure` and pagination (F-0008):**
  - Keep `while` and `on_body_failure`. Fix the examples: the two wait-style `while` loops become `until`; add a work-loop `while` (delete in batches while any remain) and an `until` with `on_body_failure: stop` in wait-for-ci (a permanent error fails at once instead of after the timeout).
  - Keep single assignment (VA-36).
  - Pagination is in 1.0 through loop-carried variables. `carry: { CURSOR: NEXT }` on `while`/`until` binds `CURSOR` once per iteration: `''` in the first, then the previous iteration's `NEXT` capture; long form `{ from: NEXT, initial: '…' }`. `collect` becomes valid on `while`/`until` too, with the same shape as on `for`.
  - A named iteration index on `while`, `until` and `retry`: `index: NAME` (0-based, like `for`'s `<AS>_INDEX`), long form `{ as: NAME, start: 1 }`. An integer, visible in the body and the node's condition, not published.

  ```yaml
  - until: { var: NEXT, empty: true }
    carry: { CURSOR: NEXT }        # '' in iteration 1, then the previous NEXT
    collect: { PAGES: PAGE }       # every PAGE, as a list, after the loop
    max: 100
    do:
      - exec: [./fetch-issues, --cursor, { var: CURSOR }]
        capture:
          PAGE: { json: .items }
          NEXT: { json: .next_cursor }
  ```

- **D-5:** keep `matrix`, `allow_empty`, `keep_blank` (add an example), `jitter`, all four `encoding` values, `{ var }` in `set`/`vars` and `race`. `max_items` applies to every `for` source. `max_log_bytes` moves to implementation configuration. Keep `zsh`; cut `fish`.

## Next steps

1. **Maintainer review (T-0051):** read this report next to the spec. The decisions are asked in batches (D-1 to D-5, then S-1 to S-14, then the corrections as a batch). D-6 and C-8 are already decided (§6).
2. Accepted items become specs ADRs and `1.0.0-draft.4`. Changes to the spike plan go into blokky-cli.
3. Items that turn out to fit poorly are raised again as open questions (brief §1.5).
