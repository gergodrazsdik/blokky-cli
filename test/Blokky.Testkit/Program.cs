using Blokky.Testkit;
using Blokky.Testkit.Commands;

return Testkit.Run(args);

namespace Blokky.Testkit
{
    /// <summary>Entry point: alias expansion, invocation log, common options, dispatch (testkit-interface.md §1).</summary>
    internal static class Testkit
    {
        private static readonly Dictionary<string, Func<IReadOnlyList<Arg>, int>> Commands = new(StringComparer.Ordinal)
        {
            ["exit"] = ExitCommands.Exit,
            ["print"] = PrintCommand.Run,
            ["cat"] = StreamCommands.Cat,
            ["argv"] = ArgvCommand.Run,
            ["env"] = EnvCommands.Env,
            ["getenv"] = EnvCommands.GetEnv,
            ["sleep"] = SleepCommand.Run,
            ["spawn"] = SpawnCommand.Run,
            ["alive"] = AliveCommand.Run,
            ["info"] = InfoCommand.Run,
            ["write-file"] = FileCommands.WriteFile,
            ["read-file"] = FileCommands.ReadFile,
            ["count"] = CounterCommands.Count,
            ["flaky"] = CounterCommands.Flaky,
            ["listen"] = ListenCommand.Run,
            ["die"] = ExitCommands.Die,
            ["symlink"] = FileCommands.Symlink,
            ["chmod"] = FileCommands.Chmod,
        };

        public static int Run(string[] args)
        {
            try
            {
                var name = Invocation.InvokedName();
                var argv = Invocation.CommandLineArgs(args);
                if (name != Invocation.ProgramName)
                {
                    argv = [.. Invocation.AliasArgs(name), .. argv];
                }

                // The log line comes before anything else, usage errors included (TK-5).
                Invocation.Log(name, argv);

                if (argv.Count == 0)
                {
                    throw TestkitException.Usage("missing command");
                }

                if (!Commands.TryGetValue(argv[0].Text, out var command))
                {
                    throw TestkitException.Usage($"unknown command '{argv[0].Text}'");
                }

                return command(argv.Skip(1).ToList());
            }
            catch (TestkitException e)
            {
                StdStream.Diagnostic(e.Message);
                return e.ExitCode;
            }
        }

        /// <summary>Parses a command's arguments and applies the common options (TK-7) before it acts.</summary>
        public static ParsedArgs Prepare(CommandSpec spec, IReadOnlyList<Arg> args)
        {
            var parsed = spec.Parse(args);
            ApplyCommon(parsed);
            return parsed;
        }

        /// <summary>Applies the common options (TK-7) to already parsed arguments.</summary>
        public static void ApplyCommon(ParsedArgs parsed)
        {
            if (parsed.GetText("--cwd-check") is { } expected)
            {
                CheckCwd(expected);
            }

            if (parsed.GetText("--pid-file") is { } pidFile)
            {
                Files.Write(pidFile, System.Text.Encoding.UTF8.GetBytes($"{Environment.ProcessId}\n"), append: false, "--pid-file");
            }
        }

        private static void CheckCwd(string expected)
        {
            var actual = Directory.GetCurrentDirectory();
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Resolve(actual), Resolve(Path.GetFullPath(expected)), comparison))
            {
                throw TestkitException.Usage($"--cwd-check: the working directory is '{actual}', not '{expected}'");
            }
        }

        /// <summary>The path with every symlink resolved (realpath); the path itself if it can't be resolved.</summary>
        private static string Resolve(string path)
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var resolved = Interop.Libc.RealPath(path, 0);
                if (resolved == 0)
                {
                    return path;
                }

                try
                {
                    return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(resolved) ?? path;
                }
                finally
                {
                    Interop.Libc.Free(resolved);
                }
            }

            var target = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            return Path.TrimEndingDirectorySeparator(target?.FullName ?? path);
        }
    }
}
