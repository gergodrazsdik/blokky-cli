using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>The columns of the validity matrix (reference.md §7) and the sibling table (§5.2), as data.</summary>
internal static class KindTables
{
    /// <summary>The repetition modifiers, in the order RF-2 names them.</summary>
    public static readonly string[] Repetitions = ["retry", "while", "until", "for"];

    /// <summary>The scope modifiers (the matrix's scope column).</summary>
    public static readonly string[] Scope = ["cwd", "env", "export", "tempdir", "lock", "container", "services"];

    /// <summary>The leaf options other than captures (§3).</summary>
    public static readonly string[] LeafOptions = ["stdin", "ok_exit", "stderr", "encoding", "keep_descendants"];

    /// <summary>The capture keys (RF-12).</summary>
    public static readonly string[] Captures = ["capture", "capture_file", "secret"];

    /// <summary>Whether <paramref name="kind"/> starts no process (RF-28): no timeout, finally or ignore_errors.</summary>
    public static bool NoProcess(NodeKind kind) =>
        kind is NodeKind.Sleep or NodeKind.Log or NodeKind.Fail or NodeKind.Set or NodeKind.Return;

    /// <summary>The matrix's scope and retry/while/until columns: ✓ for these kinds.</summary>
    public static bool TakesScope(NodeKind kind) =>
        kind is NodeKind.Exec or NodeKind.Script or NodeKind.Do or NodeKind.All or NodeKind.Any or NodeKind.Race or NodeKind.If
            or NodeKind.Assert;

    /// <summary>The matrix's <c>for</c> column.</summary>
    public static bool TakesFor(NodeKind kind) => TakesScope(kind) || kind == NodeKind.Log;

    /// <summary>The matrix's leaf-options column.</summary>
    public static bool IsLeaf(NodeKind kind) => kind is NodeKind.Exec or NodeKind.Script;

    /// <summary>The kinds a sibling field (§5.2) is valid with, or null if <paramref name="key"/> isn't one.</summary>
    public static string[]? SiblingOf(string key) => key switch
    {
        "max" or "every" or "index" => ["retry", "while", "until"],
        "backoff" or "jitter" => ["retry"],
        "on_body_failure" or "carry" => ["while", "until"],
        "collect" => ["for", "while", "until"],
        _ => null,
    };

    /// <summary>The kinds a companion field is valid on (RF-32), or null if <paramref name="key"/> isn't one.</summary>
    public static NodeKind[]? CompanionOf(string key) => key switch
    {
        "then" or "else" => [NodeKind.If],
        "shell" => [NodeKind.Script],
        "level" => [NodeKind.Log],
        "message" => [NodeKind.Assert],
        "code" => [NodeKind.Fail, NodeKind.Assert],
        _ => null,
    };

    /// <summary>The kind key's name (<see cref="NodeKind"/> follows <see cref="KeyShapes.Kinds"/>' order).</summary>
    public static string Name(NodeKind kind) => KeyShapes.Kinds[(int)kind];
}
