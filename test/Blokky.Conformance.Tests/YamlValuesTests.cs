using Blokky.Conformance.Yaml;

namespace Blokky.Conformance.Tests;

public class YamlValuesTests
{
    [Test]
    public async Task Plain_scalars_follow_the_YAML_1_2_core_schema()
    {
        var map = Values.Map("a: 3\nb: true\nc: ~\nd: 2s\ne: '3'\nf: 1.5\ng: yes\nh: 0x1f\ni:\n");

        await Assert.That(map["a"]).IsEqualTo(3L);
        await Assert.That(map["b"] is true).IsTrue();
        await Assert.That(map["c"]).IsNull();
        await Assert.That(map["d"]).IsEqualTo("2s");
        await Assert.That(map["e"]).IsEqualTo("3");
        await Assert.That(map["f"]).IsEqualTo(1.5);
        await Assert.That(map["g"]).IsEqualTo("yes");
        await Assert.That(map["h"]).IsEqualTo(31L);
        await Assert.That(map["i"]).IsNull();
    }

    [Test]
    public async Task A_literal_block_keeps_the_plan_text_and_its_lines()
    {
        var map = Values.Map("plan: |\n  blokky: 1.0\n  log: hi\nnext: 1\n");

        await Assert.That(map["plan"]).IsEqualTo("blokky: 1.0\nlog: hi\n");
    }

    [Test]
    public async Task Duplicate_keys_are_a_broken_case()
    {
        await Assert.That(() => YamlValues.Load("a: 1\na: 2\n")).Throws<CaseFormatException>();
    }

    [Test]
    public async Task Invalid_YAML_is_a_broken_case()
    {
        await Assert.That(() => YamlValues.Load("a: [1, 2\n")).Throws<CaseFormatException>();
    }

    [Test]
    public async Task Aliases_are_rejected()
    {
        await Assert.That(() => YamlValues.Load("a: &x 1\nb: *x\n")).Throws<CaseFormatException>();
    }
}
