using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Blokky.Conformance.Yaml;

/// <summary>A malformed case file: invalid YAML, a duplicate key or an unsupported construct (CF-62 <c>error</c>).</summary>
internal sealed class CaseFormatException(string message) : Exception(message);

/// <summary>
/// Loads a YAML 1.2 document into plain values (CF-8): <see cref="string"/>, <see cref="long"/>, <see cref="double"/>,
/// <see cref="bool"/>, <c>null</c>, <see cref="List{T}"/> of values and <see cref="OrderedDictionary{TKey,TValue}"/>
/// with string keys. Plain scalars are typed by the core schema; quoted and block scalars are strings.
/// Duplicate keys, aliases and non-scalar keys are rejected.
/// </summary>
internal static partial class YamlValues
{
    public static object? Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            var parser = new Parser(new StringReader(text));
            parser.Consume<StreamStart>();
            if (parser.TryConsume<StreamEnd>(out _))
            {
                return null;
            }

            parser.Consume<DocumentStart>();
            var value = ReadNode(parser);
            parser.Consume<DocumentEnd>();
            if (!parser.TryConsume<StreamEnd>(out _))
            {
                throw new CaseFormatException("a case file holds one YAML document");
            }

            return value;
        }
        catch (YamlException ex)
        {
            throw new CaseFormatException($"invalid YAML at {ex.Start.Line}:{ex.Start.Column}: {ex.Message}");
        }
    }

    private static object? ReadNode(IParser parser)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return Resolve(scalar);
        }

        if (parser.TryConsume<SequenceStart>(out _))
        {
            var list = new List<object?>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                list.Add(ReadNode(parser));
            }

            return list;
        }

        if (parser.TryConsume<MappingStart>(out _))
        {
            var map = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                if (!parser.TryConsume<Scalar>(out var key))
                {
                    throw new CaseFormatException("only scalar mapping keys are supported");
                }

                if (!map.TryAdd(key.Value, null))
                {
                    throw new CaseFormatException($"duplicate key '{key.Value}' at {key.Start.Line}:{key.Start.Column}");
                }

                map[key.Value] = ReadNode(parser);
            }

            return map;
        }

        if (parser.Accept<AnchorAlias>(out var alias))
        {
            throw new CaseFormatException($"aliases aren't supported in case files ({alias.Start.Line}:{alias.Start.Column})");
        }

        throw new CaseFormatException("unexpected YAML event");
    }

    /// <summary>YAML 1.2 core schema resolution for plain scalars.</summary>
    internal static object? Resolve(Scalar scalar)
    {
        var value = scalar.Value;
        if (scalar.Style != ScalarStyle.Plain || !scalar.Tag.IsEmpty)
        {
            return value;
        }

        switch (value)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return true;
            case "false" or "False" or "FALSE":
                return false;
        }

        if (DecimalInt().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i))
        {
            return i;
        }

        if (value.StartsWith("0o", StringComparison.Ordinal) && value.Length > 2 && value[2..].All(c => c is >= '0' and <= '7'))
        {
            return Convert.ToInt64(value[2..], 8);
        }

        if (value.StartsWith("0x", StringComparison.Ordinal) && value.Length > 2 && value[2..].All(char.IsAsciiHexDigit))
        {
            return long.Parse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        if (Float().IsMatch(value))
        {
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return value;
    }

    /// <summary>A compact, YAML-like rendering for mismatch messages.</summary>
    public static string Show(object? value)
    {
        var sb = new StringBuilder();
        Show(sb, value);
        return sb.ToString();
    }

    private static void Show(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                sb.Append('\'').Append(s.Replace("'", "''", StringComparison.Ordinal)).Append('\'');
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case TimeSpan d:
                sb.Append(Matching.Durations.Format(d));
                break;
            case IFormattable f:
                sb.Append(f.ToString(null, CultureInfo.InvariantCulture));
                break;
            case OrderedDictionary<string, object?> map:
                sb.Append("{ ");
                var first = true;
                foreach (var (k, v) in map)
                {
                    sb.Append(first ? "" : ", ").Append(k).Append(": ");
                    Show(sb, v);
                    first = false;
                }

                sb.Append(" }");
                break;
            case List<object?> list:
                sb.Append('[');
                for (var n = 0; n < list.Count; n++)
                {
                    sb.Append(n == 0 ? "" : ", ");
                    Show(sb, list[n]);
                }

                sb.Append(']');
                break;
            default:
                sb.Append(value);
                break;
        }
    }

    [GeneratedRegex("^[-+]?[0-9]+$")]
    private static partial Regex DecimalInt();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex Float();
}
