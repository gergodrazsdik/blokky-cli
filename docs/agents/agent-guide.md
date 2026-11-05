# Agent guide

How coding agents should write and run Blokky plans: heredoc convention, inputs, run budget, cleanup flow, canonical patterns and YAML pitfalls.

Status: not started. Notes collected for the guide follow.

## Notes for the guide

### Work that needs root, without `become`

(Moved from blokky-specs `containers-and-privilege.md` §4 on 2026-11-05; it describes CLI usage, not the language.)

An agent can prepare work that needs root, and the user runs it:

1. The agent writes the plan to a `*.blokky.yaml` file.
2. The user reviews it (`blokky plan`, `blokky leaves`) and notes the printed SHA-256.
3. The user runs it: `sudo blokky run --expect-sha256 <hash> [--input NAME=VALUE …] file.blokky.yaml`. The hash covers the plan; the reviewed input values are visible in `blokky plan --input …` and recorded in the report.

Any password prompt happens in the user's terminal, before Blokky starts.
