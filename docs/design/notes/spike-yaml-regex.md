# Spikes 3 and 9: YAML subset and portable regex

Status: done 2026-11-07, branch `spikes/yaml-regex` (unmerged). Spike plan rows 3 and 9 ([spike-plan.md](../spike-plan.md)).

Code: `spikes/yaml-regex/` (throwaway). A .NET 11 console app (`net11.0`, SDK 11.0.100-rc.3, YamlDotNet 18.1.0, `InvariantGlobalization`), Native AOT, plus Python drivers that read the conformance cases of blokky-specs `8f16bb0`.

| File | What it is |
|---|---|
| `PlanText.cs` | Size cap, BOM and UTF-8 checks, character lint, SY-1 line map |
| `PlanYaml.cs` | YamlDotNet scanner and event parser only: subset checks, a positioned node tree |
| `MiniSchema.cs` | A stand-in for the schema layer, just enough for the `syntax/yaml` cases (not a design) |
| `PortableRegex.cs`, `CaseTable.g.cs` | Subset parser and validator, translation to `NonBacktracking`, simple uppercase table |
| `run_cases.py`, `fuzz.py`, `extra-regex.tsv` | Case driver, mutation fuzzer, extra regex edge checks |

```bash
cd spikes/yaml-regex
dotnet publish -c Release -r linux-x64 -p:PublishAot=true -p:TrimmerSingleWarn=false
B=bin/Release/net11.0/linux-x64/publish/SpikeYamlRegex
uv run run_cases.py $B ../../../blokky-specs "$TMPDIR/cases"
uv run fuzz.py $B ../../../blokky-specs "$TMPDIR/fuzz" 40
$B regex-probe
```

## AOT and trimming (both spikes)

`dotnet publish -c Release -r linux-x64 -p:PublishAot=true -p:TrimmerSingleWarn=false` from a clean `obj/`: exit 0, **no warnings at all** (no IL2xxx, no IL3xxx; `TrimmerSingleWarn=false` lists warnings per call site instead of folding them into one IL2104 per assembly). The binary is 2.8 MB (stripped). Every result below was produced by the AOT binary; the Debug JIT build gives the same results.

## Spike 3: YamlDotNet under AOT

**Answer: yes, with YamlDotNet's `Scanner` and `Parser` only (no `Deserializer`, no reflection).** The encoding pass and the character lint run on the bytes and the decoded string before YAML. Every subset rule is detectable from events or scanner tokens. Positions must be recomputed from `Mark.Index`, and YamlDotNet needs defensive wrapping (below).

### Evidence

`run_cases.py` runs every case in `syntax/text` and `syntax/yaml` (45 cases) and compares code, line, column and the asserted `detail` (`rule`, `codepoint`, `offset`), honouring `exhaustive`:

```text
== spike3: yaml 29 pass / 0 fail, run 11 pass / 0 fail, schema 5 pass / 0 fail
```

- **yaml (29):** validation cases whose codes are YAML-layer (`BK_PLAN_ENCODING`, `_CHARACTER`, `_TOO_LARGE`, `_YAML`, `_DUPLICATE_KEY`, SY-20). They pass with the exact code and position.
- **run (11):** the plan has to validate with no error: BOM ignored, CRLF and TAB, non-ASCII, `---`, scalars as strings, quoting, pitfalls that only warn. Warnings came out for `pitfall-comment-after-exec-string` (2:7) and `pitfall-exec-flow-bracket` (2:7).
- **schema (5):** the errors come from field rules (SY-53, SY-54, RF-21, SY-89 `script: [`, the unknown key `1`). `MiniSchema.cs` hard-codes those few fields to show that the tree carries the right positions; it isn't a validator.
- `syntax/keys` (unknown keys, did-you-mean) is schema-layer work and wasn't attempted.

Mutation fuzzing (`fuzz.py`, 1–3 random edits with YAML punctuation to each of the suite's 1147 plans): the first run found crashes and a hang. With the mitigations below, a run of **45 880 mutants had 0 hangs and 0 crashes** (5 s timeout each).

### How each rule is detected

| Rule | Mechanism |
|---|---|
| SY-5/6/7 | Byte checks before decoding: UTF-32 BOMs before UTF-16 (`FF FE 00 00` starts with `FF FE`). `Rune.DecodeFromUtf8` rejects overlong forms, surrogates and truncation; `offset` is the raw byte offset. The UTF-8 BOM is stripped before YamlDotNet sees the text: given a leading U+FEFF, YamlDotNet makes it part of the first key (`"\uFEFFa"`). |
| SY-9 | Size on raw bytes; depth counted on `MappingStart`/`SequenceStart` events while streaming, before any tree is built. |
| SY-10..13 | One `Rune` pass over the string. A CR not followed by LF is rejected and ends a line in the line map. |
| SY-14 | `NodeEvent.Anchor`, `AnchorAlias` events, and a plain scalar key `<<` (a quoted `'<<'` is an ordinary key). |
| SY-15 | `NodeEvent.Tag` (also the non-specific `!`); `VersionDirective`/`TagDirective` **tokens**, because `DocumentStart` doesn't locate them. |
| SY-16 | A second `DocumentStart` event. Its mark is the `---`, or the first content after `...`; a trailing `...` alone is accepted. |
| SY-17 | A non-scalar key event (`[name]:`). The `?` indicator isn't visible in events, so a `Key` token whose source character is `?` marks it (block and flow). An empty key (`: a`) is a `Value` token with no key token before it. |
| SY-18 | A set of key strings per mapping; quoting doesn't matter. |
| SY-20 | A plain scalar value of length 0. A flow entry with no `:` (`{ exec: a,b }`) is excluded: there the SY-89 targeted error replaces it (SY-3). |
| SY-89 comment | Scanner with `skipComments: false`; `Comment.IsInline` lines are matched against the end line of a plain `exec`/`script` value. |
| SY-89 others | From styles in the tree: a flow sequence under `exec`/`script`; a quoted scalar followed by text (see positions). |

### Positions (SY-1)

- `Mark.Line`/`Mark.Column` can't be used directly. Columns count UTF-16 units: `log: [😀, *a]` gives column 11 for the alias, where SY-1 says 10. YamlDotNet also treats U+2028/U+2029 as line breaks. The prototype takes `Mark.Index` (a UTF-16 index into the BOM-less string) and maps it with its own line table (LF, CRLF, lone CR; code points). That passed every asserted position, including after a two-byte `é` and an emoji.
- A quoted scalar's start mark is its opening quote, and invalid escapes are reported there (`"^\d+$"` → 4:32). For a block scalar, the prototype moves from the indicator to the first content character (checked by hand: 4:5).
- `exec: "a" b`: YamlDotNet fails at the end of the line (2:24). The prototype relocates to the opening quote (2:7) when the last event before the exception is a quoted scalar on the same line.
- Some syntax errors are located poorly: a TAB used as indentation is reported at 1:1, and an unclosed `[` followed by a block key at the last good event. Neither is in the suite. A production parser needs a position-fixing table per error kind, or its own error recovery.

### YamlDotNet 18.1.0 defects found

1. **Infinite token loop:** `Scanner(skipComments: true)` on `log: a: b` yields `Error` tokens forever. `Parser` and `skipComments: false` throw `SemanticErrorException` instead, but the prototype's token pass hung until it stopped at the first `Error` token.
2. **Non-YamlException exceptions without marks:** `InvalidOperationException` (`exec: [a, b` then `name: x`) and `ArgumentException` (`log: !!st[r hello`, "Global tags must be valid URIs"). The prototype catches everything except OOM and maps it to `BK_PLAN_YAML`.
3. **Malformed event stream:** an empty key in a nested block mapping (`x:\n  : a`) produces a key event with no value event, and the key's mark is wrong (1:1). The tree builder must not assume key/value pairs.
4. **YAML 1.1 line breaks:** a plain scalar containing U+2028 (`log: a<LS>b`) fails with "did not find expected key". YAML 1.2 says it's an ordinary character. In quoted scalars it's kept unchanged.

### Recommended design

- Pipeline: bytes → `PlanText` (cap, BOMs, UTF-8, lint; stop on any error) → YamlDotNet `Scanner` token pass (directives, `?`, empty keys, inline comments) → `Parser` events → a positioned node tree with subset checks → the schema/rule validator on the tree. No `Deserializer`, no `YamlStream`/representation model; nothing reflection-based.
- Wrap YamlDotNet behind one adapter that: maps every exception to `BK_PLAN_YAML`; bounds token and event loops; tolerates odd event sequences; converts `Mark.Index` with Blokky's own line map; and fixes known error positions. Pin the YamlDotNet version, and keep the mutation fuzzer plus the suite as regression tests for upgrades.
- Alternative to weigh at 0.1 design time: a hand-written parser for the subset only (block/flow collections, plain/quoted/block scalars, comments). It would remove defects 1–4 and give native positions and messages, at the cost of owning a YAML scanner. Recommendation: keep YamlDotNet for 0.1 behind the adapter, and decide after the full syntax suite and fuzzing run on the real validator.

### Spec changes proposed (blokky-specs)

- **SY-12:** also reject U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR. They are invisible line breaks in some editors and YAML 1.1 parsers, so a plan would be read differently by tools: the same Trojan Source class as the bidi controls. A rejected character, rather than a parser-dependent `BK_PLAN_YAML`.
- **SY-7:** say that `detail.offset` counts from the first byte of the raw text, BOM included (the prototype does this; no case has a BOM before an invalid byte).
- **SY-20:** say whether an empty sequence item (`do:\n  -`) is an error. The prototype doesn't flag it; by the same reasoning ("an empty value is never unset") it should be one, located at the `-`.
- **SY-17:** list the empty key (`: a`) as a key that isn't a plain or quoted string (YAML allows it as a null key).

## Spike 9: regex subset on `RegexOptions.NonBacktracking`

**Answer: yes, but not by passing the pattern through.** .NET's own `$`, `.`, `\d\w\s`, `\b` and `IgnoreCase` all differ from SY-61/CO-12 (probe below). The prototype parses the pattern into its own AST, validates it against SY-61/SY-62, and emits a .NET pattern that uses only character sets of its own, `\A`, `\z`, `\b`/`\B`, groups and quantifiers. It matches over a **byte-mapped value**: the value's UTF-8 bytes, each byte ≥ 0x80 mapped to U+E000+byte, a private-use character that .NET never counts as a word character. Then:

- `.` and classes are one code point: sets are compiled to UTF-8 byte sequences, as RE2 does;
- `\b`/`\B` are ASCII-only for free: non-ASCII bytes are never word characters for .NET;
- `(?i)` is CO-12's simple uppercase mapping: the parser expands literals and classes to `{c | Up(c) ∈ Up(S)}` (classes folded before negation), and `RegexOptions.IgnoreCase` isn't used;
- `$` → `\z`, `^` → `\A`. The search is `\A(?:<any code point>)*?(?:<pattern>)`, so matches start only on code point boundaries; otherwise `\B` would match inside a multi-byte character.

### Evidence

```text
== spike9: schema 3 pass / 0 fail, run 7 pass / 0 fail          (plan errors and positions, RF-22)
== spike9 suite regex evaluations: 43 extracted
regex checks: 43 pass, 0 fail
-- for comparison, patterns passed to .NET unchanged (NonBacktracking, IgnoreCase):
regex checks: 33 pass, 10 fail
== spike9 extra edge checks (extra-regex.tsv):
regex checks: 54 pass, 0 fail
```

- **Validation (3 cases, 13 errors):** `regex-does-not-compile`, `regex-outside-subset-engine` and `regex-outside-subset-syntax` give `BK_PLAN_VALUE` with `rule: RF-22` at the opening quote, exhaustively. The rejects cover lookaround, backreferences, atomic groups, `a++`, named groups, `\p{L}`, `(?m)`, a non-leading `(?i)`, `\A`/`\z` and `\q`. The run cases validate with no error.
- **Evaluation (43 checks)** were extracted from the run outcomes of 13 cases: `matches-*`, `ignore-case-simple-uppercase`, `ignore-case-operators`, `regex-subset-valid`, input `matches`, `assert`, and the trimmed and untrimmed stderr of `output-stderr-matches-end`. All pass. With the patterns passed to .NET unchanged, 10 fail: `abc$` on `abc\n`, `\d` on U+0663, `\w`/`\W` on `é`, `\s` on NBSP, `^.\bber$` on `über`, `\u{42}`/`\u{A0}` invalid, the Kelvin sign, `ſ`.
- **Extra checks (54):** astral `.` and ranges, `(?i)[a-z]` matching `ſ` and `ı` but not the Kelvin sign, `(?i)[^a]` vs `A`, `ς`/`σ`/`Σ`, `ǅ`/`ǆ`, Deseret, `\B` on `aéb` (false), VT/FF in `\s`, NEL and LS not, plus 22 rejects (`a{2}{3}`, `a**`, a bare `]` or `}`, `a{,3}`, `a{1001}`, `\f`, `\v`, `[]`, `[\b]`, `\1`, `\Z`, `^*`, `\b+`, `\u{D800}` …).

`regex-probe` output (AOT, abridged):

```text
IgnoreCase: Kelvin sign vs ^k$ (CO-12: no)   True
IgnoreCase: long s vs ^s$ (CO-12: yes)       False
IgnoreCase: dotless i vs ^I$ (CO-12: yes)    False
IgnoreCase: final sigma vs ^σ$ (CO-12: yes)  False
NB lookahead a(?=b)                          NotSupportedException
NB \b in über (ASCII: boundary)              False
NB + ECMAScript                              ArgumentOutOfRangeException
NB lazy a{1,2}?b on aaab                     True
NB abc$ on abc\n (spec: no)                  True
NB ^.$ on U+1F600 (spec: yes)                False
OrdinalIgnoreCase long s = s (CO-12: yes)    False
OrdinalIgnoreCase dotless i = I (CO-12: yes) False
char.ToUpperInvariant(U+017F) = U+017F (Unicode: U+0053)
```

### NonBacktracking limits

- **Supported:** greedy and lazy quantifiers (lazy only changes which match is found, never `IsMatch`), counted repetition, `\A`, `\z`, `^`, `$`, `\b`, `\B`, alternation, non-capturing groups. Every subset construct works after translation.
- **Not supported:** lookaround, backreferences, atomic groups and conditionals throw `NotSupportedException`. They're outside the subset anyway. `RegexOptions.ECMAScript` (which would give ASCII `\b`) can't be combined with it.
- **Automaton size cap:** 10 000 nodes by default. `(?:a{100}){100}` is refused untranslated. After translation a `.` costs about 50 nodes, so `.{1000}` (50 055), `[^a]{300}` and `(?i)[a-z]{500}` are refused; `\w{1000}` passes. The cap can be raised per process with `AppContext.SetData("REGEX_NONBACKTRACKING_MAX_AUTOMATA_SIZE", n)`. At 2 000 000 those compile, and `.{1000}` takes about 460 ms over 7 500 characters. `(?:.{1000}){1000}` stays refused.
- Because the cap depends on the engine, a pattern within the SY-61 grammar can still fail to compile in one implementation. That breaks "a pattern means the same in every implementation" unless the spec bounds pattern size.

### Casing in .NET vs CO-12

`char.ToUpperInvariant`, `StringComparison.OrdinalIgnoreCase` and `RegexOptions.IgnoreCase` (invariant culture) are **not** CO-12's simple uppercase mapping. .NET deliberately leaves out U+0131→I and U+017F→S, and `IgnoreCase` uses its own equivalence table: Kelvin = k, but ς ≠ σ. With ICU (`InvariantGlobalization=false`), `ToUpperInvariant(U+017F)` is `S` but U+0131 still isn't `I`, so the behaviour depends on the globalization mode. CO-12 has to come from a table generated from UnicodeData.txt field 12 (the spike derives one from CPython's Unicode 16.0 data; 1 477 mappings). The same table serves `equals`/`in`/`contains` with `ignore_case` and SY-30's did-you-mean, not just regex.

### Recommended design

- Own subset parser → AST → validation (RF-22 at the value's start) → emission over the byte-mapped value, compiled once per pattern with `NonBacktracking | CultureInvariant`; value mapping per match (UTF-8 encode plus byte mapping, no allocation beyond one buffer).
- A generated simple-uppercase table pinned to one Unicode version, used for every CO-12 comparison. Never `OrdinalIgnoreCase`, `ToUpperInvariant` or `IgnoreCase`.
- Raise the automata limit at startup, to a value matching whatever bound the spec sets. Treat an engine refusal as RF-22 with a message naming the size.
- Fallback to evaluate before 0.1 if the size cost hurts: a small Pike VM over code points for the same AST (linear time, exact semantics, no engine limits), about 150 lines. The byte-mapping trick was chosen here because the spike asked about `NonBacktracking`.

### Spec changes proposed (blokky-specs)

- **SY-61:** bound repetition (RE2's limit: `n`, `m` ≤ 1000) and the size of a pattern after expansion. Otherwise the 10 000-node cap (or RE2's program size limit) decides validity per engine. Suggested: counts ≤ 1000 and a pattern no larger than 1 000 characters, with nested counted repetition whose product exceeds 1000 rejected. To be settled with ADR 0018's portability goal.
- **SY-61:** state the subset's lexical edges, which the prototype had to choose:
  - a bare `]`, `}` or `{` outside a class is outside the subset (escape it);
  - `-` in a class is literal only first or last;
  - an unescaped `[` in a class, an empty class `[]`, quantifiers on `^`, `$`, `\b`, stacked quantifiers (`a{2}{3}`, `a**`) and `{,n}` are outside;
  - `\d\w\s\D\W\S` are allowed inside classes; `\b` isn't;
  - `\f`, `\v` aren't in the subset (or add them).
- **SY-61/CO-12:** say whether `(?i)` applies to `\w`/`\W` (the prototype doesn't fold the class escapes, as RE2 does, so `(?i)\w` doesn't match `ſ`), and that a class is folded before negation.
- **CO-12:** pin the Unicode version of the simple uppercase mapping (or say "the implementation's, at least N"). Add a non-normative note that platform "ignore case" APIs differ (U+0131, U+017F, U+212A, final sigma), and add `ı`/`I` and `(?i)[a-z]` vs `ſ` cases to `ignore-case-simple-uppercase`.
- **Suite:** add `\B` on a value with a multi-byte character (`aéb`), `.` on an astral character, and VT/FF in `\s`. These are the edges where a translation is most likely to drift.

## Open points

- The schema layer (unknown keys, did-you-mean, the per-field table) wasn't prototyped. `MiniSchema.cs` covers only what the five schema-layer `syntax/yaml` cases need. The SY-3 "one mistake, one error" suppression (empty key → also SY-22 here; comma pitfall → SY-20) needs a deliberate design in the validator.
- Syntax-error positions for malformed YAML outside the suite (TAB indentation, unclosed flow collections) are approximate; there's no case for them.
- The case table comes from CPython data plus a titlecase fallback for the Greek iota-subscript letters. Production must generate it from UnicodeData.txt.
- Linux x64 only; AOT on Windows and macOS wasn't run. Nothing here is OS-specific, but CI should publish on all three.
- Performance was only spot-checked: the depth cap and the 4 MiB cap weren't benchmarked.
