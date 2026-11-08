using Blokky.Core.Ast;
using Blokky.Core.Errors;
using Blokky.Core.Features;
using Blokky.Core.Reading;

namespace Blokky.Core.Validation;

/// <summary>What validating a plan found.</summary>
/// <param name="Read">The plan reader's result.</param>
/// <param name="Ast">The AST, unless the reader's errors ended processing.</param>
/// <param name="Features">The features the plan uses (empty without an AST).</param>
/// <param name="Errors">Every plan error, in the order found: reader, schema level, static rules, feature gate.</param>
public sealed record ValidationResult(
    PlanReadResult Read,
    PlanAst? Ast,
    IReadOnlyList<FeatureUse> Features,
    IReadOnlyList<BlokkyError> Errors)
{
    /// <summary>Whether the plan is valid and every feature it uses is supported.</summary>
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// The static front half shared by <c>validate</c> and <c>run</c> (architecture.md §2): the plan reader, the syntax tree,
/// the AST builder with the schema-level checks, the static rule families (<see cref="PlanRules"/>), then the feature
/// gate. Every error of the plan step is collected (SY-3); only the reader's fatal errors stop it early.
/// </summary>
/// <param name="supported">The feature table the command gates on.</param>
/// <param name="table">Which table that is.</param>
/// <param name="rules">The rule families; <see cref="PlanRules.All"/> by default.</param>
/// <param name="reader">The plan reader; the default one by default.</param>
/// <param name="environment">The environment the run starts with (VA-40); the process environment by default.</param>
public sealed class PlanValidator(IReadOnlySet<string> supported, FeatureTable table = FeatureTable.Validated,
    IReadOnlyList<IPlanRule>? rules = null, PlanReader? reader = null, Func<string, string?>? environment = null)
{
    private readonly IReadOnlyList<IPlanRule> _rules = rules ?? PlanRules.All;
    private readonly PlanReader _reader = reader ?? new PlanReader();

    /// <summary>Validates the plan <paramref name="bytes"/>, exactly as given.</summary>
    public ValidationResult Validate(ReadOnlySpan<byte> bytes)
    {
        var read = _reader.Read(bytes);
        if (!read.CanContinue || SyntaxTree.Build(read.Events, read.Markers) is not { } tree)
        {
            return new ValidationResult(read, null, [], read.Errors);
        }

        var errors = new PlanErrorSink();
        foreach (var error in read.Errors)
        {
            errors.Add(error);
        }

        var ast = AstBuilder.Build(tree, errors);
        var context = new PlanContext(ast, tree, read.Text!, read.Lines!, environment);
        foreach (var rule in _rules)
        {
            rule.Check(context, errors);
        }

        var features = FeatureDeriver.Derive(ast);
        FeatureGate.Check(features, supported, table, errors);
        return new ValidationResult(read, ast, features, errors.Errors);
    }
}
