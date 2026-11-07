# Spike report: executable paths and link-safe deletion (spikes 6 and 7)

Spike plan rows 6 and 7 ([spike-plan.md](../spike-plan.md)). Linux only (Fedora 44 on WSL2, kernel 6.18, glibc, .NET 11 RC3, Native AOT `linux-x64`). Code on the unmerged branch `spikes/paths-delete`, directory `spikes/paths-delete/`: `Program.cs` (one AOT console app, all native calls through `LibraryImport` against `libc.so.6`), `spike6.sh`, `spike7.sh`, and two C helpers (`c/showme.c` prints argv[0] and `/proc/self/exe` and picks an "applet" by basename; `c/swapper.c` swaps two names with `renameat2(RENAME_EXCHANGE)` in a tight loop). The AOT publish has no trim or AOT warnings (`TreatWarningsAsErrors`); the binary links only libc, libm and the loader.

## Answers

**Spike 6.** Yes on Linux: once argv[0] is resolved to an absolute path, starting it does no further search, with either `posix_spawn` (not `posix_spawnp`) or .NET `Process` with an absolute `FileName`. Neither resolves symbolic links: the kernel `execve`s the path as given, so `program` stays the PATH entry and a shebang script sees that path as its script argument. The difference is argv[0]: `posix_spawn` passes whatever we give (argv[0] as written, like bash), while .NET `Process` always passes `FileName`, the absolute path. Name-dependent multi-call binaries work either way, because the basename is the same. SH-29's "provisional" can be dropped.

**Spike 7.** Yes: `openat(O_DIRECTORY|O_NOFOLLOW)` recursion with `fstatat(AT_SYMLINK_NOFOLLOW)` and `unlinkat` removes a tempdir full of links to outside files and directories without touching the targets, under AOT, including 500 runs with a concurrent `RENAME_EXCHANGE` attacker (0 outside damage). .NET's `Directory.Delete(path, true)` doesn't follow links in a static tree, but it is path-based: under the same attack it deleted files **outside** the tree in 79 of 100 runs. It must not be used for run-owned files. On the creation side, `mkdirat` and `openat(O_CREAT|O_EXCL|O_NOFOLLOW)` refuse every existing name, links included; `Directory.CreateDirectory` and `FileMode.Create` write through links.

## Evidence: spike 6

`spike6.sh` sets `PATH="$T/bin1::.:$T/bin2:/usr/bin:/bin"` (with an empty and a `.` entry), runs from a cwd that holds a decoy `mytool`, and puts symlinks `bin1/hello` and `bin1/mytool` to `scripts/showme`, a symlinked shebang script `bin1/myscript -> scripts/real.sh`, and a second decoy in `bin2`. Output, paths shortened to `$T`:

| Case | Result |
|---|---|
| resolve `mytool` (resolver skips empty and relative entries) | `$T/bin1/mytool via PATH`; neither decoy |
| `posix_spawn($T/bin1/hello, ["hello"])` | `argv0=hello exe=$T/scripts/showme applet=hello-applet` |
| `posix_spawn($T/bin1/mytool, ["mytool","a","b"])` | `argv0=mytool exe=$T/scripts/showme argc=3` |
| `posix_spawn` of symlinked shebang script | `script 0=$T/bin1/myscript` (the PATH entry, not `real.sh`) |
| `Process` with `FileName=$T/bin1/hello` | `argv0=$T/bin1/hello exe=$T/scripts/showme applet=hello-applet` |
| `Process` with symlinked script | `script 0=$T/bin1/myscript` |
| `Process` with bare `FileName="mytool"` | `DECOY cwd $T/cwd/mytool`: .NET's own search tries the cwd before `PATH` |
| bash `mytool; hello x` (comparison) | `argv0=mytool`, `argv0=hello`: bash passes argv[0] as typed |
| absolute path that doesn't exist, `bin2/mytool` exists | `posix_spawn errno=2`; `Process` `Win32Exception errno=2`; no fallback search |
| executable without shebang | `posix_spawn errno=8 (Exec format error)`; `Process` `Win32Exception errno=8`; neither falls back to `/bin/sh` |
| cwd `$T/w`, argv[0] `ld/../tool`, `ld -> $T/other/sub` | `Path.GetFullPath` gives `$T/w/tool`; the kernel runs `$T/other/tool` (prints `OTHER`) |

`/proc/self/exe` always shows the symlink target (the kernel resolves it for that link); argv[0] is never rewritten. No `execve` was called directly: forking from managed code isn't sound, and `posix_spawn` is `execve` in the child (no search is defined for it). `strace` isn't installed, so "no search" is shown by outcome (decoys never run, missing path gives ENOENT) rather than by syscall trace.

## Evidence: spike 7

`spike7.sh` builds a tree (mode-0700 root) with 150 files in nested dirs, `link-file -> outside/file`, `link-dir -> outside/dir` (51 files and a subdir), a deep link to `outside/dir`, a dangling link, and `d/lnk -> outside/dir` next to the real dir `d/sub`. It fingerprints `outside/` before and after every run. In the TOCTOU runs, `swapper` exchanges `d/sub` and `d/lnk` continuously while the removal runs.

| Run | Outside damaged | Tree left over | Notes |
|---|---|---|---|
| static, safe remover | no | no | `files=150 links=5 dirs=8` |
| static, `Directory.Delete(true)` | no | no | links removed as links |
| 5 × 100 TOCTOU, safe remover | **0 / 500** | 0 | last batch: races hit in 83 runs, 554 retries in total |
| 100 TOCTOU, `Directory.Delete(true)` | **79 / 100** | 79 | e.g. `Could not find a part of the path '.../d/lnk/deep'` after it had already deleted entries in `outside/dir` |
| tmpfs mounted below the tree (`unshare -rm`, outside the Claude sandbox) | no | mount point and its ancestors kept | `rmtree partial ... skippedMounts=1`; the file on the mount survives |

The safe remover (`RmTree`/`RemoveAt` in `Program.cs`):

1. Open the trusted per-user root with `openat(AT_FDCWD, root, O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC)`; everything below is relative to directory fds.
2. Per entry: `fstatat(parent, name, AT_SYMLINK_NOFOLLOW)`. Not a directory: `unlinkat(parent, name, 0)` (a link goes, its target stays). `EISDIR` means it was swapped for a dir: retry.
3. Directory on another `st_dev`: keep it and its ancestors, count it. Otherwise `openat(parent, name, O_DIRECTORY|O_NOFOLLOW)`. A link swapped in gives `ENOTDIR` (not `ELOOP` with `O_DIRECTORY` on Linux) or `ELOOP`: retry, which now unlinks the link. `fstat` on the opened fd re-checks `st_dev`.
4. List with `fdopendir(dup(fd))` + `rewinddir` + `readdir` (names collected first), recurse with the fd as parent, close, `unlinkat(parent, name, AT_REMOVEDIR)`. `ENOTDIR`/`ENOTEMPTY`/`EBUSY` (swapped or refilled concurrently): retry, bounded (1000 here).

Creation (`create`, in a 0700 dir holding links): `mkdirat` over a link to a dir or a dangling link: `EEXIST`. `openat(O_CREAT|O_EXCL|O_NOFOLLOW)` over a link or dangling link: `EEXIST`; without `O_EXCL`: `ELOOP`; with neither, the dangling link's target is **created** outside. `File.Open(FileMode.CreateNew)` is safe (`IOException`, it uses `O_EXCL`); `FileMode.Create` created the outside target; `Directory.CreateDirectory(root/link-dir/x)` created `x` inside `outside/dir`. Opening a root that is itself a symlink with `O_DIRECTORY|O_NOFOLLOW` fails (`ENOTDIR`). The root's owner and mode come from `fstat` on that fd (`uid=1000 euid=1000 mode=700`).

## How .NET `Process.Start` behaves (Linux)

- `FileName` rooted: used as is, no search, no `realpath`, no executable pre-check that changes the outcome (errors come from `execve`: ENOENT, EACCES, ENOEXEC as `Win32Exception.NativeErrorCode`).
- `FileName` bare: .NET searches itself and the cwd wins over `PATH` (observed). That violates SH-25, so Blokky must never hand `Process` a bare name.
- argv[0] is always `FileName`; there is no API to set it separately. With `UseShellExecute=false`, no shell is involved.

## Recommended design

- **Resolver** (`exec` argv[0], shells, `command:` probes): bare name → walk Blokky's own `PATH`, skipping empty and relative entries; join `dir + "/" + name` without canonicalising; accept the first candidate that `stat` (following links) shows as a regular file and `access(X_OK)` allows. A name with `/` → join to the node's cwd, dropping only `.` segments, never collapsing `..` lexically (`Path.GetFullPath` would run a different file than the kernel). The result is `program`.
- **Launcher:** `posix_spawn(program, argv)` with argv[0] as written for `exec` (the shell's convention), and the resolved shell path as argv[0] for `script` (as results.md's `invocation` already says). This fits spike 1, whose launcher is `posix_spawn` anyway. If spike 1 lands on .NET `Process`, argv[0] becomes `program`, which still keeps multi-call basenames; the spec should then say argv[0] *ends with* the name as found.
- **ENOEXEC** maps to `BK_SPAWN_FAILED` `bad_format`; Blokky never retries through `/bin/sh`.
- **Run-owned files:** one small native module (`openat`, `mkdirat`, `unlinkat`, `fstatat`, `fstat`, `fdopendir`/`readdir`) for both creation and removal; never `Directory.Delete`, `Directory.CreateDirectory`, `File.Create`/`FileMode.Create` or `CreateTempSubdirectory` (path-based `mkdtemp` under `/tmp`) on these paths. Create names with `mkdirat(…, 0700)` / `openat(O_CREAT|O_EXCL|O_NOFOLLOW, 0600)` relative to the root fd, with a random name and retry on `EEXIST`. Check the root once per run with `fstat`: owned by the euid, no group/other write.
- **Mount points** found during removal are left with their ancestors; the removal reports "partial" (see open points for how).

## Spec and contract changes

- **SH-29:** drop "*provisional — verify in spike*". Add: "The process's argv[0] is argv[0] as written for `exec`, and the resolved shell path for `script`; `program` is the path the process is started under" (matches results.md `invocation`).
- **SH-25:** add: "Empty and relative `PATH` entries are skipped" (both name the cwd, which SH-25 excludes). CO-45 inherits it.
- **SH-26:** add: "The relative path is joined to the cwd as written; `..` is resolved by the OS, not lexically." `program` records the joined path.
- **errors.md `BK_SPAWN_FAILED`:** state that a file the OS can't execute (no shebang) is `bad_format`, with no fallback to a shell.
- **EM-69:** no rule change needed; the mechanism is confirmed. Optionally note "an entry swapped during removal is re-examined, never followed". Decide what a removal that stops at a mount point reports (open point 1).
- **CLI docs:** the implementation note above (no path-based .NET file APIs for run-owned files) goes into the CLI design docs, not the spec.

## Open points

1. **Partial removal:** EM-69 says removal never crosses a file system boundary but not what the node reports when a mount point (or a file it can't remove, or the retry bound) stops it. Proposal: a run-result warning with the kept path, not a node failure. Needs a decision (new tracker question).
2. **Retry bound:** a hostile process can keep swapping forever; the bound (1000 per entry here) and its error belong in the CLI design. It is an ergonomic bound, not a control.
3. **Moved-out directories:** the run's own processes (same user) can `rename` a directory out of the tree while the remover holds its fd; the remover then empties that directory where it now is. Those are the run's own files, and the same user could delete anything anyway (baseline "same as plain bash"), so this is accepted, but EM-69's "never removes anything outside the file's own tree" should be read as "never reaches outside through a link or mount".
4. **Portability:** struct `stat` offsets, `dirent` layout (`d_name` at 19) and flag values here are glibc x86_64. linux-arm64 has a different `struct stat`; macOS has different flag values (`O_NOFOLLOW` 0x100, `O_DIRECTORY` 0x100000, `AT_*`), `struct stat`, `dirent`, and no `/proc`. Per-RID constants and struct readers are needed; macOS results only from CI.
5. **Not verified here:** CI `ubuntu-24.04` (scripts are ready); a syscall trace (`strace` not installed); `posix_spawn` behaviour on musl.
6. **Windows (0.6 spike):**
   - Resolution: `CreateProcessW` with an absolute `lpApplicationName` does no search, but argv[0] is whatever the command line string starts with; .NET `Process` builds it from `FileName`. Check `.exe` symlinks and App Execution Aliases (`IO_REPARSE_TAG_APPEXECLINK` in `WindowsApps`), which aren't regular files.
   - Removal: reparse points (symlinks, junctions, mount points, cloud placeholders) must be opened with `FILE_FLAG_OPEN_REPARSE_POINT` and deleted as themselves. The `openat` equivalent is `NtCreateFile` with `RootDirectory` set to the parent handle; delete by handle with `SetFileInformationByHandle(FileDispositionInfoEx, FILE_DISPOSITION_FLAG_POSIX_SEMANTICS)`; a junction swap is the TOCTOU to test. Volume boundary: compare `VolumeSerialNumber` from `GetFileInformationByHandleEx`. Check what `Directory.Delete(true)` does with junctions on Windows (expected: deletes the junction without recursing, but path-based like on Linux).
   - Creation: `CREATE_NEW` with `FILE_FLAG_OPEN_REPARSE_POINT`, relative to the root handle, and an owner-only DACL instead of mode 0700.
