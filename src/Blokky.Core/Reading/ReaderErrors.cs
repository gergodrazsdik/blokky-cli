using System.Text.Json.Nodes;
using Blokky.Core.Errors;

namespace Blokky.Core.Reading;

/// <summary>Builds the reader's plan errors: a code, a position and <c>detail.rule</c> (errors.md §4).</summary>
internal static class ReaderErrors
{
    /// <summary>A plan error; <paramref name="extraKey"/> adds one more <c>detail</c> field (<c>offset</c>, <c>codepoint</c>).</summary>
    public static BlokkyError Create(string code, string? rule, PlanPosition position, string message, string? extraKey = null,
        JsonNode? extraValue = null)
    {
        JsonObject? detail = null;
        if (rule is not null || extraKey is not null)
        {
            detail = [];
            if (rule is not null)
            {
                detail["rule"] = rule;
            }

            if (extraKey is not null)
            {
                detail[extraKey] = extraValue;
            }
        }

        return new BlokkyError { Code = code, Message = message, Pos = position.ToString(), Detail = detail };
    }
}
