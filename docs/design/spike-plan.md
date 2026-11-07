# Spike plan

Every item marked "Provisional — verify in spike", with acceptance criteria.

Status: not started (T-0027). The input below is collected for it.

## Input from the council review of draft.3

The [council report](../review/council-draft.3.md) §4 mapped the proposed spikes onto the brief's spike list. As decided in §6 and applied in `1.0.0-draft.4` (specs ADRs 0017–0019):

| Topic | Spec rules | Plan |
|---|---|---|
| Cancelled group children in joins: confirmation waits for `finally` | EM-31, EM-32, EM-58 | new spike |
| Leaf completion order: drain, stop the tree, captures, next sibling | EM-18, EM-20, EM-21, EM-68 | merge into brief spike 8 |
| Tree kill and lifeline per OS; `keep_descendants: detach` | EM-15, EM-21, EM-22 | merge into brief spikes 1 and 5 |
| Windows graceful stop: hidden console per leaf, `graceful: none` | EM-15, EM-26, EM-30 | merge into brief spike 2 |
| Containers with compatible images (`sh`, `sleep`, `kill`): idle process, in-container kill | CP-4, CP-8, CP-36 | merge into brief spikes 11 and 13 (no helper) |
| Engine matrix: Docker and Podman, SELinux without relabelling, rootless, desktop VMs; invoking-user mapping for non-root build images; `exec --env-file` | CP-9 to CP-11, CP-16, CP-17, CP-37, CP-38 | new spike |
| Portable regex subset translated to .NET NonBacktracking under AOT | SY-61, SY-62, CO-13, RF-22 | merge into brief spike 9 |
| Windows output code pages: UTF-8 per leaf | SH-34, VA-20, VA-21, SH-12, SH-17 | new spike |
| PowerShell prologue scanner, execution policy, minimum shell versions | SH-13, SH-17, SH-20 | new spike |
| Conformance runner: `kill` action, `after_run` checks, per-OS timing | CF-22, CF-40, CF-43, CF-64, EM-60 | new spike |

Dropped: the `become` transport and sudoers spike (brief spike 11's privilege half), because `become` is cut (specs ADR 0017).
