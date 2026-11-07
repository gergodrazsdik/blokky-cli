using Blokky.Conformance.Matching;
using Blokky.Conformance.Yaml;

namespace Blokky.Conformance.Tests;

public class MatcherTests
{
    private static readonly Matcher Matcher = new();

    private static string? Match(string expected, string actual) => Matcher.Match(Values.Yaml(expected), Values.Yaml(actual), "");

    [Test]
    public async Task Mappings_match_as_subsets()
    {
        await Assert.That(Match("{ code: BK_EXIT }", "{ code: BK_EXIT, node: /do/0 }")).IsNull();
        var mismatch = Match("{ code: BK_EXIT, node: /do/1 }", "{ code: BK_EXIT, node: /do/0 }");

        await Assert.That(mismatch).IsEqualTo("node: expected '/do/1', got '/do/0'");
    }

    [Test]
    public async Task Lists_match_exactly_and_in_order()
    {
        await Assert.That(Match("[a, b]", "[a, b]")).IsNull();
        await Assert.That(Match("[a, b]", "[b, a]")).IsEqualTo("[0]: expected 'a', got 'b'");
        await Assert.That(Match("[a]", "[a, b]")).IsNotNull();
    }

    [Test]
    public async Task An_integer_matches_its_decimal_string()
    {
        await Assert.That(Match("3", "'3'")).IsNull();
        await Assert.That(Match("'3'", "3")).IsNull();
        await Assert.That(Match("3", "'03'")).IsNotNull();
        await Assert.That(Match("true", "'true'")).IsNotNull();
    }

    [Test]
    public async Task Null_matches_only_a_present_null()
    {
        await Assert.That(Matcher.Match(null, null, "error")).IsNull();
        await Assert.That(Matcher.Match(null, Missing.Value, "error")).IsEqualTo("error: expected null, got (absent)");
        await Assert.That(Match("{ error: null }", "{ }")).IsNotNull();
    }

    [Test]
    public async Task Absent_and_present()
    {
        await Assert.That(Match("{ signal: { $absent: true } }", "{ status: ok }")).IsNull();
        var mismatch = Match("{ signal: { $absent: true } }", "{ signal: SIGKILL }");

        await Assert.That(mismatch).IsEqualTo("signal: expected absent, got 'SIGKILL'");
        await Assert.That(Match("{ signal: { $present: true } }", "{ signal: null }")).IsNull();
        await Assert.That(Match("{ signal: { $present: true } }", "{ }")).IsNotNull();
    }

    [Test]
    public async Task Regex_is_unanchored()
    {
        await Assert.That(Match("{ $regex: 'bin/sh$' }", "/usr/bin/sh")).IsNull();
        await Assert.That(Match("{ $regex: '^x' }", "yx")).IsNotNull();
    }

    [Test]
    public async Task One_of_accepts_any_listed_value()
    {
        await Assert.That(Match("{ $one_of: [BK_EXIT, { $absent: true }] }", "BK_EXIT")).IsNull();
        await Assert.That(Matcher.Match(Values.Yaml("{ $one_of: [BK_EXIT, { $absent: true }] }"), Missing.Value, "x")).IsNull();
        await Assert.That(Match("{ $one_of: [a, b] }", "c")).IsNotNull();
    }

    [Test]
    public async Task Duration_bounds_have_slack_on_min_and_the_timing_factor_on_max()
    {
        var min = Values.Yaml("{ $min: 2s, $max: 4s }");
        await Assert.That(Matcher.Match(min, TimeSpan.FromMilliseconds(1960), "d")).IsNull();
        await Assert.That(Matcher.Match(min, TimeSpan.FromMilliseconds(1900), "d")).IsNotNull();
        await Assert.That(Matcher.Match(min, TimeSpan.FromSeconds(5), "d")).IsNotNull();
        await Assert.That(new Matcher(MatchOptions.Default with { TimingFactor = 2 }).Match(min, TimeSpan.FromSeconds(5), "d")).IsNull();
    }

    [Test]
    public async Task Numeric_bounds_have_no_slack()
    {
        await Assert.That(Match("{ $min: 2 }", "2")).IsNull();
        await Assert.That(Match("{ $min: 2 }", "1")).IsNotNull();
        await Assert.That(Match("{ $max: 2 }", "3")).IsNotNull();
    }

    [Test]
    public async Task A_duration_string_equals_the_same_duration()
    {
        await Assert.That(Matcher.Match("1m30s", TimeSpan.FromSeconds(90), "d")).IsNull();
        await Assert.That(Matcher.Match("90s", TimeSpan.FromSeconds(91), "d")).IsNotNull();
    }

    [Test]
    public async Task Unordered_needs_a_one_to_one_match()
    {
        // The first expectation fits both actual elements; only an augmenting path finds the assignment.
        await Assert.That(Match("{ $unordered: [{ $regex: a }, ab] }", "[ab, a]")).IsNull();
        await Assert.That(Match("{ $unordered: [a, a] }", "[a, b]")).IsNotNull();
        await Assert.That(Match("{ $unordered: [a] }", "[a, b]")).IsNotNull();
    }

    [Test]
    public async Task Contains_allows_other_elements()
    {
        const string Log = "[{ argv: [sleep, 1s] }, { argv: [exit, '0'], pid: 4 }]";

        await Assert.That(Match("{ $contains: [{ argv: [exit, '0'] }] }", Log)).IsNull();
        await Assert.That(Match("{ $contains: [a, a] }", "[a, b]")).IsNotNull();
    }

    [Test]
    public async Task Count_takes_a_number_or_a_range()
    {
        await Assert.That(Match("{ $count: 2 }", "[a, b]")).IsNull();
        await Assert.That(Match("{ $count: { $min: 3 } }", "[a, b]")).IsNotNull();
    }

    [Test]
    public async Task Secret_matches_a_secret_output()
    {
        await Assert.That(Matcher.Match(Values.Yaml("{ $secret: true }"), SecretValue.Value, "outputs.T")).IsNull();
        await Assert.That(Match("{ $secret: true }", "hunter2")).IsNotNull();
    }

    [Test]
    public async Task Misused_matchers_are_broken_cases()
    {
        await Assert.That(() => Match("{ $nope: 1 }", "1")).Throws<CaseFormatException>();
        await Assert.That(() => Match("{ $regex: a, $min: 1 }", "1")).Throws<CaseFormatException>();
        await Assert.That(() => Match("{ $absent: true, x: 1 }", "1")).Throws<CaseFormatException>();
        await Assert.That(() => Match("{ $absent: false }", "1")).Throws<CaseFormatException>();
    }

    [Test]
    public async Task The_first_mismatch_is_a_path_into_the_expectation()
    {
        var mismatch = Match(
            "{ nodes: { /all/0: { error: { code: BK_TIMEOUT_TOTAL } } } }",
            "{ nodes: { /all/0: { error: { code: BK_CANCELLED } } } }");

        await Assert.That(mismatch).IsEqualTo("nodes./all/0.error.code: expected 'BK_TIMEOUT_TOTAL', got 'BK_CANCELLED'");
    }
}
