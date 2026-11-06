# Agent council: review brief (shared by all lenses)

You are one reviewer on a council reviewing the **Blokky language spec, `v1.0.0-draft.3`**. Blokky is a small, declarative YAML block language and runner that wraps shell commands in structured-programming blocks (timeouts, retries, polling, temp dirs, cleanup). Its main users are coding agents; secondarily, the humans who review what those agents run. Each reviewer has one lens and does not see the others' findings. The maintainer decides what to do with the findings; accepted ones become ADRs and `1.0.0-draft.4`.

## What to read

- **The spec under review (read-only snapshot of the tag):** blokky-specs at tag `v1.0.0-draft.3`
  - `spec/` (start with `overview.md`, then `syntax.md`, `reference.md`, `execution-model.md`, `variables.md`, `conditions.md`, `shells.md`, `containers-and-privilege.md`, `errors.md`, `results.md`, `glossary.md`; `prior-art.md` for context)
  - `schema/blokky.schema.json` (normative plan schema)
  - `examples/` (`README.md`, `basics/`, `scenarios/`)
  - `conformance/README.md`, `conformance/testkit-interface.md`
  - `adr/0001`–`0016`: the reasons behind past decisions
  - `ROADMAP.md`
- **Principles (context, not under review):** `blokky-cli/docs/design/brief.md` §1 (lines 48–122: what Blokky is, design principles, security posture, evolution). The brief's §2–§4 are historical input; where they disagree with the spec, the spec and ADRs win.
- Known open findings, already tracked (don't re-report them, but you may add evidence): F-0008 (`while` bodies can't see the previous iteration's captures, so cursor pagination can't be written), F-0009 (EM-22 stops every descendant at run end, so a plan can't start a long-lived daemon), F-0010 (`syntax.md` has no rule IDs; the conformance feature-id vocabulary isn't pinned).

## Rules

- **Do not modify any file** in either repo. Write only to your own output file (below) and to your own scratch directory `council/<lens>/` if you need one. Don't commit, push or use `gh`.
- **ADRs may be challenged**, but only with an argument the ADR doesn't already answer: cite the ADR text you are answering.
- **Evidence over opinion.** Every finding quotes the text (file, section, rule ID) or shows a plan that demonstrates it. If you can't point at it, it's not a finding.
- **Severity:** `blocker` (contradiction or gap that makes a feature unimplementable or a plan's meaning undecidable; or a security-posture violation), `major` (a real user would hit it: wrong plans, surprising behaviour, a feature that doesn't earn its place), `minor` (wording, small inconsistency, missing example). Be stingy with blocker.
- **Kinds:** `cut` (remove a feature), `simplify` (reduce a feature's surface), `fix` (a rule is wrong or contradicts another), `clarify` (ambiguous or underspecified), `question` (needs a decision; no clear right answer).
- The project's security posture: Blokky must never be *less* safe than the same commands in plain bash, must not grant capabilities, bypass the runner's permission system or hide what will run; it is **not** a sandbox; hygiene (masking, lint, sanitization) is not a control and must not be presented as one; resource bounds are for ergonomics, not security. Don't propose controls that only work if agents can *only* use Blokky.
- Quality over quantity: 10 well-evidenced findings beat 40 thin ones. Merge related points into one finding.

## Output

Write your findings as YAML to `council/<lens>.yaml`, as a list:

```yaml
- id: <LENS-PREFIX>-1          # e.g. MIN-1, AGT-1, SEC-1, IMP-1, SQ-1
  lens: <lens>
  kind: cut | simplify | fix | clarify | question
  severity: blocker | major | minor
  where: spec/reference.md §2.6, EM-58   # files, sections, rule IDs
  claim: one sentence
  evidence: |
    quotes, or a plan, that show it
  proposal: |
    what to change (or the options, for a question)
  confidence: high | medium | low
```

Your lens prompt may ask for an extra section; put it in the same file under a top-level key as described there (in that case the file is a mapping with `findings:` plus that key).

Validate that the file parses as YAML (`python3 -c 'import yaml,sys; yaml.safe_load(open(sys.argv[1]))' <file>`, or `yq . <file>`).

Your final message: a short summary (counts by severity and kind, the 3–5 most important findings in one line each). The file is the deliverable.
