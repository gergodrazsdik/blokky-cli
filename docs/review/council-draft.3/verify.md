# Agent council: verification brief

You verify one council reviewer's findings on the **Blokky language spec `v1.0.0-draft.3`**. Read `BRIEF.md` in this directory first: it describes the material (read-only snapshot at blokky-specs at tag `v1.0.0-draft.3`), the posture and the findings format.

Your job is **adversarial**: try to disprove each finding against the text. You did not write them and owe them nothing.

For every finding, check:

1. **Facts.** Is every quote accurate and in the cited place? Are counts and "no example uses X" / "every scenario does Y" claims true (grep the examples)? Does the rule actually say what the finding says, read in context with the rules around it, the glossary and the ADRs?
2. **Already answered.** Does a rule elsewhere, or an ADR, already resolve it? If the finding challenges an ADR, does it answer the ADR's actual argument?
3. **Severity and kind.** Are they right by the brief's definitions?
4. **The proposal.** Would it break something else in the spec (another rule, an example, a scenario)? Do not judge whether the change is *desirable*: that is the maintainer's call. Only whether it is coherent and what it would cost.

Do not modify any repo file; no git, no `gh`. Work only in your scratch dir if you need one.

## Output

Write `council/<lens>.verified.yaml`, one entry per finding id in the input, in the same order:

```yaml
- id: MIN-1
  verdict: confirmed | partly | refuted
  severity: major            # your corrected severity (same as input if right)
  notes: |
    what you checked; for partly/refuted, exactly which claim is wrong and the text that shows it
  proposal_impact: |
    rules, examples or scenarios the proposal would touch (empty if none)
```

`partly` means the core holds but a claim, count, quote or the severity is wrong. `refuted` means the core claim is wrong or already resolved.

If the reviewer's file has an extra section (`verdicts:`, `exercise:`, `spikes:`), spot-check it (at least a third of the entries, all cut/merge entries) and add a top-level note as the last list entry: `- id: extra-section` with `verdict` and `notes`.

Validate the YAML parses. Final message: counts per verdict and the refuted/partly ones in one line each.
