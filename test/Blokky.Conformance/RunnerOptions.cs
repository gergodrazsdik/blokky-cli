using System.Globalization;
using System.Runtime.InteropServices;

namespace Blokky.Conformance;

/// <summary>Command-line options of <c>blokky-conformance</c>.</summary>
internal sealed record RunnerOptions
{
    public const string Usage =
        """
        usage: blokky-conformance [options]

            --specs DIR           blokky-specs checkout (default: $BLOKKY_SPECS or ../blokky-specs)
            --blokky PATH         the blokky binary (default: $BLOKKY_BIN or the AOT publish path under artifacts/)
            --testkit PATH        blokky-testkit (default: $BLOKKY_TESTKIT or the AOT publish path under artifacts/)
            --lock FILE           conformance.lock to compare the checkout with (default: conformance.lock if present)
            --require-lock-match  fail (exit 2) if the checkout isn't the locked commit
            --require-testkit     fail (exit 2) if blokky-testkit isn't built
            --filter PREFIX       run only cases whose id starts with PREFIX (repeatable)
            --results FILE        JSON lines (default: artifacts/conformance/results.jsonl)
            --junit FILE          JUnit XML (default: artifacts/conformance/junit.xml)
            --parallel N          cases at a time (default: processor count)
            --timing-factor F     multiplies $max bounds and the harness deadline (default 1, CF-43)
            --scale K             the scaled clock mode's factor (default 10; at most 20, CF-40)
            --container-engine    a container engine and the suite's images are available (CF-66)
            --run-root DIR        where per-case directories go (default: artifacts/conformance/runs)
            --keep                keep each case's directories
            --verbose             print every case, not only fails and errors
        """;

    public string Specs { get; init; } = Environment.GetEnvironmentVariable("BLOKKY_SPECS") ?? "../blokky-specs";

    public string Blokky { get; init; } = Environment.GetEnvironmentVariable("BLOKKY_BIN") ?? PublishPath("Blokky.Cli", "blokky");

    public string Testkit { get; init; } = Environment.GetEnvironmentVariable("BLOKKY_TESTKIT") ?? PublishPath("Blokky.Testkit",
        "blokky-testkit");

    public string? Lock { get; init; } = File.Exists("conformance.lock") ? "conformance.lock" : null;

    public bool RequireLockMatch { get; init; }

    public bool RequireTestkit { get; init; }

    public IReadOnlyList<string> Filters { get; init; } = [];

    public string Results { get; init; } = Path.Combine("artifacts", "conformance", "results.jsonl");

    public string JUnit { get; init; } = Path.Combine("artifacts", "conformance", "junit.xml");

    public int Parallel { get; init; } = Environment.ProcessorCount;

    public double TimingFactor { get; init; } = 1.0;

    public double Scale { get; init; } = OperatingSystem.IsWindows() ? 1.0 : 10.0;

    public bool ContainerEngine { get; init; }

    public string RunRoot { get; init; } = Path.Combine("artifacts", "conformance", "runs");

    public bool Keep { get; init; }

    public bool Verbose { get; init; }

    public bool Help { get; init; }

    /// <summary>The <c>UseArtifactsOutput</c> publish path for this machine's RID (tech-stack.md §8).</summary>
    internal static string PublishPath(string project, string name)
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        return Path.Combine("artifacts", "publish", project, $"release_{os}-{arch}", name + exe);
    }

    /// <summary>Parses argv; one spelling per option, the value in the next argument.</summary>
    public static (RunnerOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new RunnerOptions();
        var filters = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new FormatException($"{arg} needs a value");
            try
            {
                o = arg switch
                {
                    "--specs" => o with { Specs = Value() },
                    "--blokky" => o with { Blokky = Value() },
                    "--testkit" => o with { Testkit = Value() },
                    "--lock" => o with { Lock = Value() },
                    "--require-lock-match" => o with { RequireLockMatch = true },
                    "--require-testkit" => o with { RequireTestkit = true },
                    "--filter" => Add(o, filters, Value()),
                    "--results" => o with { Results = Value() },
                    "--junit" => o with { JUnit = Value() },
                    "--parallel" => o with { Parallel = Math.Max(1, int.Parse(Value(), CultureInfo.InvariantCulture)) },
                    "--timing-factor" => o with { TimingFactor = Positive(double.Parse(Value(), CultureInfo.InvariantCulture)) },
                    "--scale" => o with { Scale = Math.Clamp(double.Parse(Value(), CultureInfo.InvariantCulture), 1, 20) },
                    "--container-engine" => o with { ContainerEngine = true },
                    "--run-root" => o with { RunRoot = Value() },
                    "--keep" => o with { Keep = true },
                    "--verbose" => o with { Verbose = true },
                    "--help" or "-h" => o with { Help = true },
                    _ => throw new FormatException($"unknown option '{arg}'"),
                };
            }
            catch (FormatException ex)
            {
                return (null, ex.Message);
            }
        }

        return (o with { Filters = filters }, null);
    }

    private static RunnerOptions Add(RunnerOptions o, List<string> filters, string filter)
    {
        filters.Add(filter);
        return o;
    }

    private static double Positive(double value) => value > 0 ? value : throw new FormatException("--timing-factor is positive");
}
