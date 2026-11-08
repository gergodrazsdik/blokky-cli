using Blokky.Core.Syntax;

namespace Blokky.Core.Tests;

public class PortableRegexTests
{
    [Test]
    [Arguments(@"(?i)^ab\-\d{2,3}(?:\s|\t)x+?y{2}z{1,}w?\b$")]
    [Arguments(@"^[^a-z]\x41\u{42}.\/\w+\W\S*$")]
    [Arguments(@"\Bo\B|^\D*$")]
    [Arguments(@"^a]b}c$")]
    [Arguments(@"^[-a]{3}[b-]$")]
    [Arguments(@"^[\^\]\\\-]{4}$")]
    [Arguments(@"^[\x41\u{42}]{2}$")]
    [Arguments(@"^a\fb\vc$")]
    [Arguments(@"a{1000}")]
    [Arguments(@"a{0,1000}?")]
    [Arguments(@"(?:a{100}){100}")]
    [Arguments(@"()|a|")]
    [Arguments(@"[--a]")]
    [Arguments(@"[\d\s_]")]
    public async Task Patterns_within_the_subset_are_accepted_and_compile(string pattern)
    {
        await Assert.That(PortableRegex.Check(pattern)).IsNull();
        await Assert.That(PortableRegex.Compile(pattern, false, out var error)).IsNotNull();
        await Assert.That(error).IsNull();
    }

    [Test]
    [Arguments("(main", "never closed")]
    [Arguments("[a-", "never closed")]
    [Arguments("a)", "closes no group")]
    [Arguments("a(?=b)", "lookahead")]
    [Arguments("(?<!b)a", "lookbehind")]
    [Arguments(@"(a)\1", "backreferences")]
    [Arguments("(?>a+)b", "atomic")]
    [Arguments("a++b", "another repetition")]
    [Arguments("a**", "another repetition")]
    [Arguments("a{2}{3}", "another repetition")]
    [Arguments("(?<word>a)", "named groups")]
    [Arguments(@"\p{L}", "Unicode classes")]
    [Arguments("(?m)^a", "inline options")]
    [Arguments("a(?i)B", "very start")]
    [Arguments(@"\Aabc\z", "use ^ and $")]
    [Arguments(@"a\qb", @"\q")]
    [Arguments("a{1001}", "at most 1000")]
    [Arguments("a{2,1001}", "at most 1000")]
    [Arguments("a{1001,}", "at most 1000")]
    [Arguments("a{3,2}", "smaller")]
    [Arguments("x{", "doesn't start a repetition")]
    [Arguments("a{,3}", "doesn't start a repetition")]
    [Arguments("*a", "repeats nothing")]
    [Arguments("^*", "anchor")]
    [Arguments(@"\b+", "anchor")]
    [Arguments("[]", "empty class")]
    [Arguments("[^]", "empty class")]
    [Arguments("[a[]", "unescaped '['")]
    [Arguments("[a-b-c]", "'-'")]
    [Arguments(@"[\d-z]", "class escape")]
    [Arguments("[z-a]", "before its start")]
    [Arguments(@"[\b]", @"\b")]
    [Arguments(@"\x4", "two hex digits")]
    [Arguments(@"\u{D800}", "scalar value")]
    [Arguments(@"\u42", "braces")]
    [Arguments(@"a\", "trailing")]
    [Arguments("(?:ab{100}){100}", "10000 nodes")]
    [Arguments("(?:(?:a{10}){10}){101}", "10000 nodes")]
    public async Task Patterns_outside_the_subset_are_RF_22(string pattern, string why)
    {
        var error = PortableRegex.Check(pattern);

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Rule).IsEqualTo("RF-22");
        await Assert.That(error.Message).Contains(why);
    }

    [Test]
    [Arguments("abc$", "abc\n", false)]
    [Arguments("abc$", "abc", true)]
    [Arguments("^b", "ab", false)]
    [Arguments("b", "ab", true)]
    [Arguments(@"\d", "٣", false)]
    [Arguments(@"^\w$", "é", false)]
    [Arguments(@"^\W$", "é", true)]
    [Arguments(@"\s", "\u00A0", false)]
    [Arguments(@"^\s\s$", "\v\f", true)]
    [Arguments(@"\s", "\u0085", false)]
    [Arguments(@"^.\bber$", "über", true)]
    [Arguments(@"^.$", "😀", true)]
    [Arguments(@"^.$", "\n", false)]
    [Arguments(@"^[😀-😂]$", "😁", true)]
    [Arguments(@"\B", "aéb", false)]
    [Arguments(@"^\u{1F600}$", "😀", true)]
    [Arguments("(?i)^k$", "\u212A", false)]
    [Arguments("(?i)^k$", "K", true)]
    [Arguments("(?i)^s$", "ſ", true)]
    [Arguments("(?i)^I$", "ı", true)]
    [Arguments("(?i)^[a-z]+$", "ſı", true)]
    [Arguments("(?i)^[a-z]$", "\u212A", false)]
    [Arguments("(?i)^σ$", "ς", true)]
    [Arguments("(?i)^[^a]$", "A", false)]
    [Arguments(@"(?i)^\w$", "ſ", false)]
    [Arguments(@"(?i)^[\W]$", "s", false)]
    [Arguments("a{2,3}?b", "aaab", true)]
    public async Task Matching_follows_the_subset_not_dotnet(string pattern, string value, bool expected)
    {
        var regex = PortableRegex.Compile(pattern, false, out var error);

        await Assert.That(error).IsNull();
        await Assert.That(regex!.IsMatch(value)).IsEqualTo(expected);
    }

    [Test]
    public async Task Ignore_case_applies_to_the_whole_pattern()
    {
        var regex = PortableRegex.Compile("^abc$", true, out _)!;

        await Assert.That(regex.IgnoreCase).IsTrue();
        await Assert.That(regex.IsMatch("ABC")).IsTrue();
        await Assert.That(PortableRegex.Compile("^abc$", false, out _)!.IsMatch("ABC")).IsFalse();
    }

    [Test]
    [Arguments("(?:.{1000}){10}", 'b')]
    [Arguments("(?i)(?:[a-z]{1000}){10}", 'B')]
    [Arguments("(?:[^a]{1000}){10}", 'é')]
    [Arguments(@"(?:\W{1000}){10}", '-')]
    public async Task Patterns_at_the_size_bound_compile(string pattern, char value)
    {
        // A full match at the bound builds about 10 000 DFA states on first use (seconds; a run-engine concern), so this
        // checks that the engine accepts the pattern and matches a short value.
        var (matched, elapsed, _) = await Bounded.RunAsync(() =>
        {
            var regex = PortableRegex.Compile(pattern, false, out var error) ?? throw new InvalidOperationException(error!.Message);
            return regex.IsMatch(new string(value, 200));
        }, TimeSpan.FromSeconds(60));

        await Assert.That(matched).IsFalse();
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Deep_nesting_and_long_patterns_are_checked_in_linear_time()
    {
        var deep = new string('(', 200_000) + "a" + new string(')', 200_000);
        var wide = string.Concat(Enumerable.Repeat("[a-z\\d]", 200_000));

        var repeated = string.Concat(Enumerable.Repeat("(?:a{1})", 100_000));

        var (errors, elapsed, _) = await Bounded.RunAsync(
            () => (PortableRegex.Check(deep), PortableRegex.Check(wide), PortableRegex.Check(repeated)), TimeSpan.FromSeconds(30));

        await Assert.That(errors.Item1).IsNull();
        await Assert.That(errors.Item2!.Message).Contains("10000 nodes");
        await Assert.That(errors.Item3!.Message).Contains("10000 nodes");
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Deeply_nested_groups_translate_in_linear_time()
    {
        var deep = new string('(', 20_000) + "a" + new string(')', 20_000) + "{3}";

        var (regex, elapsed, _) = await Bounded.RunAsync(() => PortableRegex.Compile(deep, false, out _), TimeSpan.FromSeconds(30));

        await Assert.That(regex).IsNotNull();
        await Assert.That(regex!.IsMatch("xaaa")).IsTrue();
        await Assert.That(regex.IsMatch("xaa")).IsFalse();
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task The_simple_uppercase_table_is_unicode_16()
    {
        await Assert.That(SimpleCase.Upper('a')).IsEqualTo('A');
        await Assert.That(SimpleCase.Upper(0x0131)).IsEqualTo('I');
        await Assert.That(SimpleCase.Upper(0x017F)).IsEqualTo('S');
        await Assert.That(SimpleCase.Upper(0x212A)).IsEqualTo(0x212A);
        await Assert.That(SimpleCase.Upper(0x00DF)).IsEqualTo(0x00DF);
        await Assert.That(SimpleCase.Upper(0x1F80)).IsEqualTo(0x1F88);
        await Assert.That(SimpleCase.EqualsIgnoreCase("ſıς", "SIΣ")).IsTrue();
        await Assert.That(SimpleCase.EqualsIgnoreCase("k", "\u212A")).IsFalse();
    }
}
