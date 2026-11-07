// Spikes 6 and 7 (throwaway): executable resolution and link-safe removal/creation on Linux.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static Native;

return args.Length == 0 ? Usage() : args[0] switch
{
  "which" => Which(args[1]),
  "spawn" => Spawn(args[1..], useProcess: false),
  "proc" => Spawn(args[1..], useProcess: true),
  "procbare" => ProcBare(args[1..]),
  "startabs" => StartAbs(args[1], args[2], args[3], args[4..]),
  "fullpath" => Print(Path.GetFullPath(args[1]) + " | join=" + Join(Environment.CurrentDirectory, args[1])),
  "rmtree" => RmTree(args[1]),
  "dotnetdelete" => DotnetDelete(args[1]),
  "create" => Create(args[1]),
  _ => Usage(),
};

static int Usage() { Console.Error.WriteLine("usage: which|spawn|proc|procbare|startabs|fullpath|rmtree|dotnetdelete|create ..."); return 2; }
static int Print(string s) { Console.WriteLine(s); return 0; }

// ---------- Spike 6: resolution ----------

// SH-25/SH-26. No symlink resolution and no lexical "..": the result is the path as found.
static string? Resolve(string argv0, string cwd, out string how)
{
  if (argv0.Contains('/'))
  {
    how = "path";
    var p = argv0.StartsWith('/') ? argv0 : Join(cwd, argv0);
    return IsExecFile(p) ? p : null;
  }
  how = "PATH";
  foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
  {
    if (dir.Length == 0 || !dir.StartsWith('/')) continue; // empty or relative entry = cwd-relative: skipped (SH-25)
    var p = dir.TrimEnd('/') + "/" + argv0;
    if (IsExecFile(p)) return p;
  }
  return null;
}

// Only drops "./" segments; ".." stays for the kernel to resolve physically.
static string Join(string cwd, string rel)
{
  var parts = rel.Split('/').Where(s => s.Length > 0 && s != ".");
  return cwd.TrimEnd('/') + "/" + string.Join('/', parts);
}

static unsafe bool IsExecFile(string p)
{
  byte* st = stackalloc byte[StatSize];
  // The target must be a regular executable file (the link is followed for this check only).
  return fstatat(AT_FDCWD, p, st, 0) == 0 && (Mode(st) & S_IFMT) == S_IFREG && access(p, X_OK) == 0;
}

static int Which(string name)
{
  var r = Resolve(name, Environment.CurrentDirectory, out var how);
  Console.WriteLine($"{name} -> {r ?? "(not found)"} via {how}");
  return r is null ? 1 : 0;
}

static int Spawn(string[] argv, bool useProcess)
{
  var program = Resolve(argv[0], Environment.CurrentDirectory, out var how);
  if (program is null) { Console.WriteLine($"BK_SPAWN_NOT_FOUND {argv[0]}"); return 127; }
  Console.WriteLine($"program={program} ({how})");
  return useProcess ? RunProcess(program, argv[1..]) : PosixSpawn(program, argv);
}

static int StartAbs(string mode, string abs, string argv0, string[] rest) =>
  mode == "process" ? RunProcess(abs, rest) : PosixSpawn(abs, [argv0, .. rest]);

static int ProcBare(string[] argv) => RunProcess(argv[0], argv[1..]);

static int RunProcess(string fileName, string[] rest)
{
  var psi = new ProcessStartInfo(fileName) { UseShellExecute = false };
  foreach (var a in rest) psi.ArgumentList.Add(a);
  try
  {
    using var p = Process.Start(psi)!;
    p.WaitForExit();
    Console.WriteLine($"process exit={p.ExitCode}");
    return 0;
  }
  catch (System.ComponentModel.Win32Exception e)
  {
    Console.WriteLine($"process Win32Exception errno={e.NativeErrorCode}: {e.Message}");
    return 1;
  }
}

static unsafe int PosixSpawn(string program, string[] argv)
{
  var handles = new List<IntPtr>();
  byte** av = (byte**)NativeMemory.AllocZeroed((nuint)(argv.Length + 1), (nuint)sizeof(byte*));
  for (int i = 0; i < argv.Length; i++) { var h = Marshal.StringToCoTaskMemUTF8(argv[i]); handles.Add(h); av[i] = (byte*)h; }
  byte** envp = *(byte***)NativeLibrary.GetExport(NativeLibrary.Load(Libc), "environ");
  int rc = posix_spawn(out int pid, program, 0, 0, av, envp);
  foreach (var h in handles) Marshal.FreeCoTaskMem(h);
  NativeMemory.Free(av);
  if (rc != 0) { Console.WriteLine($"posix_spawn errno={rc} ({Marshal.GetPInvokeErrorMessage(rc)})"); return 1; }
  waitpid(pid, out int status, 0);
  Console.WriteLine($"posix_spawn exit={(status >> 8) & 0xff}");
  return 0;
}

// ---------- Spike 7: removal ----------

static int DotnetDelete(string path)
{
  try { Directory.Delete(path, recursive: true); Console.WriteLine("Directory.Delete ok"); return 0; }
  catch (Exception e) { Console.WriteLine($"Directory.Delete {e.GetType().Name}: {e.Message}"); return 1; }
}

static unsafe int RmTree(string path)
{
  var full = path.TrimEnd('/');
  var parent = Path.GetDirectoryName(full)!;
  var name = Path.GetFileName(full);
  // The per-user root is trusted (created and checked by Blokky); open it without following a final link.
  int pfd = openat(AT_FDCWD, parent, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
  if (pfd < 0) { Console.WriteLine($"open root errno={Marshal.GetLastPInvokeError()}"); return 1; }
  byte* st = stackalloc byte[StatSize];
  if (fstatat(pfd, name, st, AT_SYMLINK_NOFOLLOW) != 0) { Console.WriteLine("tempdir missing"); return 1; }
  var stats = new RmStats();
  bool complete;
  try { complete = RemoveAt(pfd, name, Dev(st), stats); }
  catch (IOException e) { Console.WriteLine("rmtree error: " + e.Message); return 1; }
  finally { close(pfd); }
  Console.WriteLine($"rmtree {(complete ? "ok" : "partial")} files={stats.Files} links={stats.Links} dirs={stats.Dirs} retries={stats.Retries} skippedMounts={stats.SkippedMounts}");
  return 0;
}

// Returns false when something had to be kept (a mount point below), so the parent stays too.
static unsafe bool RemoveAt(int parentFd, string name, ulong rootDev, RmStats s)
{
  byte* st = stackalloc byte[StatSize];
  for (int attempt = 0; attempt < 1000; attempt++)
  {
    if (attempt > 0) s.Retries++;
    if (fstatat(parentFd, name, st, AT_SYMLINK_NOFOLLOW) != 0)
    {
      if (Errno() == ENOENT) return true;
      throw Fail("fstatat", name);
    }
    uint mode = Mode(st) & S_IFMT;
    if (mode != S_IFDIR)
    {
      if (unlinkat(parentFd, name, 0) == 0) { if (mode == S_IFLNK) s.Links++; else s.Files++; return true; }
      int e = Errno();
      if (e == ENOENT) return true;
      if (e == EISDIR) continue;          // swapped for a directory since fstatat
      throw Fail("unlinkat", name);
    }
    if (Dev(st) != rootDev) { s.SkippedMounts++; return false; } // never cross a file system boundary
    int fd = openat(parentFd, name, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
    if (fd < 0)
    {
      int e = Errno();
      if (e == ENOENT) return true;
      if (e is ELOOP or ENOTDIR) continue; // swapped for a link or file since fstatat: re-stat, unlink it
      throw Fail("openat", name);
    }
    bool all = true;
    try
    {
      if (fstat(fd, st) != 0) throw Fail("fstat", name);
      if (Dev(st) != rootDev) { s.SkippedMounts++; return false; }
      foreach (var child in List(fd)) all &= RemoveAt(fd, child, rootDev, s);
    }
    finally { close(fd); }
    if (!all) return false;
    if (unlinkat(parentFd, name, AT_REMOVEDIR) == 0) { s.Dirs++; return true; }
    int e2 = Errno();
    if (e2 == ENOENT) return true;
    if (e2 is ENOTDIR or ENOTEMPTY or EBUSY) continue; // swapped, or refilled concurrently: start over
    throw Fail("unlinkat(AT_REMOVEDIR)", name);
  }
  throw new IOException($"giving up on {name} after 1000 attempts");
}

static unsafe List<string> List(int fd)
{
  var names = new List<string>();
  int d = dup(fd);
  IntPtr dir = fdopendir(d);
  if (dir == 0) { close(d); throw Fail("fdopendir", "."); }
  rewinddir(dir);
  for (byte* ent; (ent = (byte*)readdir(dir)) != null;)
  {
    var n = Marshal.PtrToStringUTF8((IntPtr)(ent + 19))!; // glibc x86_64 dirent64: d_name at offset 19
    if (n is not ("." or "..")) names.Add(n);
  }
  closedir(dir);
  return names;
}

// ---------- Spike 7: creation (EM-69) ----------

static unsafe int Create(string root)
{
  int rfd = openat(AT_FDCWD, root, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
  Report("open root O_DIRECTORY|O_NOFOLLOW", rfd);
  if (rfd < 0) return 1;
  byte* st = stackalloc byte[StatSize];
  fstat(rfd, st);
  Console.WriteLine($"root uid={Uid(st)} euid={geteuid()} mode={Convert.ToString(Mode(st) & 0xfff, 8)} (must be owned by euid, no group/other write)");

  Report("mkdirat fresh 0700", mkdirat(rfd, "fresh", 0x1c0));
  Report("mkdirat over link-to-dir", mkdirat(rfd, "link-dir", 0x1c0));
  Report("mkdirat over dangling link", mkdirat(rfd, "dangling", 0x1c0));
  const int newFile = O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC;
  Report("openat new file O_CREAT|O_EXCL|O_NOFOLLOW", Closed(openat(rfd, "fresh-file", newFile, 0x180)));
  Report("openat O_CREAT|O_EXCL|O_NOFOLLOW over link-file", Closed(openat(rfd, "link-file", newFile, 0x180)));
  Report("openat O_CREAT|O_EXCL|O_NOFOLLOW over dangling", Closed(openat(rfd, "dangling", newFile, 0x180)));
  Report("openat O_CREAT|O_NOFOLLOW (no EXCL) over dangling", Closed(openat(rfd, "dangling", O_WRONLY | O_CREAT | O_NOFOLLOW | O_CLOEXEC, 0x180)));
  Report("openat O_CREAT (no EXCL, no NOFOLLOW) over dangling2 [unsafe]", Closed(openat(rfd, "dangling2", O_WRONLY | O_CREAT | O_CLOEXEC, 0x180)));
  close(rfd);

  Try("File.Open CreateNew over dangling3", () => File.Open(Path.Combine(root, "dangling3"), FileMode.CreateNew).Dispose());
  Try("File.Open Create over dangling4 [unsafe]", () => File.Open(Path.Combine(root, "dangling4"), FileMode.Create).Dispose());
  Try("Directory.CreateDirectory over link-dir", () => Directory.CreateDirectory(Path.Combine(root, "link-dir", "made-by-dotnet")));
  Try("Directory.CreateTempSubdirectory", () => Console.WriteLine("  -> " + Directory.CreateTempSubdirectory("bk-").FullName));
  return 0;
}

static int Closed(int fd) { if (fd >= 0) close(fd); return fd; }
static void Report(string what, int rc) =>
  Console.WriteLine(rc >= 0 ? $"{what}: ok" : $"{what}: errno={Errno()} ({Marshal.GetPInvokeErrorMessage(Errno())})");
static void Try(string what, Action a)
{
  try { a(); Console.WriteLine($"{what}: ok"); }
  catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); }
}

static int Errno() => Marshal.GetLastPInvokeError();
static IOException Fail(string op, string name) => new($"{op}({name}) errno={Errno()} ({Marshal.GetPInvokeErrorMessage(Errno())})");
static unsafe ulong Dev(byte* st) => *(ulong*)st;
static unsafe uint Mode(byte* st) => *(uint*)(st + 24);
static unsafe uint Uid(byte* st) => *(uint*)(st + 28);

sealed class RmStats { public int Files, Links, Dirs, Retries, SkippedMounts; }

static unsafe partial class Native
{
  public const string Libc = "libc.so.6";
  public const int AT_FDCWD = -100, AT_SYMLINK_NOFOLLOW = 0x100, AT_REMOVEDIR = 0x200, X_OK = 1;
  public const int O_RDONLY = 0, O_WRONLY = 1, O_CREAT = 0x40, O_EXCL = 0x80,
    O_DIRECTORY = 0x10000, O_NOFOLLOW = 0x20000, O_CLOEXEC = 0x80000;
  public const int ENOENT = 2, EBUSY = 16, EEXIST = 17, ENOTDIR = 20, EISDIR = 21, ENOTEMPTY = 39, ELOOP = 40;
  public const uint S_IFMT = 0xF000, S_IFDIR = 0x4000, S_IFLNK = 0xA000, S_IFREG = 0x8000;
  public const int StatSize = 144; // struct stat, glibc x86_64

  [LibraryImport(Libc, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
  public static partial int openat(int dirfd, string path, int flags, uint mode = 0);
  [LibraryImport(Libc, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
  public static partial int unlinkat(int dirfd, string path, int flags);
  [LibraryImport(Libc, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
  public static partial int fstatat(int dirfd, string path, byte* buf, int flags);
  [LibraryImport(Libc, SetLastError = true)]
  public static partial int fstat(int fd, byte* buf);
  [LibraryImport(Libc, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
  public static partial int mkdirat(int dirfd, string path, uint mode);
  [LibraryImport(Libc, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
  public static partial int access(string path, int mode);
  [LibraryImport(Libc, SetLastError = true)] public static partial int close(int fd);
  [LibraryImport(Libc, SetLastError = true)] public static partial int dup(int fd);
  [LibraryImport(Libc, SetLastError = true)] public static partial IntPtr fdopendir(int fd);
  [LibraryImport(Libc)] public static partial void rewinddir(IntPtr dir);
  [LibraryImport(Libc, SetLastError = true)] public static partial IntPtr readdir(IntPtr dir);
  [LibraryImport(Libc, SetLastError = true)] public static partial int closedir(IntPtr dir);
  [LibraryImport(Libc)] public static partial uint geteuid();
  [LibraryImport(Libc, StringMarshalling = StringMarshalling.Utf8)]
  public static partial int posix_spawn(out int pid, string path, IntPtr fileActions, IntPtr attr, byte** argv, byte** envp);
  [LibraryImport(Libc, SetLastError = true)]
  public static partial int waitpid(int pid, out int status, int options);
}
