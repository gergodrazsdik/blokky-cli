using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;

namespace Blokky.Conformance.Reporting;

/// <summary>Totals and the pass rate (CF-63).</summary>
internal sealed record Totals(int Cases, int Pass, int Fail, int Error, int SkipNotSupported, int SkipRequirement, int SkipPlatform)
{
    /// <summary>
    /// The later-feature validation pass (ADR 0009): validation cases whose features aren't all validated, and the valid plans
    /// of not-supported run cases.
    /// </summary>
    public LaterTotals Later { get; init; } = new(0, 0, 0, 0);

    public int Skip => SkipNotSupported + SkipRequirement + SkipPlatform;

    /// <summary>Cases that count for the pass rate: every case but requirement and platform skips (CF-63).</summary>
    public int Counted => Cases - SkipRequirement - SkipPlatform;

    public double PassRate => Counted == 0 ? 0 : 100.0 * Pass / Counted;

    public static Totals Of(IReadOnlyCollection<CaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        int Count(Func<CaseResult, bool> p) => results.Count(p);
        return new Totals(
            results.Count,
            Count(r => r.Outcome == Outcome.Pass),
            Count(r => r.Outcome == Outcome.Fail),
            Count(r => r.Outcome == Outcome.Error),
            Count(r => r.Outcome == Outcome.Skip && r.SkipKind == SkipKind.NotSupported),
            Count(r => r.Outcome == Outcome.Skip && r.SkipKind == SkipKind.Requirement),
            Count(r => r.Outcome == Outcome.Skip && r.SkipKind == SkipKind.Platform))
        {
            Later = new LaterTotals(
                Count(r => r.Later?.Kind == LaterKind.Pass),
                Count(r => r.Later?.Kind == LaterKind.Pending),
                Count(r => r.Later?.Kind == LaterKind.Fail),
                Count(r => r.Later?.Kind == LaterKind.NotApplicable)),
        };
    }

    public string Line() => string.Create(CultureInfo.InvariantCulture,
        $"cases: {Cases}  pass: {Pass}  fail: {Fail}  skip: {Skip} "
        + $"(not supported: {SkipNotSupported}, requirement: {SkipRequirement}, platform: {SkipPlatform})  error: {Error}");

    public string RateLine() => string.Create(CultureInfo.InvariantCulture,
        $"pass rate: {PassRate:0.00}% ({Pass} of {Counted} cases not skipped for a requirement or platform)");

    public string LaterLine() => string.Create(CultureInfo.InvariantCulture,
        $"later-feature validation: {Later.Pass} of {Later.Judged} pass, {Later.Pending} pending a rule family, {Later.Fail} fail "
        + $"({Later.NotApplicable} not applicable: they expect input errors)");
}

/// <summary>Totals of the later-feature validation pass.</summary>
internal sealed record LaterTotals(int Pass, int Pending, int Fail, int NotApplicable)
{
    /// <summary>Cases the pass judged: all but the not-applicable ones.</summary>
    public int Judged => Pass + Pending + Fail;
}

/// <summary>Writes the machine-readable results: JSON lines and JUnit XML (CF-63, validation-strategy.md §2).</summary>
internal static class Report
{
    public static string OutcomeName(Outcome outcome) => outcome switch
    {
        Outcome.Pass => "pass",
        Outcome.Fail => "fail",
        Outcome.Skip => "skip",
        _ => "error",
    };

    public static string LaterName(LaterKind kind) => kind switch
    {
        LaterKind.Pass => "pass",
        LaterKind.Pending => "pending",
        LaterKind.Fail => "fail",
        _ => "n/a",
    };

    public static void WriteJsonLines(string path, IEnumerable<CaseResult> results, Totals totals)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(totals);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        foreach (var r in results)
        {
            WriteLine(stream, w =>
            {
                w.WriteString("id", r.Id);
                w.WriteString("outcome", OutcomeName(r.Outcome));
                Optional(w, "reason", r.Reason);
                w.WriteStartArray("rules");
                foreach (var rule in r.Rules)
                {
                    w.WriteStringValue(rule);
                }

                w.WriteEndArray();
                Optional(w, "clock", r.Clock);
                Optional(w, "note", r.Note);
                if (r.Later is { } later)
                {
                    w.WriteString("later", LaterName(later.Kind));
                    Optional(w, "later_reason", later.Reason);
                }

                w.WriteNumber("elapsed_ms", (long)r.Elapsed.TotalMilliseconds);
            });
        }

        WriteLine(stream, w =>
        {
            w.WriteStartObject("totals");
            w.WriteNumber("cases", totals.Cases);
            w.WriteNumber("pass", totals.Pass);
            w.WriteNumber("fail", totals.Fail);
            w.WriteNumber("error", totals.Error);
            w.WriteNumber("skip_not_supported", totals.SkipNotSupported);
            w.WriteNumber("skip_requirement", totals.SkipRequirement);
            w.WriteNumber("skip_platform", totals.SkipPlatform);
            w.WriteNumber("pass_rate", Math.Round(totals.PassRate, 2));
            w.WriteNumber("later_pass", totals.Later.Pass);
            w.WriteNumber("later_pending", totals.Later.Pending);
            w.WriteNumber("later_fail", totals.Later.Fail);
            w.WriteNumber("later_not_applicable", totals.Later.NotApplicable);
            w.WriteEndObject();
        });
    }

    public static void WriteJUnit(string path, IReadOnlyCollection<CaseResult> results, Totals totals)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(totals);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using var xml = XmlWriter.Create(path, settings);
        xml.WriteStartElement("testsuites");
        xml.WriteStartElement("testsuite");
        xml.WriteAttributeString("name", "blokky-conformance");
        xml.WriteAttributeString("tests", Invariant(totals.Cases));
        var laterOnly = results.Count(r => r.Outcome != Outcome.Fail && r.Later?.Kind == LaterKind.Fail);
        xml.WriteAttributeString("failures", Invariant(totals.Fail + laterOnly));
        xml.WriteAttributeString("errors", Invariant(totals.Error));
        xml.WriteAttributeString("skipped", Invariant(totals.Skip));
        foreach (var r in results)
        {
            xml.WriteStartElement("testcase");
            xml.WriteAttributeString("classname", r.Id.Split('/')[0]);
            xml.WriteAttributeString("name", r.Id);
            xml.WriteAttributeString("time", (r.Elapsed.TotalMilliseconds / 1000).ToString("0.000", CultureInfo.InvariantCulture));
            var element = r.Later?.Kind == LaterKind.Fail ? "failure" : r.Outcome switch
            {
                Outcome.Fail => "failure",
                Outcome.Error => "error",
                Outcome.Skip => "skipped",
                _ => null,
            };
            if (element is not null)
            {
                xml.WriteStartElement(element);
                var message = r.Later?.Kind == LaterKind.Fail ? "later-feature validation: " + r.Later.Reason : r.Reason;
                xml.WriteAttributeString("message", Sanitize(message ?? ""));
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>Markdown for the CI job summary.</summary>
    public static string Summary(Totals totals, string specs, IEnumerable<CaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(results);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"### Conformance ({specs})").AppendLine()
            .AppendLine("| Cases | Pass | Fail | Error | Skip: not supported | Skip: requirement | Skip: platform | Pass rate |")
            .AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|")
            .AppendLine(CultureInfo.InvariantCulture,
                $"| {totals.Cases} | {totals.Pass} | {totals.Fail} | {totals.Error} | {totals.SkipNotSupported} "
                + $"| {totals.SkipRequirement} | {totals.SkipPlatform} | {totals.PassRate:0.00}% |")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"{totals.LaterLine()}.");
        var bad = results.Where(r => r.Outcome is Outcome.Fail or Outcome.Error || r.Later?.Kind == LaterKind.Fail).Take(50).ToList();
        if (bad.Count > 0)
        {
            sb.AppendLine().AppendLine("| Case | Outcome | Reason |").AppendLine("|---|---|---|");
            foreach (var r in bad)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| `{r.Id}` | {OutcomeName(r.Outcome)}{(r.Later?.Kind == LaterKind.Fail ? " (later: fail)" : "")} | "
                    + $"{(r.Later?.Kind == LaterKind.Fail ? r.Later.Reason : r.Reason)?.Replace("|", "\\|", StringComparison.Ordinal)} |");
            }
        }

        return sb.ToString();
    }

    private static void WriteLine(Stream stream, Action<Utf8JsonWriter> body)
    {
        using (var w = new Utf8JsonWriter(stream,
            new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
    }

    private static void Optional(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null)
        {
            w.WriteString(name, value);
        }
    }

    private static string Invariant(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Sanitize(string text) => new([.. text.Where(XmlConvert.IsXmlChar)]);
}
