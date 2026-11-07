# Spikes 12 and 14: `pwsh` exit codes and the prologue scanner (Linux)

Spike plan rows 12 and 14 ([spike-plan.md](../spike-plan.md)). Rules: specs `spec/shells.md` SH-13, SH-17, SH-18, SH-20 (and SH-34 for the Windows prologue text), [specs ADR 0011](https://github.com/gergodrazsdik/blokky-specs/blob/main/adr/0011-pwsh-prologue.md).

Status: done 2026-11-07, Linux only (Fedora 44 on WSL2). Code on the unmerged branch `spikes/pwsh`, under `spikes/pwsh/`.

## Answers

1. **Spike 12.** `pwsh -NoProfile -NonInteractive -File <file>` exits **0** whenever the script ends without `exit`, whatever `$LASTEXITCODE` and `$?` are, with or without the prologue. It exits **1** after an uncaught terminating error (`throw`, a parse error, a `#requires` failure, or a cmdlet error the prologue turns terminating), and **N mod 256** after `exit N`. SH-17's claim (cmdlet error → 1 with the prologue, 0 without) and SH-18 (a failing native program doesn't stop the script) hold. SH-20 can be resolved: the implicit exit code is 0.
2. **Spike 14.** A line scanner as SH-17 describes it keeps every line number exact on pwsh 7.2, 7.3, 7.4, 7.5 and 7.6 (caught `$_.InvocationInfo.ScriptLineNumber` and the uncaught error's `file:N` both match the plan's line). The conformance cases `script/pwsh-prologue-placement`, `pwsh-prologue-stops-on-cmdlet-error`, `pwsh-native-failure-continues` (with `sh -c 'exit 3'` for `blokky-testkit exit 3`) and `pwsh-file-without-bom` give the expected output on all five versions.
3. **Scanner breakers:** code after `#>` on the line that closes a block comment (the prologue lands inside the comment and is silently lost), top-level named blocks (`begin`/`process`/`end`/`clean`/`dynamicparam`), and a `param` block whose `(` is on the next line or that follows attributes on several lines. Details and fixes below.
4. **Minimum version:** every behaviour SH-17, SH-18 and SH-20 rely on is the same from 7.2 to 7.6. The only difference is `$PSNativeCommandUseErrorActionPreference`, which has no effect before 7.4 (it was experimental in 7.3). Recommended minimum: **pwsh 7.4**.

## Setup

- Versions: 7.6.9 (installed global tool), and 7.2.24, 7.3.12, 7.4.22, 7.5.13 installed as the `PowerShell` NuGet tool package into `spikes/pwsh/tools/<v>` (`dotnet tool install --tool-path`). Only .NET 10 and 11 runtimes are installed, so 7.2–7.5 ran with `DOTNET_ROLL_FORWARD=Major` on .NET 10.0.14. The exit-code and parser behaviour tested is PowerShell's own, not the runtime's, but **CI should confirm on the official packages with their own runtimes** (see Open points).
- Invocation exactly as the SH table: `pwsh -NoProfile -NonInteractive -File <file>`, stdin `/dev/null`, stdout and stderr redirected. `-File` prints no banner, so `-NoLogo` isn't needed.
- Prologue: `$ErrorActionPreference = 'Stop';` and a space, prepended to the line SH-17 names (column "+P" below).
- Scanner: `spikes/pwsh/scanner/prologue.cs`, a .NET 11 console app published with Native AOT (no trim/AOT warnings; `GeneratedRegex`). Runners: `spikes/pwsh/run-exit.sh`, `spikes/pwsh/run-prologue.sh`.

## Spike 12: exit codes

Same result on 7.2, 7.3, 7.4, 7.5 and 7.6 in every row, except rows J and K (7.2/7.3: 0 in both columns).

| # | Script | Plain | +P | Notes |
|---|---|---|---|---|
| A | `sh -c 'exit 3'` then `Write-Output ok` | 0 | 0 | `ok` printed (SH-18) |
| B | `Write-Output start` then `sh -c 'exit 3'` (last) | 0 | 0 | SH-20 case: `$LASTEXITCODE` 3 is ignored |
| C | failing native, then `sh -c 'exit 0'` | 0 | 0 | |
| D | `Get-Item` of a missing file, then `Write-Output after` | 0 | **1** | plain prints `after`; +P stops (SH-17) |
| E | `throw 'boom'` | 1 | 1 | |
| F | `exit 7` | 7 | 7 | |
| G | failing native, then `exit $LASTEXITCODE` | 3 | 3 | the portable way to propagate |
| H | failing native, then bare `exit` | 0 | 0 | |
| I | `Write-Error 'x'`, then `Write-Output after` | 0 | **1** | |
| J | `$PSNativeCommandUseErrorActionPreference = $true`, failing native mid-script | 0 | 1 | 7.2/7.3: 0 / 0 (no effect) |
| K | same, failing native last | 0 | 1 | 7.2/7.3: 0 / 0 |
| L | `try { throw } catch { }` | 0 | 0 | |
| M | `exit 300` | 44 | 44 | mod 256 (Linux) |
| N | `exit -1` | 255 | 255 | |
| O | parse error | 1 | 1 | nothing runs |
| P | `$global:LASTEXITCODE = 5` at the end | 0 | 0 | |
| Q | failing native, then a caught cmdlet error | 0 | 0 | |
| R | `Get-Item … -ErrorAction Continue` | 0 | 0 | explicit `-ErrorAction` beats the prologue |
| S | `exit 'abc'` | 0 | 0 | non-numeric `exit` value |
| T | missing program (`no-such-program-xyz`) | 0 | **1** | CommandNotFound is a cmdlet-style error |
| — | `#requires -Version 99` | 1 | 1 | `ResourceUnavailable`, nothing runs |

Comparison with the spec: SH-17 (D, I, T) holds; SH-18 (A–C) holds, and J/K show what a script opting into `$PSNativeCommandUseErrorActionPreference` gets on 7.4+ (exit 1 with the prologue, a non-terminating error and 0 without it). SH-20's open value is 0 (B).

## Spike 14: prologue placement

The scanner walks lines (split on LF): skips blank lines, `#` line comments (including `#requires`), `<# … #>` block comments and `using namespace|module|assembly` lines (case-insensitive), and stops at the first other line. There it rejects `param(` (after any number of attributes, comments and line breaks) and named blocks, and otherwise prepends the prologue after the line's indentation.

| Case (first lines) | Scanner | Lines (caught / uncaught) on 7.2–7.6 |
|---|---|---|
| conformance placement: comment, blank, `using namespace`, statements | line 4 | 5 / 7 ✓ (exact output match) |
| `#requires -Version 7.0`, statement | line 2 | ✓ |
| multi-line `<# .SYNOPSIS … #>`, statement | line 5 | ✓ |
| one-line `<# c #>`, statement | line 2 | ✓ |
| `<#` … `#> Write-Output a` (code after `#>`) | **breaker**, see below | — |
| `@'` here-string as first statement, content looks like `#`/`using` | line 1 | ✓ |
| `$x = @"` … `param($y)` … `"@` | line 1 | ✓ |
| `param($Name)`, `Param ($Name)` | rejected | — |
| `param` ⏎ `($Name)` (valid PowerShell) | rejected | — |
| comment, `[CmdletBinding()]` ⏎ `param()` | rejected | — |
| `[SuppressMessage(…)]` ⏎ `[CmdletBinding()] <# x #> param()` | rejected | — |
| `[Console]::Out.WriteLine('a')`, `[int]$x = 1`, `[ValidateNotNull()]$x = 1` | line 1 | ✓ |
| `begin { }` / `process { }` / `end { }`, `dynamicparam { }` | **breaker**: rejected by the spike | — |
| `using namespace System.IO; [File]::Exists('/x')` (code on the using line) | line 2 | ✓, but line 1's code runs before the prologue |
| `Using Namespace …` (mixed case) | skipped as using | ✓ |
| `class Foo { … }`, `function f { }`, `trap { }` first | line 1 | ✓ (`trap … continue` swallows the final throw: exit 0, as PowerShell does) |
| indented first statement; backtick continuation | prologue after the indentation | ✓ |
| comments only, no statement | no insertion point | exit 0 |
| SH-34 Windows prologue text, run on Linux | line 4 | 5 ✓ (text only; encoding effect untested) |

What a naive insertion does (`spikes/pwsh/naive/`, 7.4 and 7.6):

| Prologue placed before | Result |
|---|---|
| `using namespace …` | `ParserError`: using must come first (why SH-17 skips `using`) |
| `param($a)` or `param` ⏎ `(…)` | `param` runs as a command: CommandNotFound |
| `[CmdletBinding()]` ⏎ `param()` | `ParserError` |
| `begin { }` … `end { }` | `begin` runs as a command: CommandNotFound |
| the start of the line `#> Write-Output a` | **no error**: the prologue is inside the comment; `$ErrorActionPreference` stays `Continue` |
| right after `#>` on that line | works, line numbers unchanged |

## Recommended design

- Keep `-File` and the plain exit-code path (ADR 0011). Don't append `exit $LASTEXITCODE`: it changes the text and SH-18's "no `-e`" parity holds already (bash without `-e` also exits with the last command's status, so this is the one real difference; document it).
- Scanner as above, token-light: whitespace, `#…`, `<#…#>`, `using` keyword lines, then a lookahead over `[…]` groups, comments and line breaks for `param` + `(`. Keywords case-insensitive. An `[…]` group followed by anything other than another `[`, or `param`, is an expression, not an attribute.
- If the first statement line closes a block comment, insert right after `#>` (not at the line start).
- Reject top-level named blocks like `param(`: plan scripts take no pipeline input, so `begin`/`process`/`end`/`clean`/`dynamicparam` have no use.
- A script with no statement gets no prologue.

## Proposed spec changes (blokky-specs)

- **SH-20:** replace the provisional text: "`pwsh -File` and `powershell -File` exit with 0 when the script ends without `exit`, whatever `$LASTEXITCODE` is; with 1 after an uncaught terminating error (including errors the prologue makes terminating); and with the `exit` value otherwise. A plan that wants a native program's status ends with `exit $LASTEXITCODE`." (Windows `powershell` 5.1 to be confirmed in the 0.6 spike; keep that half provisional.)
- **SH-17:** (a) keywords are matched case-insensitively; (b) "when that line closes a block comment, the prologue goes right after its `#>`"; (c) "`param` may be followed by whitespace and line breaks before `(`; attributes may span lines and have comments between them"; (d) top-level named blocks (`begin`, `process`, `end`, `clean`, `dynamicparam`) at that position are a plan error like `param(`; (e) a script with no such line gets no prologue; (f) code after a `using` statement on the same line runs before the prologue (note, or plan error: maintainer's call).
- **SH-18:** add that a script may opt in with `$PSNativeCommandUseErrorActionPreference = $true` (7.4+), which with the prologue ends the script with exit 1 at a failing native program.
- **SH-20 or §5 (minimum versions, IMP-15 deferred in ADR 0019):** "`pwsh` 7.4 or later." Blokky doesn't probe the version; earlier 7.x behave the same except the opt-in above.
- Conformance: new `shells/script` cases for code after `#>`, `param` ⏎ `(`, a named block (plan error), `pwsh-implicit-exit-zero` (failing native last, no `exit` → exit 0), and drop the "until SH-20 is settled" note in `pwsh-native-failure-continues`.

## Open points

- **CI matrix:** 7.4, 7.5 and 7.6 ran here on .NET 10 by roll-forward. Confirm with the official packages (each on its own runtime) on `ubuntu-24.04` and macOS, running `run-exit.sh` and `run-prologue.sh`. Lifecycle (Microsoft's dates, not re-checked here): 7.4 LTS ends 2026-11-10 and 7.5 ended in May 2026, so 7.6 LTS may become the floor soon; the maintainer decides.
- **ANSI in captured stderr:** pwsh writes error records with ANSI colour escapes even when stderr is redirected, if `TERM` looks like a colour terminal. `TERM=dumb` stops it on all versions; `NO_COLOR=1` only on 7.6. Captures and logs of pwsh leaves will contain escapes. Proposed finding: decide whether the leaf environment (§3) sets anything, or docs advise `$PSStyle.OutputRendering = 'PlainText'`. This is display hygiene, not a control.
- **For the Windows / `powershell` 5.1 spike (0.6, spike C):** the SH-20 exit codes for `powershell -File` (5.1 is known to differ for `exit` values and parse errors; untested); the scanner on 5.1 (no `clean` block, `#requires` handling, BOM per SH-12); the Windows prologue's `[Console]::OutputEncoding` effect and line numbers; `exit N` beyond 0–255 on Windows (no mod 256); `-ExecutionPolicy Bypass` under Group Policy.
