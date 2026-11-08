using System.Collections.Frozen;
using System.Text;
using Blokky.Core.Features;
using Blokky.Core.Syntax;
using Blokky.Core.Validation;

namespace Blokky.Core.Tests;

public class SyntaxRulesTests
{
    private static readonly FrozenSet<string> Everything = FeatureIds.All.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Each error as <c>rule line:column node</c>.</summary>
    private static string Errors(string plan) =>
        string.Join("; ", new PlanValidator(Everything).Validate(Encoding.UTF8.GetBytes(plan)).Errors.Select(e =>
            $"{e.Code} {e.Detail?["rule"]} {e.Pos} {e.Node}"));

    // ---- exec strings ----------------------------------------------------------------------------------------------

    [Test]
    [Arguments("dotnet build -c Release", "dotnet|build|-c|Release")]
    [Arguments("  a\tb\nc  ", "a|b|c")]
    [Arguments("a 'b c' \"d e\"", "a|b c|d e")]
    [Arguments("--name='a b'x", "--name=a bx")]
    [Arguments("a '' \"\"", "a||")]
    [Arguments(@"a \$HOME \` \| \*", "a|$HOME|`|||*")]
    [Arguments(@"a ""x\""y\\z\n""", @"a|x""y\z\n")]
    [Arguments("a '$HOME `x` |;*'", "a|$HOME `x` |;*")]
    [Arguments("a b~ c#d x=~", "a|b~|c#d|x=~")]
    public async Task Exec_strings_split_into_argv(string text, string expected)
    {
        await Assert.That(ExecString.Split(text, out var argv)).IsNull();
        await Assert.That(string.Join("|", argv)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("a $HOME", "'$'")]
    [Arguments("a \"$HOME\"", "inside double quotes")]
    [Arguments("a `b`", "backtick")]
    [Arguments("a|b", "'|'")]
    [Arguments("a 2>&1", "'>'")]
    [Arguments("a *.txt", "'*'")]
    [Arguments("a {b,c}", "'{'")]
    [Arguments("a ~/x", "'~'")]
    [Arguments("a #x", "'#'")]
    [Arguments("a 'b", "single quote")]
    [Arguments("a \"b", "double quote")]
    [Arguments("a\\", "trailing")]
    [Arguments(" \t\n", "no words")]
    public async Task Exec_strings_with_shell_syntax_are_SH_7(string text, string why)
    {
        var error = ExecString.Split(text, out _);

        await Assert.That(error!.Rule).IsEqualTo("SH-7");
        await Assert.That(error.Message).Contains(why);
    }

    [Test]
    public async Task Interpolation_in_an_exec_string_is_left_to_RF_9()
    {
        await Assert.That(ExecString.Split("echo ${X} \"${Y}\"", out var argv)).IsNull();
        await Assert.That(string.Join("|", argv)).IsEqualTo("echo|${X}|${Y}");
        await Assert.That(ExecString.Split("echo ${X} a|b", out _)!.Rule).IsEqualTo("SH-7");
    }

    [Test]
    [Arguments("tool.cmd", true)]
    [Arguments("./tools/build.BAT", true)]
    [Arguments("x.Cmd", true)]
    [Arguments("npm", false)]
    [Arguments("cmd", false)]
    [Arguments("x.bat.exe", false)]
    public async Task Batch_files_as_argv0_are_SH_10(string program, bool rejected)
    {
        await Assert.That(ExecString.BatchFile(program) is not null).IsEqualTo(rejected);
    }

    // ---- JSON paths ------------------------------------------------------------------------------------------------

    [Test]
    [Arguments(".", "")]
    [Arguments(".number", "number")]
    [Arguments(".head.ref", "head,ref")]
    [Arguments(".[0].databaseId", "[0],databaseId")]
    [Arguments(".labels[1].name", "labels,[1],name")]
    [Arguments(".[\"weird-key\"].x", "weird-key,x")]
    [Arguments(".a.[\"b-c\"]", "a,b-c")]
    [Arguments(".list[0][10]", "list,[0],[10]")]
    [Arguments(".[\"q\\\"k\\u0041\"]", "q\"kA")]
    [Arguments("._a1", "_a1")]
    public async Task Json_paths_parse_into_segments(string text, string expected)
    {
        await Assert.That(JsonPath.Parse(text, out var segments)).IsNull();
        await Assert.That(string.Join(",", segments.Select(s => s.Member ?? $"[{s.Index}]"))).IsEqualTo(expected);
    }

    [Test]
    [Arguments("", "SY-65")]
    [Arguments("c", "SY-65")]
    [Arguments(".a-b", "SY-65")]
    [Arguments(".1a", "SY-65")]
    [Arguments(".a.", "SY-65")]
    [Arguments(".a b", "SY-65")]
    [Arguments(".['a-b']", "SY-66")]
    [Arguments("[\"a-b\"]", "SY-66")]
    [Arguments(".[\"a\"", "SY-66")]
    [Arguments(".[\"a\\x\"]", "SY-66")]
    [Arguments(".[\"a\" ]", "SY-66")]
    [Arguments(".c[x]", "SY-67")]
    [Arguments(".c[01]", "SY-67")]
    [Arguments(".c[", "SY-67")]
    [Arguments(".c[99999999999]", "SY-67")]
    [Arguments(".items[]", "SY-68")]
    [Arguments(".items[-1]", "SY-68")]
    [Arguments(".items[0:2]", "SY-68")]
    [Arguments("..name", "SY-68")]
    [Arguments(".a..b", "SY-68")]
    [Arguments(".items | length", "SY-68")]
    [Arguments(".items[] | select(.ok)", "SY-68")]
    public async Task Malformed_and_unsupported_json_paths(string text, string rule)
    {
        await Assert.That(JsonPath.Parse(text, out _)?.Rule).IsEqualTo(rule);
    }

    [Test]
    public async Task A_pipe_inside_a_bracket_key_is_a_key()
    {
        await Assert.That(JsonPath.Parse(".[\"a|b(c)\"]", out var segments)).IsNull();
        await Assert.That(segments.Single().Member).IsEqualTo("a|b(c)");
    }

    // ---- globs -----------------------------------------------------------------------------------------------------

    [Test]
    public async Task Globs_parse_into_segments()
    {
        await Assert.That(GlobPattern.Parse("docs/**/a?[!c-e]*.md", out var segments)).IsNull();
        await Assert.That(segments.Count).IsEqualTo(3);
        await Assert.That(segments[1].Recursive).IsTrue();
        await Assert.That(string.Join(",", segments[2].Tokens.Select(t => t.Kind))).IsEqualTo("Literal,Question,Class,Star,Literal");
        await Assert.That(segments[2].Tokens[2].Negated).IsTrue();
        await Assert.That(segments[2].Tokens[2].Set!.Contains('d')).IsTrue();

        await Assert.That(GlobPattern.Parse(@"e/a\[1\].md", out var escaped)).IsNull();
        await Assert.That(escaped[1].Tokens.Single().Text).IsEqualTo("a[1].md");
        await Assert.That(GlobPattern.Parse(".github/*.yml", out var dot)).IsNull();
        await Assert.That(dot[0].StartsWithDot).IsTrue();
        await Assert.That(GlobPattern.Parse("src/..x/*.md", out _)).IsNull();
        await Assert.That(GlobPattern.Parse(@"src/\{a\}.md", out _)).IsNull();
    }

    [Test]
    [Arguments("src/a**b/x.md", "SY-72")]
    [Arguments("docs/**.md", "SY-72")]
    [Arguments("***", "SY-72")]
    [Arguments("src/*.{cs,fs}", "SY-73")]
    [Arguments("src/a}.md", "SY-73")]
    [Arguments(@"a\", "SY-73")]
    [Arguments("../*.txt", "SY-102")]
    [Arguments("src/../docs/*.md", "SY-102")]
    [Arguments(@"a/\.\./b", "SY-102")]
    [Arguments("..", "SY-102")]
    [Arguments("/etc/*", "SY-70")]
    [Arguments("a//b", "SY-70")]
    [Arguments("a/", "SY-70")]
    [Arguments("a[bc", "SY-71")]
    [Arguments("a[b/c]", "SY-71")]
    [Arguments("a[]", "SY-71")]
    [Arguments("[z-a]", "SY-71")]
    public async Task Malformed_globs(string text, string rule)
    {
        await Assert.That(GlobPattern.Parse(text, out _)?.Rule).IsEqualTo(rule);
    }

    // ---- image references ------------------------------------------------------------------------------------------

    [Test]
    [Arguments("alpine", null, "alpine", null)]
    [Arguments("alpine:3.20", null, "alpine", "3.20")]
    [Arguments("localhost:5000/team/app:1.2", "localhost:5000", "team/app", "1.2")]
    [Arguments("mcr.microsoft.com/dotnet/sdk:10.0", "mcr.microsoft.com", "dotnet/sdk", "10.0")]
    [Arguments("localhost/app", "localhost", "app", null)]
    [Arguments("library/a__b-c--d.e", null, "library/a__b-c--d.e", null)]
    [Arguments("10.0.0.1:5000/x", "10.0.0.1:5000", "x", null)]
    public async Task Image_references_parse(string text, string? registry, string name, string? tag)
    {
        var image = ImageReference.Parse(text, out var error);

        await Assert.That(error).IsNull();
        await Assert.That(image!.Registry).IsEqualTo(registry);
        await Assert.That(image.Name).IsEqualTo(name);
        await Assert.That(image.Tag).IsEqualTo(tag);
    }

    [Test]
    public async Task An_image_reference_with_a_digest_parses()
    {
        const string Digest = "sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec";
        var image = ImageReference.Parse("docker.io/library/busybox@" + Digest, out _);

        await Assert.That(image!.Digest).IsEqualTo(Digest);
        await Assert.That(image.Registry).IsEqualTo("docker.io");
    }

    [Test]
    [Arguments("busybox:")]
    [Arguments("Busybox:1.37")]
    [Arguments("busybox@sha256:a7dbc319")]
    [Arguments("busybox:-1.37")]
    [Arguments("busybox@md5")]
    [Arguments("a_")]
    [Arguments("a___b")]
    [Arguments("-a")]
    [Arguments("a//b")]
    [Arguments("my-.registry:5000/a")]
    [Arguments("host:port/a")]
    [Arguments("a/")]
    public async Task Malformed_image_references_are_SY_103(string text)
    {
        ImageReference.Parse(text, out var error);

        await Assert.That(error?.Rule).IsEqualTo("SY-103");
    }

    [Test]
    public async Task Long_image_names_are_SY_103()
    {
        ImageReference.Parse("example.com/" + new string('a', 243), out var ok);
        ImageReference.Parse("example.com/" + new string('a', 244), out var tooLong);

        await Assert.That(ok).IsNull();
        await Assert.That(tooLong?.Rule).IsEqualTo("SY-103");
    }

    // ---- the rule family on plans ----------------------------------------------------------------------------------

    [Test]
    public async Task Every_string_grammar_is_checked_where_it_is_used()
    {
        const string Plan =
            "blokky: 1.0\n"
            + "inputs:\n"
            + "  A:\n"
            + "    matches: '(a'\n"
            + "vars: { X: a, V: '[]' }\n"
            + "do:\n"
            + "  - exec: a|b\n"
            + "  - exec: [x.cmd]\n"
            + "  - exec: [a, '*']\n"
            + "    capture: { C: c }\n"
            + "  - for: { glob: 'a**', as: F }\n"
            + "    exec: [a]\n"
            + "  - for: { items: { var: V, json: '.[]' }, as: I }\n"
            + "    exec: [a]\n"
            + "  - container: { image: 'A' }\n"
            + "    exec: [a]\n"
            + "  - when: { all: [{ var: X, json: x, equals: a }, { not: { var: X, matches: '(?=a)' } }] }\n"
            + "    log: x\n"
            + "  - when: { probe: { exec: 'b;c' } }\n"
            + "    log: x\n"
            + "  - services:\n"
            + "      - name: web\n"
            + "        exec: x.bat\n"
            + "        ready: { var: X, not_matches: '[' }\n"
            + "    exec: [a]\n";

        await Assert.That(Errors(Plan)).IsEqualTo(
            "BK_PLAN_VALUE RF-22 4:14 /; BK_PLAN_VALUE SH-7 7:11 /do/0; BK_PLAN_VALUE SH-10 8:12 /do/1; "
            + "BK_PLAN_VALUE SY-65 10:19 /do/2; BK_PLAN_VALUE SY-72 11:18 /do/3; BK_PLAN_VALUE SY-68 13:35 /do/4; "
            + "BK_PLAN_VALUE SY-103 15:25 /do/5; BK_PLAN_VALUE SY-65 17:35 /do/6; BK_PLAN_VALUE RF-22 17:77 /do/6; "
            + "BK_PLAN_VALUE SH-7 19:28 /do/7; BK_PLAN_VALUE SH-10 23:15 /do/8; BK_PLAN_VALUE RF-22 24:39 /do/8");
    }

    [Test]
    public async Task Valid_strings_report_nothing()
    {
        const string Plan =
            "blokky: 1.0\n"
            + "inputs:\n"
            + "  A:\n"
            + "    matches: '^v\\d+$'\n"
            + "vars: { X: a, V: '[{\"name\":\"n\"}]' }\n"
            + "do:\n"
            + "  - exec: git status --short\n"
            + "  - exec: [build.sh, x.cmd]\n"
            + "    capture: { C: '.a[\"b\"][0]' }\n"
            + "  - for: { glob: 'docs/**/*.md', as: F }\n"
            + "    exec: [a]\n"
            + "  - for: { items: { var: V }, as: I, fields: { N: .name } }\n"
            + "    exec: [a]\n"
            + "  - container: { image: 'alpine:3.20' }\n"
            + "    exec: [a]\n"
            + "  - when: { var: X, json: ., matches: '(?i)^ok$', ignore_case: true }\n"
            + "    log: x\n";
        const string Interpolated =
            "blokky: 1.0\n"
            + "vars: { X: a }\n"
            + "do:\n"
            + "  - container: { image: '${X}' }\n"
            + "    exec: [a]\n"
            + "  - for: { glob: '${X}/{a,b}', as: F }\n"
            + "    exec: [a]\n"
            + "  - when: { var: X, json: '.${X}', matches: '${X}(' }\n"
            + "    log: x\n";

        await Assert.That(Errors(Plan)).IsEqualTo("");

        // ${…} in a literal field is RF-9's mistake (T-0083), not a grammar error.
        await Assert.That(Errors(Interpolated)).DoesNotContain("BK_PLAN_VALUE");
    }
}
