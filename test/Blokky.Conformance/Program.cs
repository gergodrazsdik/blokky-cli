using System.Collections.Concurrent;
using Blokky.Conformance;
using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Reporting;
using Blokky.Core.Features;

var (options, usageError) = RunnerOptions.Parse(args);
if (options is null || options.Help)
{
    Console.Error.WriteLine(usageError is null ? RunnerOptions.Usage : $"blokky-conformance: {usageError}\n\n{RunnerOptions.Usage}");
    return options is null ? 2 : 0;
}

var casesDir = Path.GetFullPath(Path.Combine(options.Specs, "conformance", "cases"));
if (!Directory.Exists(casesDir))
{
    Console.Error.WriteLine($"blokky-conformance: no cases at {casesDir} (check out blokky-specs, or pass --specs)");
    return 2;
}

if (!File.Exists(options.Blokky))
{
    Console.Error.WriteLine($"blokky-conformance: no blokky binary at {options.Blokky} (run just publish, or pass --blokky)");
    return 2;
}

var specsDir = Path.GetFullPath(options.Specs);
var head = LockFile.HeadOf(specsDir);
var lockInfo = "no conformance.lock";
if (options.Lock is not null)
{
    var pin = LockFile.Parse(File.ReadAllText(options.Lock));
    lockInfo = $"lock {pin.Tag} {pin.Commit}";
    if (pin.Commit is not null && !string.Equals(head, pin.Commit, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"blokky-conformance: warning: {specsDir} is at {head ?? "(unknown commit)"}, not the locked {pin.Tag} ({pin.Commit})");
        if (options.RequireLockMatch)
        {
            return 2;
        }
    }
}

var host = HostInfo.Detect(options.Testkit, options.ContainerEngine);
if (host.Testkit is null)
{
    Console.Error.WriteLine(
        $"blokky-conformance: warning: no blokky-testkit at {options.Testkit}; cases that run are skipped (requirement: testkit)");
    if (options.RequireTestkit)
    {
        return 2;
    }
}

var files = CaseLoader.FindCaseFiles(casesDir)
    .Where(f => options.Filters.Count == 0 || options.Filters.Any(p => CaseLoader.IdOf(casesDir, f).StartsWith(p,
        StringComparison.Ordinal)))
    .ToList();
if (files.Count == 0)
{
    // A --filter typo or an empty checkout would otherwise report 0 fail and 0 error, which reads as a pass.
    var filters = options.Filters.Count == 0 ? "" : $" matching --filter {string.Join(", ", options.Filters)}";
    Console.Error.WriteLine($"blokky-conformance: no cases{filters} under {casesDir}");
    return 2;
}

// Under artifacts/, on the same file system as the published testkit, so case bin/ dirs get hard links (testkit.md §4).
var runRoot = Path.GetFullPath(Path.Combine(options.RunRoot, $"run-{Guid.NewGuid():N}"));
Directory.CreateDirectory(runRoot);
var adapter = new BlokkyAdapter(Path.GetFullPath(options.Blokky), new ProcessRunner());
var runner = new CaseRunner(adapter, host, new RunnerSettings(runRoot, options.TimingFactor, options.Scale, options.Keep));

Console.WriteLine($"blokky conformance: {specsDir} at {head ?? "(unknown commit)"} ({lockInfo})");
Console.WriteLine($"binary: {adapter.Binary}; testkit: {host.Testkit ?? "not built"}; shells: {string.Join(", ", host.Shells.Keys)}");
var capabilities = adapter.Capabilities;
var clockModes = string.Join(", ", capabilities.ClockModes);
Console.WriteLine($"declared support: {capabilities.Validated.Count} validated and {capabilities.Runnable.Count} runnable of "
    + $"{FeatureIds.All.Count} features; clock modes: {clockModes}");

// CF-14, CF-59: cases that list the same port never run at the same time.
var portLocks = new ConcurrentDictionary<int, SemaphoreSlim>();
var results = new ConcurrentDictionary<string, CaseResult>(StringComparer.Ordinal);
await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = options.Parallel }, async (file, ct) =>
{
    var ports = PortsOf(file);
    var held = ports.Select(p => portLocks.GetOrAdd(p, _ => new SemaphoreSlim(1, 1))).ToList();
    foreach (var gate in held)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
    }

    try
    {
        var result = await runner.RunAsync(casesDir, file, ct).ConfigureAwait(false);
        results[result.Id] = result;
        if (options.Verbose || result.Outcome is Outcome.Fail or Outcome.Error)
        {
            Console.WriteLine($"{Report.OutcomeName(result.Outcome),-5} {result.Id}{(result.Reason is null ? "" : ": " + result.Reason)}");
        }

        if (result.Later is { } later && (later.Kind == LaterKind.Fail || (options.Verbose && later.Kind != LaterKind.Pass)))
        {
            Console.WriteLine($"later {Report.LaterName(later.Kind),-7} {result.Id}: {later.Reason}");
        }
    }
    finally
    {
        foreach (var gate in held)
        {
            gate.Release();
        }
    }
}).ConfigureAwait(false);

if (!options.Keep)
{
    try
    {
        Directory.Delete(runRoot, recursive: true);
    }
    catch (IOException)
    {
        // Best effort.
    }
}

var ordered = results.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
var totals = Totals.Of(ordered);
Report.WriteJsonLines(options.Results, ordered, totals);
Report.WriteJUnit(options.JUnit, ordered, totals);
if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
{
    File.AppendAllText(summary, Report.Summary(totals, head ?? specsDir, ordered));
}

var notes = ordered.Count(r => r.Note is not null);
Console.WriteLine(totals.Line());
Console.WriteLine(totals.RateLine());
Console.WriteLine(totals.LaterLine());

// The pending list only shrinks: an entry whose cases all pass must be deleted (meaningful for a full run only).
var stale = options.Filters.Count == 0 ? LaterFeaturePass.Stale(ordered.Select(r => r.Later).OfType<LaterResult>()) : [];
foreach (var entry in stale)
{
    Console.WriteLine(
        $"stale pending entry {entry.Key} ({entry.Task}): every case it applies to passes; delete it from PendingRuleFamilies");
}
if (notes > 0)
{
    Console.WriteLine($"notes: {notes} case(s) carry a note (see {options.Results})");
}

Console.WriteLine($"results: {options.Results}, {options.JUnit}");
return totals.Fail + totals.Error + totals.Later.Fail + stale.Count > 0 ? 1 : 0;

// Ports come from requires.ports; the loader reads them again per case, so this is a cheap pre-scan of the raw file.
static IReadOnlyList<int> PortsOf(string file)
{
    try
    {
        var c = Blokky.Conformance.Cases.CaseLoader.Load(Path.GetDirectoryName(file)!, file, HostInfo.CurrentOs);
        return [.. c.Requires.Ports.Order()];
    }
    catch (Blokky.Conformance.Yaml.CaseFormatException)
    {
        return [];
    }
}
