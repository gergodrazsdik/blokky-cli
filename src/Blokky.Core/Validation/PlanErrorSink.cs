using System.Text.Json.Nodes;
using Blokky.Core.Errors;
using Blokky.Core.Reading;

namespace Blokky.Core.Validation;

/// <summary>
/// Collects plan errors (SY-3: all of them, in one pass). Each carries a code, the path id of its node (<c>node</c>), its
/// position (<c>pos</c>, SY-1) and <c>detail.rule</c> (errors.md §4); unknown keys carry no rule.
/// </summary>
public sealed class PlanErrorSink
{
    private readonly List<BlokkyError> _errors = [];

    /// <summary>The errors so far, in the order reported.</summary>
    public IReadOnlyList<BlokkyError> Errors => _errors;

    /// <summary>The number of errors so far.</summary>
    public int Count => _errors.Count;

    /// <summary>Adds an error built elsewhere (the reader's).</summary>
    public void Add(BlokkyError error) => _errors.Add(error);

    /// <summary><c>BK_PLAN_VALUE</c>: one value decides alone (its grammar or type, an empty value or list).</summary>
    public void Value(string rule, PlanPosition at, string node, string message, string? fix = null) =>
        Report(ErrorCodes.PlanValue, rule, at, node, message, fix);

    /// <summary><c>BK_PLAN_INVALID</c>: a language key in the wrong place, or a rule relating keys, values or scopes.</summary>
    public void Invalid(string rule, PlanPosition at, string node, string message, string? fix = null) =>
        Report(ErrorCodes.PlanInvalid, rule, at, node, message, fix);

    /// <summary><c>BK_PLAN_UNKNOWN_KEY</c>, with <c>detail.suggestion</c> when there is one (SY-30); no rule (SY-48).</summary>
    public void UnknownKey(PlanPosition at, string node, string message, string? suggestion, string? fix = null) =>
        _errors.Add(new BlokkyError
        {
            Code = ErrorCodes.PlanUnknownKey,
            Message = message,
            Node = node,
            Pos = at.ToString(),
            Fix = fix,
            Detail = suggestion is null ? null : new JsonObject { ["suggestion"] = suggestion },
        });

    /// <summary><c>BK_PLAN_VERSION</c>, always with <c>detail.supported</c>.</summary>
    public void Version(string rule, PlanPosition at, string node, string message, IReadOnlyList<string> supported, string? fix = null)
    {
        ArgumentNullException.ThrowIfNull(supported);
        var list = new JsonArray();
        foreach (var version in supported)
        {
            list.Add((JsonNode?)JsonValue.Create(version));
        }

        _errors.Add(new BlokkyError
        {
            Code = ErrorCodes.PlanVersion,
            Message = message,
            Node = node,
            Pos = at.ToString(),
            Fix = fix,
            Detail = new JsonObject { ["rule"] = rule, ["supported"] = list },
        });
    }

    /// <summary><c>BK_PLAN_NOT_SUPPORTED</c> with <c>detail.feature</c> (SY-31, SY-94).</summary>
    public void NotSupported(string feature, PlanPosition at, string node, string verb) =>
        _errors.Add(new BlokkyError
        {
            Code = ErrorCodes.PlanNotSupported,
            Message = $"this build of blokky can't {verb} plans that use '{feature}' yet: nothing was run",
            Node = node,
            Pos = at.ToString(),
            Detail = new JsonObject { ["feature"] = feature },
        });

    private void Report(string code, string rule, PlanPosition at, string node, string message, string? fix) =>
        _errors.Add(new BlokkyError
        {
            Code = code,
            Message = message,
            Node = node,
            Pos = at.ToString(),
            Fix = fix,
            Detail = new JsonObject { ["rule"] = rule },
        });
}
