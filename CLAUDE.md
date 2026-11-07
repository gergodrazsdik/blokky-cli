@AGENTS.md

## Claude Code

Additions for Claude Code only; everything shared is in AGENTS.md (imported above, loaded once whichever "Project instructions" setting you use).

- **Start sessions with `just claude`** from blokky-cli. It runs Claude Code with `--add-dir ../blokky-specs` and `CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1`, so the specs repo's instructions load too. Settings in the added directory's `.claude/` don't apply; this repo's do.
- **The added repo's `AGENTS.md`:** Claude Code loads `../blokky-specs/CLAUDE.md`, but may not expand its `@AGENTS.md` import (it counts as an external import that needs approval). If `../blokky-specs/AGENTS.md` isn't in your context, read it with the Read tool before changing files in blokky-specs. The same applies to this file when blokky-cli is the added directory.
- **Subagents and worktrees** (brief §0.2): when work can be parallelized, give each subagent its own git worktree and branch, explicit file ownership, and its own PR. Shared files belong to the main agent; subagents propose changes to them in their PR description. Code work fans out per [ADR 0007](docs/adr/0007-runtime-plan.md): one `feat/<topic>` branch per subagent.
- **Sandbox:** `.claude/settings.json` enables the Bash sandbox with a small network allowlist. Commit signing, `gh` and network beyond the allowlist may need to run outside the sandbox as separate calls (for example `git commit -s -F <file>` on its own, not chained); follow your user-level setup for that.
- **Check what loads:** `claude -p --permission-mode plan "list the instruction files in your context"` from the repo root.
