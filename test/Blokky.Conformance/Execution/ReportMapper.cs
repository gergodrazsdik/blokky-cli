using System.Globalization;
using Blokky.Conformance.Matching;

namespace Blokky.Conformance.Execution;

/// <summary>
/// Maps blokky's run report (docs/contracts/output-contract.md §3) to the result document (conformance/README.md §3.6,
/// CF-2). It only renames and reshapes: a field the report doesn't record stays absent, never inferred.
/// Provisional: the run engine (T-0070) writes the first real reports, and differences between the report and the
/// result document found then are report gaps, fixed in the contract (validation-strategy.md §2).
/// </summary>
internal static class ReportMapper
{
    /// <summary>Node fields whose report and result-document names are the same.</summary>
    private static readonly string[] NodeKeys =
    [
        "id", "kind", "name", "status", "ignored", "skip_reason", "program", "exit_code", "signal", "killed_by",
        "descendants_held_output", "survivors", "left_running", "user", "container", "services",
    ];

    private static readonly string[] EntryKeys = ["index", "name", "item", "status"];

    /// <summary>The result document, with <c>nodes</c> flattened to a mapping keyed by path id (CF-26).</summary>
    public static OrderedDictionary<string, object?> Map(OrderedDictionary<string, object?> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var doc = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        Copy(report, "run_id", doc, "id");
        if (report.TryGetValue("status", out var status))
        {
            doc["status"] = status switch
            {
                "interrupted" or "budget_exceeded" => "stopped",
                _ => status,
            };
        }

        Copy(report, "stop_cause", doc, "stop_cause");
        if (report.TryGetValue("error", out var error))
        {
            doc["error"] = Error(error);
        }

        if (report.TryGetValue("outputs", out var outputs) && outputs is OrderedDictionary<string, object?> outputMap)
        {
            var mapped = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, value) in outputMap)
            {
                mapped[name] = value is OrderedDictionary<string, object?> m && m.TryGetValue("secret", out var s) && s is true
                    ? SecretValue.Value
                    : value;
            }

            doc["outputs"] = mapped;
        }

        if (report.TryGetValue("inputs", out var inputs) && inputs is OrderedDictionary<string, object?> inputMap)
        {
            var mapped = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, value) in inputMap)
            {
                if (value is OrderedDictionary<string, object?> entry)
                {
                    var copy = new OrderedDictionary<string, object?>(entry, StringComparer.Ordinal);
                    if (copy.TryGetValue("source", out var source) && source is "cli" or "mcp")
                    {
                        copy["source"] = "supplied";
                    }

                    mapped[name] = copy;
                }
            }

            doc["inputs"] = mapped;
        }

        CopyDuration(report, "duration_ms", doc, "duration");
        var nodes = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        if (report.TryGetValue("nodes", out var roots) && roots is List<object?> list)
        {
            foreach (var root in list.OfType<OrderedDictionary<string, object?>>())
            {
                Flatten(root, nodes);
            }
        }

        doc["nodes"] = nodes;
        return doc;
    }

    /// <summary>An error object without <c>message</c>, <c>pos</c> and <c>fix</c>; <c>pos</c> becomes <c>line</c>/<c>column</c>.</summary>
    public static object? Error(object? error)
    {
        if (error is not OrderedDictionary<string, object?> e)
        {
            return error;
        }

        var mapped = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in e)
        {
            switch (key)
            {
                case "message" or "fix":
                    break;
                case "pos" when value is string pos:
                    var parts = pos.Split(':');
                    if (parts.Length == 2 && long.TryParse(parts[0], CultureInfo.InvariantCulture, out var line)
                        && long.TryParse(parts[1], CultureInfo.InvariantCulture, out var column))
                    {
                        mapped["line"] = line;
                        mapped["column"] = column;
                    }

                    break;
                case "causes" when value is List<object?> causes:
                    mapped["causes"] = causes.Select(Error).ToList();
                    break;
                default:
                    mapped[key] = value;
                    break;
            }
        }

        return mapped;
    }

    private static void Flatten(OrderedDictionary<string, object?> node, OrderedDictionary<string, object?> nodes)
    {
        var mapped = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in NodeKeys)
        {
            Copy(node, key, mapped, key);
        }

        if (node.TryGetValue("error", out var error))
        {
            mapped["error"] = Error(error);
        }

        Copy(node, "argv", mapped, "invocation");
        Copy(node, "captures", mapped, "bindings");
        CopyDuration(node, "duration_ms", mapped, "duration");
        CopyDuration(node, "lock_wait_ms", mapped, "lock_wait");
        if (node.TryGetValue("logs", out var logs) && logs is List<object?> logList)
        {
            mapped["messages"] = logList.OfType<OrderedDictionary<string, object?>>()
                .Select(l => (object?)new OrderedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["text"] = l.GetValueOrDefault("message"),
                    ["level"] = l.GetValueOrDefault("level"),
                }).ToList();
        }

        if (node.TryGetValue("probes", out var probes) && probes is List<object?> probeList)
        {
            mapped["probes"] = probeList.OfType<OrderedDictionary<string, object?>>()
                .Select(p => (object?)new OrderedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["invocation"] = p.GetValueOrDefault("command"),
                    ["exit_code"] = p.GetValueOrDefault("exit_code"),
                    ["result"] = p.GetValueOrDefault("result"),
                }).ToList();
        }

        if ((node.GetValueOrDefault("attempts") ?? node.GetValueOrDefault("iterations")) is OrderedDictionary<string, object?> rep)
        {
            var repetition = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
            Copy(rep, "count", repetition, "count");
            Copy(rep, "ended_by", repetition, "ended_by");
            Copy(rep, "last_condition", repetition, "last_condition");
            if (rep.TryGetValue("items", out var items) && items is List<object?> itemList)
            {
                repetition["entries"] = itemList.OfType<OrderedDictionary<string, object?>>().Select(i =>
                {
                    var entry = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var key in EntryKeys)
                    {
                        Copy(i, key, entry, key);
                    }

                    if (i.TryGetValue("error", out var e))
                    {
                        entry["error"] = Error(e);
                    }

                    return (object?)entry;
                }).ToList();
            }

            mapped["repetition"] = repetition;
        }

        if (node.TryGetValue("children", out var children) && children is List<object?> childList)
        {
            var kids = childList.OfType<OrderedDictionary<string, object?>>().ToList();
            mapped["children"] = kids.Select(k => k.GetValueOrDefault("id")).ToList();
            foreach (var kid in kids)
            {
                Flatten(kid, nodes);
            }
        }

        if (node.TryGetValue("finally", out var fin) && fin is OrderedDictionary<string, object?> finMap)
        {
            Copy(finMap, "status", mapped, "finally_status");
            if (finMap.TryGetValue("nodes", out var finNodes) && finNodes is List<object?> finList)
            {
                foreach (var kid in finList.OfType<OrderedDictionary<string, object?>>())
                {
                    Flatten(kid, nodes);
                }
            }
        }

        if (mapped.TryGetValue("id", out var id) && id is string path)
        {
            nodes[path] = mapped;
        }
    }

    private static void Copy(OrderedDictionary<string, object?> from, string key, OrderedDictionary<string, object?> to, string asKey)
    {
        if (from.TryGetValue(key, out var value))
        {
            to[asKey] = value;
        }
    }

    private static void CopyDuration(OrderedDictionary<string, object?> from, string key, OrderedDictionary<string, object?> to,
        string asKey)
    {
        if (from.TryGetValue(key, out var value) && value is long ms)
        {
            to[asKey] = TimeSpan.FromMilliseconds(ms);
        }
    }
}
