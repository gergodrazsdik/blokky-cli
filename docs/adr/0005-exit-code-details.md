# 0005: Exit-code precedence and Windows events

- Status: accepted
- Date: 2026-11-05
- Questions: none from the brief's §4 list; open items of [ADR 0002](0002-cli-run-lifecycle.md) (blokky-cli PR #8), settled while drafting the exit-code table in `docs/cli.md` (T-0026)

## Context and problem statement

ADR 0002 fixed the exit-code table but left open which code wins when a run fails before starting with errors from several phases (for example a sha mismatch together with plan errors), and which code Windows console close, logoff and shutdown events produce. Both were asked as multiple choices with a recommendation, on 2026-11-05. (A third open item, the code for `clean` on an unknown or in-progress run, was settled as 2 in blokky-specs `spec/errors.md`.)

## Considered options

| Question | Options (chosen in bold) |
|---|---|
| Precedence among pre-run errors | **pipeline order**; lowest code |
| Windows CTRL_CLOSE, CTRL_LOGOFF, CTRL_SHUTDOWN | **129, like SIGHUP**; 143, like SIGTERM |

## Decision outcome

Both answers follow the recommendation.

- **Pipeline order:** `blokky run` checks arguments (2), then `--expect-sha256` (7), config (4), the plan (3), input values (2) and approval (5), and stops at the first failing step. Its code is the exit code; the envelope lists every error found in that step. A sha mismatch stops everything else, because nothing in untrusted plan bytes should be checked or reported.
- **Windows session events** (console close, logoff, shutdown) exit with 129, the counterpart of SIGHUP; Ctrl-C and Ctrl-Break stay 130.

### Consequences

- Good: the exit code always names the first thing to fix.
- Neutral: an agent with both a plan error and an input error sees the plan error first and the input error on the next attempt.
- Neutral: blokky-specs `output-contract.md` OC-11 changes from "exit-code order" to the pipeline order (blokky-specs PR #21).

## More information

- Applied in: `docs/cli.md` (Exit codes); blokky-specs `spec/output-contract.md` OC-11, `spec/execution-model.md` EM-39.
- Tracker: T-0026.
