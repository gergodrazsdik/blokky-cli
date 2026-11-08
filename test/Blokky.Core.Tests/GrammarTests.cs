using Blokky.Core.Ast;

namespace Blokky.Core.Tests;

public class GrammarTests
{
    [Test]
    [Arguments("0s", true, 0)]
    [Arguments("500ms", false, 500)]
    [Arguments("1h30m", false, 5_400_000)]
    [Arguments("1m30s500ms", false, 90_500)]
    [Arguments("1h0m", false, 3_600_000)]
    [Arguments("168h", false, 604_800_000)]
    public async Task Durations_parse(string text, bool zeroAllowed, long ms)
    {
        await Assert.That(Grammars.Duration(text, zeroAllowed, out var value)).IsNull();
        await Assert.That((long)value.TotalMilliseconds).IsEqualTo(ms);
    }

    [Test]
    [Arguments("30", "SY-57")]
    [Arguments("1.5s", "SY-57")]
    [Arguments("05s", "SY-57")]
    [Arguments("+5s", "SY-57")]
    [Arguments("-5s", "SY-57")]
    [Arguments("30m1h", "SY-56")]
    [Arguments("1m1m", "SY-56")]
    [Arguments("1m 30s", "SY-56")]
    [Arguments("10S", "SY-56")]
    [Arguments("s", "SY-56")]
    [Arguments("1d", "SY-56")]
    [Arguments("0s", "SY-58")]
    [Arguments("0ms", "SY-58")]
    [Arguments("168h1s", "SY-59")]
    [Arguments("10081m", "SY-59")]
    [Arguments("99999999999999999999h", "SY-59")]
    public async Task Bad_durations_name_their_rule(string text, string rule)
    {
        await Assert.That(Grammars.Duration(text, zeroAllowed: false, out _)?.Rule).IsEqualTo(rule);
    }

    [Test]
    public async Task Zero_is_written_0s_where_it_is_allowed()
    {
        await Assert.That(Grammars.Duration("0s", zeroAllowed: true, out _)).IsNull();
        await Assert.That(Grammars.Duration("0h", zeroAllowed: true, out _)?.Rule).IsEqualTo("SY-58");
        await Assert.That(Grammars.Duration("0m0s", zeroAllowed: true, out _)?.Rule).IsEqualTo("SY-58");
        await Assert.That(Grammars.Duration("30", zeroAllowed: true, out _)?.Fix).IsEqualTo("30s");
    }

    [Test]
    [Arguments("0", 0, 0L, null)]
    [Arguments("3", 1, 3L, null)]
    [Arguments("2147483647", 1, 2147483647L, null)]
    [Arguments("2147483648", 1, 0L, "SY-54")]
    [Arguments("03", 1, 0L, "SY-54")]
    [Arguments("+3", 1, 0L, "SY-54")]
    [Arguments("1e1", 1, 0L, "SY-54")]
    [Arguments("0", 1, 0L, "SY-54")]
    [Arguments("", 0, 0L, "SY-54")]
    public async Task Integers(string text, int min, long expected, string? rule)
    {
        await Assert.That(Grammars.WholeNumber(text, min, int.MaxValue, out var value)?.Rule).IsEqualTo(rule);
        await Assert.That(rule is null ? value : 0L).IsEqualTo(expected);
    }

    [Test]
    public async Task Exit_codes_reach_the_unsigned_32_bit_range()
    {
        await Assert.That(Grammars.WholeNumber("4294967295", 0, 4_294_967_295L, out _, "SY-96")).IsNull();
        await Assert.That(Grammars.WholeNumber("4294967296", 0, 4_294_967_295L, out _, "SY-96")?.Rule).IsEqualTo("SY-96");
    }

    [Test]
    [Arguments("1", false)]
    [Arguments("1.5", false)]
    [Arguments("10", false)]
    [Arguments("10.5", true)]
    [Arguments("0.5", true)]
    [Arguments("2.", true)]
    [Arguments(".5", true)]
    [Arguments("5e-1", true)]
    [Arguments("01.5", true)]
    public async Task Backoff_factors(string text, bool bad)
    {
        await Assert.That(Grammars.DecimalNumber(text, 1m, 10m, minExclusive: false, out _) is not null).IsEqualTo(bad);
    }

    [Test]
    public async Task Jitter_excludes_zero()
    {
        await Assert.That(Grammars.DecimalNumber("0", 0m, 1m, minExclusive: true, out _)?.Rule).IsEqualTo("SY-55");
        await Assert.That(Grammars.DecimalNumber("1", 0m, 1m, minExclusive: true, out _)).IsNull();
    }

    [Test]
    [Arguments("true", null, null)]
    [Arguments("false", null, null)]
    [Arguments("True", "SY-53", "true")]
    [Arguments("yes", "SY-53", "true")]
    [Arguments("on", "SY-53", "true")]
    [Arguments("1", "SY-53", "true")]
    [Arguments("No", "SY-53", "false")]
    [Arguments("maybe", "SY-53", null)]
    public async Task Booleans_with_a_fix_it(string text, string? rule, string? fix)
    {
        var error = Grammars.Boolean(text, out _);

        await Assert.That(error?.Rule).IsEqualTo(rule);
        await Assert.That(error?.Fix).IsEqualTo(fix);
    }

    [Test]
    [Arguments("A", true)]
    [Arguments("RUN_ID2", true)]
    [Arguments("run_id", false)]
    [Arguments("_ITEM", false)]
    [Arguments("2X", false)]
    [Arguments("BLOKKY_X", false)]
    [Arguments("BLOKKYX", true)]
    public async Task Variable_names(string text, bool ok)
    {
        await Assert.That(Grammars.VariableName(text) is null).IsEqualTo(ok);
    }

    [Test]
    public async Task Other_names()
    {
        await Assert.That(Grammars.EnvironmentName("my_var")).IsNull();
        await Assert.That(Grammars.EnvironmentName("my-var")?.Rule).IsEqualTo("SY-52");
        await Assert.That(Grammars.EnvironmentName("9LIVES")?.Rule).IsEqualTo("SY-52");
        await Assert.That(Grammars.UserCode("OOM")).IsNull();
        await Assert.That(Grammars.UserCode("BK_MINE")?.Rule).IsEqualTo("SY-52");
        await Assert.That(Grammars.LowerName("conf-x.y_z", "lock name")).IsNull();
        await Assert.That(Grammars.LowerName(new string('a', 65), "lock name")?.Rule).IsEqualTo("SY-52");
        await Assert.That(Grammars.LowerName("Web", "service name")?.Rule).IsEqualTo("SY-52");
        await Assert.That(Grammars.TempdirPrefix("build.1")).IsNull();
        await Assert.That(Grammars.TempdirPrefix("a/b")?.Rule).IsEqualTo("SY-88");
    }

    [Test]
    [Arguments("1.0", true)]
    [Arguments("1.10", true)]
    [Arguments("1", false)]
    [Arguments("1.0.0", false)]
    [Arguments("01.0", false)]
    [Arguments("1.00", false)]
    [Arguments("v1.0", false)]
    public async Task Spec_versions(string text, bool ok)
    {
        await Assert.That(Grammars.IsSpecVersion(text)).IsEqualTo(ok);
    }

    [Test]
    [Arguments("-1.5", true)]
    [Arguments("007", true)]
    [Arguments("+1", false)]
    [Arguments("1e3", false)]
    [Arguments(".5", false)]
    [Arguments("5.", false)]
    [Arguments(" 1", false)]
    [Arguments("NaN", false)]
    public async Task Numeric_operands(string text, bool ok)
    {
        await Assert.That(Grammars.NumericOperand(text) is null).IsEqualTo(ok);
    }

    [Test]
    [Arguments("lo", "do")]
    [Arguments("Timeout", "timeout")]
    [Arguments("timeoutt", "timeout")]
    [Arguments("register", "capture")]
    [Arguments("params", "inputs")]
    [Arguments("xyzzy", null)]
    [Arguments("ti\U0001F600\U0001F600eout", "timeout")]
    public async Task Unknown_root_keys_get_a_synonym_or_the_closest_key(string key, string? suggestion)
    {
        await Assert.That(KeyShapes.Suggest(MappingShape.Root, key).Suggestion).IsEqualTo(suggestion);
    }

    /// <summary>
    /// SY-30 compares after CO-12's simple uppercase mapping, which maps U+0131 (dotless i) to I and U+017F (long s) to S;
    /// .NET's invariant uppercasing leaves both alone, which would put this key 3 edits from <c>description</c>.
    /// </summary>
    [Test]
    public async Task Unknown_keys_compare_by_the_simple_uppercase_mapping()
    {
        const string Key = "de\u017Fcr\u0131pt\u0131on";

        await Assert.That(Key.ToUpperInvariant()).IsNotEqualTo("DESCRIPTION");
        await Assert.That(KeyShapes.Suggest(MappingShape.Input, Key).Suggestion).IsEqualTo("description");
    }

    [Test]
    public async Task Synonyms_apply_only_where_their_table_says()
    {
        await Assert.That(KeyShapes.Suggest(MappingShape.Node, "params").Suggestion).IsNull();
        await Assert.That(KeyShapes.Suggest(MappingShape.Input, "enum").Suggestion).IsEqualTo("in");
        await Assert.That(KeyShapes.Suggest(MappingShape.Node, "options").Suggestion).IsNull();
        await Assert.That(KeyShapes.Suggest(MappingShape.Node, new string('k', 100_000)).Suggestion).IsNull();
    }
}
