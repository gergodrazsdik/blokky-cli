// Spike 1/5/8L: launcher, lifeline, tree kill, drain. Throwaway code.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static Native;

unsafe
{
  var a = args;
  if (a.Length == 0) { Console.Error.WriteLine("usage: launcher run|__exec|__watchdog|probe-net ..."); return 2; }
  return a[0] switch
  {
    "__exec" => Trampoline.Exec(a),
    "__watchdog" => Watchdog.Run(a),
    "probe-net" => NetProbe.Run(a),
    "run" => Host.Run(a),
    _ => 2,
  };
}

static unsafe class Trampoline
{
  // __exec <expected-ppid> <prog> <args...>: own session, PDEATHSIG, then execve.
  public static int Exec(string[] a)
  {
    int expected = int.Parse(a[1]);
    setsid(); // no-op (EPERM) when posix_spawn already made us a session leader
    prctl(PR_SET_PDEATHSIG, (nuint)SIGKILL, 0, 0, 0);
    if (getppid() != expected) _exit(125); // parent died before prctl: no lifeline, give up
    var argv = Marshal.AllocHGlobal(IntPtr.Size * (a.Length - 1));
    for (int i = 2; i < a.Length; i++) ((IntPtr*)argv)[i - 2] = Marshal.StringToCoTaskMemUTF8(a[i]);
    ((IntPtr*)argv)[a.Length - 2] = IntPtr.Zero;
    execv(a[2], argv);
    return 127;
  }
}

static class Watchdog
{
  // __watchdog <marker>: stdin is the lifeline pipe. Lines: "+pgid", "bye".
  // On EOF without "bye": SIGKILL every registered group and every marked process.
  public static int Run(string[] a)
  {
    var marker = a[1];
    var groups = new List<int>();
    string? line;
    while ((line = Console.In.ReadLine()) != null)
    {
      if (line == "bye") return 0;
      if (line.StartsWith('+')) groups.Add(int.Parse(line.AsSpan(1)));
    }
    foreach (var g in groups) kill(-g, SIGKILL);
    var self = Environment.ProcessId;
    foreach (var pid in Procs.WithMarker(marker)) if (pid != self) kill(pid, SIGKILL);
    return 0;
  }
}

static class Procs
{
  public static IEnumerable<int> All()
  {
    foreach (var d in Directory.EnumerateDirectories("/proc"))
      if (int.TryParse(Path.GetFileName(d), out var pid)) yield return pid;
  }

  public static (char state, int ppid, int pgid, int sid)? Stat(int pid)
  {
    try
    {
      var s = File.ReadAllText($"/proc/{pid}/stat");
      var f = s[(s.LastIndexOf(')') + 2)..].Split(' ');
      return (f[0][0], int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]));
    }
    catch (IOException) { return null; }
    catch (UnauthorizedAccessException) { return null; }
  }

  public static IEnumerable<int> WithMarker(string marker)
  {
    var needle = Encoding.UTF8.GetBytes(marker + "\0");
    foreach (var pid in All())
    {
      byte[] env;
      try { env = File.ReadAllBytes($"/proc/{pid}/environ"); } catch { continue; }
      if (env.AsSpan().IndexOf(needle) >= 0 && Stat(pid) is { state: not 'Z' }) yield return pid;
    }
  }

  // Live descendants of `self` (a subreaper) other than `except`: orphans adopted from any leaf.
  public static IEnumerable<int> AdoptedBy(int self, int except)
  {
    var map = All().Select(p => (p, s: Stat(p))).Where(x => x.s is { state: not 'Z' }).ToList();
    var front = new HashSet<int>(map.Where(x => x.s!.Value.ppid == self && x.p != except).Select(x => x.p));
    var all = new HashSet<int>(front);
    while (front.Count > 0)
    {
      front = new HashSet<int>(map.Where(x => front.Contains(x.s!.Value.ppid)).Select(x => x.p));
      all.UnionWith(front);
    }
    return all;
  }

  // Live (non-zombie) members of a process group.
  public static List<int> Group(int pgid) =>
    All().Where(p => Stat(p) is { } s && s.pgid == pgid && s.state != 'Z').ToList();
}

static unsafe class Host
{
  static readonly long T0 = Stopwatch.GetTimestamp();
  static double Ms() => Stopwatch.GetElapsedTime(T0).TotalMilliseconds;
  static long NowNs() => (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;

  // run [--launch spawn|tramp] [--lifeline none|pdeathsig|watchdog] [--timeout ms] [--grace ms]
  //     [--drain ms] [--subreaper] [--thread main|ephemeral] [--pidfile f] script...
  public static int Run(string[] a)
  {
    string launch = "tramp", lifeline = "watchdog", thread = "main", pidfile = "";
    int timeout = 10000, grace = 1000, drain = 1000; bool subreaper = false;
    var scripts = new List<string>();
    for (int i = 1; i < a.Length; i++)
      switch (a[i])
      {
        case "--launch": launch = a[++i]; break;
        case "--lifeline": lifeline = a[++i]; break;
        case "--timeout": timeout = int.Parse(a[++i]); break;
        case "--grace": grace = int.Parse(a[++i]); break;
        case "--drain": drain = int.Parse(a[++i]); break;
        case "--subreaper": subreaper = true; break;
        case "--thread": thread = a[++i]; break;
        case "--pidfile": pidfile = a[++i]; break;
        default: scripts.Add(a[i]); break;
      }
    if (subreaper) prctl(PR_SET_CHILD_SUBREAPER, 1, 0, 0, 0);
    var runId = $"{Environment.ProcessId}x{Random.Shared.Next():x}";
    var markerVar = $"BLOKKY_RUN_{runId}";
    var marker = $"{markerVar}=1";
    if (pidfile != "") File.WriteAllText(pidfile, Environment.ProcessId + "\n");

    int wdFd = -1;
    if (lifeline == "watchdog")
    {
      int* p = stackalloc int[2];
      pipe2(p, O_CLOEXEC);
      Spawn(Environment.ProcessPath!, ["__watchdog", marker], p[0], -1, -1, setsid: true, env: null, out _);
      close(p[0]);
      wdFd = p[1];
    }

    int rc = 0;
    var env = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
      .Select(e => $"{e.Key}={e.Value}").Append(marker).ToArray();
    int* po = stackalloc int[2]; int* pe = stackalloc int[2];
    for (int li = 0; li < scripts.Count; li++)
    {
      pipe2(po, O_CLOEXEC); pipe2(pe, O_CLOEXEC);
      string prog; string[] argv;
      if (launch.StartsWith("tramp"))
      { prog = Environment.ProcessPath!; argv = ["__exec", Environment.ProcessId.ToString(), "/bin/sh", "-c", scripts[li]]; }
      else
      { prog = "/bin/sh"; argv = ["-c", scripts[li]]; }
      bool setsidFlag = launch != "tramp-late"; // tramp-late: trampoline calls setsid itself (racy)
      int pid = 0;
      int devnull = open("/dev/null", O_RDONLY | O_CLOEXEC);
      int o1 = po[1], e1 = pe[1];
      var start = Ms(); var startNs = NowNs();
      if (thread == "ephemeral")
      { var t = new Thread(() => { Spawn(prog, argv, devnull, o1, e1, setsid: setsidFlag, env, out pid); Thread.Sleep(200); }); t.Start(); t.Join(); }
      else Spawn(prog, argv, devnull, o1, e1, setsid: setsidFlag, env, out pid);
      close(devnull); close(po[1]); close(pe[1]);
      // Leader is in its own session/group: pgid == pid (POSIX_SPAWN_SETSID, atomic with the spawn).
      int pgid = pid;
      if (wdFd >= 0) { var b = Encoding.ASCII.GetBytes($"+{pgid}\n"); fixed (byte* bp = b) write(wdFd, bp, (nuint)b.Length); }

      var outBuf = new MemoryStream(); var errBuf = new MemoryStream();
      var cts = new CancellationTokenSource();
      var rOut = Reader(po[0], outBuf, cts.Token); var rErr = Reader(pe[0], errBuf, cts.Token);

      int pidfd = pidfd_open(pid, 0);
      var pfd = new PollFd { fd = pidfd, events = POLLIN };
      int left = timeout; string killedBy = "";
      int pr = poll(&pfd, 1, left);
      var exitedAt = Ms();
      if (pr == 0)
      {
        killedBy = "timeout";
        StopGroup(pgid, grace);
        poll(&pfd, 1, -1);
        exitedAt = Ms();
      }
      // 1. duration ends (direct child exited; leader kept as zombie until the tree is stopped)
      double duration = exitedAt - start;
      // 2. drain until EOF or the drain bound
      bool drained = Task.WaitAll([rOut, rErr], drain);
      bool held = !drained;
      var drainEnd = Ms();
      // 3. stop the remaining tree (group), then escapees by marker (best effort)
      var survivors = Procs.Group(pgid);
      if (survivors.Count > 0) StopGroup(pgid, grace);
      var escapees = Procs.WithMarker(marker).Where(p => p != Environment.ProcessId && Procs.Stat(p) is { } s && s.pgid != pgid).ToList();
      if (subreaper) escapees = escapees.Union(Procs.AdoptedBy(Environment.ProcessId, pid)).ToList();
      escapees.RemoveAll(IsWatchdog);
      foreach (var e in escapees) kill(e, SIGTERM);
      WaitGone(escapees, grace);
      foreach (var e in escapees) kill(e, SIGKILL);
      WaitGone(escapees, 1000);
      int status; waitpid(pid, &status, 0); close(pidfd);
      while (waitpid(-1, &status, WNOHANG) > 0) { } // reap adopted orphans (subreaper)
      cts.Cancel();
      Task.WaitAll([rOut, rErr], 500);
      close(po[0]); close(pe[0]);
      var stopEnd = Ms();
      // 4. bind captures, then the next leaf starts
      var outText = Encoding.UTF8.GetString(outBuf.ToArray()).Replace("\n", "\\n");
      Console.WriteLine($"leaf={li} pid={pid} start_ns={startNs} end_ns={NowNs()} duration_ms={duration:F0} drain_ms={drainEnd - exitedAt:F0} stop_ms={stopEnd - drainEnd:F0} held_output={held.ToString().ToLower()} group_survivors_at_stop={survivors.Count} escapees=[{string.Join(',', escapees)}] escapees_alive_after={escapees.Count(Alive)} group_alive_after={Procs.Group(pgid).Count} killed_by={killedBy} stdout=\"{outText}\"");
      if (Procs.Group(pgid).Count > 0 || escapees.Any(Alive)) rc = 1;
    }
    if (wdFd >= 0) { var b = "bye\n"u8.ToArray(); fixed (byte* bp = b) write(wdFd, bp, (nuint)b.Length); close(wdFd); }
    return rc;
  }

  static bool IsWatchdog(int pid)
  {
    try { return File.ReadAllText($"/proc/{pid}/cmdline").Contains("__watchdog"); } catch { return false; }
  }

  static bool Alive(int pid) => Procs.Stat(pid) is { state: not 'Z' };

  static void WaitGone(List<int> pids, int ms)
  {
    var end = Ms() + ms;
    while (Ms() < end && pids.Any(Alive)) { ReapAll(); Thread.Sleep(10); }
  }

  static void ReapAll() { int s; while (waitpid(-1, &s, WNOHANG | __WALL) > 0) { } }

  static void StopGroup(int pgid, int grace)
  {
    if (kill(-pgid, SIGTERM) < 0) return;
    var end = Ms() + grace;
    while (Ms() < end && Procs.Group(pgid).Count > 0) Thread.Sleep(10);
    kill(-pgid, SIGKILL);
    var end2 = Ms() + 2000;
    while (Ms() < end2 && Procs.Group(pgid).Count > 0) Thread.Sleep(5);
  }

  static Task Reader(int fd, MemoryStream sink, CancellationToken ct) => Task.Factory.StartNew(() =>
  {
    var buf = new byte[4096];
    var p = new PollFd { fd = fd, events = POLLIN };
    while (!ct.IsCancellationRequested)
    {
      if (poll(&p, 1, 50) <= 0) continue;
      nint n;
      fixed (byte* b = buf) n = read(fd, b, (nuint)buf.Length);
      if (n <= 0) return;
      lock (sink) sink.Write(buf, 0, (int)n);
    }
  }, TaskCreationOptions.LongRunning);

  public static void Spawn(string prog, string[] args, int stdin, int stdout, int stderr, bool setsid, string[]? env, out int pid)
  {
    var attr = (IntPtr)NativeMemory.AllocZeroed(1024);
    var fa = (IntPtr)NativeMemory.AllocZeroed(1024);
    posix_spawnattr_init(attr);
    posix_spawn_file_actions_init(fa);
    var all = (IntPtr)NativeMemory.AllocZeroed(256);
    var empty = (IntPtr)NativeMemory.AllocZeroed(256);
    sigfillset(all); sigemptyset(empty);
    // Reset every signal disposition (the runtime ignores SIGPIPE) and the mask.
    posix_spawnattr_setsigdefault(attr, all);
    posix_spawnattr_setsigmask(attr, empty);
    short flags = POSIX_SPAWN_SETSIGDEF | POSIX_SPAWN_SETSIGMASK;
    if (setsid) flags |= POSIX_SPAWN_SETSID;
    posix_spawnattr_setflags(attr, flags);
    if (stdin >= 0) posix_spawn_file_actions_adddup2(fa, stdin, 0);
    if (stdout >= 0) posix_spawn_file_actions_adddup2(fa, stdout, 1);
    if (stderr >= 0) posix_spawn_file_actions_adddup2(fa, stderr, 2);
    var argv = Strs([prog, .. args]);
    var envp = Strs(env ?? Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Select(e => $"{e.Key}={e.Value}").ToArray());
    int p;
    int r = posix_spawn(&p, prog, fa, attr, argv, envp);
    if (r != 0) throw new InvalidOperationException($"posix_spawn: {r}");
    pid = p;
  }

  static IntPtr Strs(string[] s)
  {
    var v = (IntPtr*)NativeMemory.AllocZeroed((nuint)((s.Length + 1) * IntPtr.Size));
    for (int i = 0; i < s.Length; i++) v[i] = Marshal.StringToCoTaskMemUTF8(s[i]);
    return (IntPtr)v;
  }
}

static class NetProbe
{
  // probe-net <detached 0|1> <killOnParentExit 0|1> <thread main|ephemeral> <script>
  public static int Run(string[] a)
  {
    var psi = new ProcessStartInfo("/bin/sh", ["-c", a[4]])
    {
      StartDetached = a[1] == "1",
      KillOnParentExit = a[2] == "1",
    };
    Process? p = null;
    try
    {
      if (a[3] == "ephemeral") { var t = new Thread(() => { p = Process.Start(psi); Thread.Sleep(200); }); t.Start(); t.Join(); Thread.Sleep(100); }
      else p = Process.Start(psi);
    }
    catch (Exception e) { Console.WriteLine($"start failed: {e.GetType().Name}: {e.Message}"); return 1; }
    var s = Procs.Stat(p!.Id);
    Console.WriteLine($"host={Environment.ProcessId} child={p.Id} pgid={s?.pgid} sid={s?.sid}");
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
    return 0;
  }
}

[StructLayout(LayoutKind.Sequential)]
struct PollFd { public int fd; public short events; public short revents; }

static unsafe partial class Native
{
  public const int SIGKILL = 9, SIGTERM = 15, PR_SET_PDEATHSIG = 1, PR_SET_CHILD_SUBREAPER = 36;
  public const int O_CLOEXEC = 0x80000, O_RDONLY = 0, WNOHANG = 1, __WALL = 0x40000000;
  public const short POLLIN = 1;
  public const short POSIX_SPAWN_SETSIGDEF = 0x04, POSIX_SPAWN_SETSIGMASK = 0x08, POSIX_SPAWN_SETSID = 0x80;

  [LibraryImport("libc", SetLastError = true)] public static partial int setsid();
  [LibraryImport("libc", SetLastError = true)] public static partial int getppid();
  [LibraryImport("libc", SetLastError = true)] public static partial int prctl(int option, nuint a2, nuint a3, nuint a4, nuint a5);
  [LibraryImport("libc", SetLastError = true)] public static partial int kill(int pid, int sig);
  [LibraryImport("libc")] public static partial void _exit(int code);
  [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)] public static partial int execv(string path, IntPtr argv);
  [LibraryImport("libc", SetLastError = true)] public static partial int pipe2(int* fds, int flags);
  [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)] public static partial int open(string path, int flags);
  [LibraryImport("libc", SetLastError = true)] public static partial int close(int fd);
  [LibraryImport("libc", SetLastError = true)] public static partial nint read(int fd, byte* buf, nuint n);
  [LibraryImport("libc", SetLastError = true)] public static partial nint write(int fd, byte* buf, nuint n);
  [LibraryImport("libc", SetLastError = true)] public static partial int poll(PollFd* fds, nuint n, int timeout);
  [LibraryImport("libc", SetLastError = true)] public static partial int pidfd_open(int pid, uint flags);
  [LibraryImport("libc", SetLastError = true)] public static partial int waitpid(int pid, int* status, int options);
  [LibraryImport("libc")] public static partial int sigfillset(IntPtr set);
  [LibraryImport("libc")] public static partial int sigemptyset(IntPtr set);
  [LibraryImport("libc")] public static partial int posix_spawnattr_init(IntPtr attr);
  [LibraryImport("libc")] public static partial int posix_spawnattr_setflags(IntPtr attr, short flags);
  [LibraryImport("libc")] public static partial int posix_spawnattr_setsigdefault(IntPtr attr, IntPtr set);
  [LibraryImport("libc")] public static partial int posix_spawnattr_setsigmask(IntPtr attr, IntPtr set);
  [LibraryImport("libc")] public static partial int posix_spawn_file_actions_init(IntPtr fa);
  [LibraryImport("libc")] public static partial int posix_spawn_file_actions_adddup2(IntPtr fa, int fd, int newfd);
  [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
  public static partial int posix_spawn(int* pid, string path, IntPtr fa, IntPtr attr, IntPtr argv, IntPtr envp);
}
