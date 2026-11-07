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
            Count(r => r.Outcome == Outcome.Skip && r.SkipKind == SkipKind.Platform));
    }

    public string Line() => string.Create(CultureInfo.InvariantCulture,
        $"cases: {Cases}  pass: {Pass}  fail: {Fail}  skip: {Skip} "
        + $"(not supported: {SkipNotSupported}, requirement: {SkipRequirement}, platform: {SkipPlatform})  error: {Error}");

    public string RateLine() => string.Create(CultureInfo.InvariantCulture,
        $"pass rate: {PassRate:0.00}% ({Pass} of {Counted} cases not skipped for a requirement or platform)");
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
        xml.WriteAttributeString("failures", Invariant(totals.Fail));
        xml.WriteAttributeString("errors", Invariant(totals.Error));
        xml.WriteAttributeString("skipped", Invariant(totals.Skip));
        foreach (var r in results)
        {
            xml.WriteStartElement("testcase");
            xml.WriteAttributeString("classname", r.Id.Split('/')[0]);
            xml.WriteAttributeString("name", r.Id);
            xml.WriteAttributeString("time", (r.Elapsed.TotalMilliseconds / 1000).ToString("0.000", CultureInfo.InvariantCulture));
            var element = r.Outcome switch
            {
                Outcome.Fail => "failure",
                Outcome.Error => "error",
                Outcome.Skip => "skipped",
                _ => null,
            };
            if (element is not null)
            {
                xml.WriteStartElement(element);
                xml.WriteAttributeString("message", Sanitize(r.Reason ?? ""));
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
                + $"| {totals.SkipRequirement} | {totals.SkipPlatform} | {totals.PassRate:0.00}% |");
        var bad = results.Where(r => r.Outcome is Outcome.Fail or Outcome.Error).Take(50).ToList();
        if (bad.Count > 0)
        {
            sb.AppendLine().AppendLine("| Case | Outcome | Reason |").AppendLine("|---|---|---|");
            foreach (var r in bad)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| `{r.Id}` | {OutcomeName(r.Outcome)} | {(r.Reason ?? "").Replace("|", "\\|", StringComparison.Ordinal)} |");
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
