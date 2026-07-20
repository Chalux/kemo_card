using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CharacterSummaryBuilderTests
{
    private static string Tr(string key) => key switch
    {
        "UI_ELEMENT_RED" => "红",
        "UI_ELEMENT_BLUE" => "蓝",
        "UI_ROLE_WARRIOR" => "战士",
        "UI_RACE_CANINE" => "犬科",
        "UI_RACE_FELINE" => "猫科",
        "char.kemo.name" => "可萝",
        "d1" => "造成 6 点伤害。",
        "d2" => "[url=kw:exhaust]消耗[/url]",
        _ => key,
    };

    [Test]
    public void Build_formats_title_meta_and_skill_lines()
    {
        var character = new CharacterDto
        {
            DisplayNameId = "char.kemo.name",
            Element = EElement.Red | EElement.Blue,
            Role = ERole.Warrior,
            Race = ERace.Canine | ERace.Feline,
            SkillRefs =
            [
                new SkillRefDto { SkillId = "s1" },
                new SkillRefDto { SkillId = "s2" },
            ],
        };

        var tip = CharacterSummaryBuilder.Build(
            character,
            id => id switch
            {
                "s1" => new SkillDto { Id = "s1", DescId = "d1" },
                "s2" => new SkillDto { Id = "s2", DescId = "d2" },
                _ => null,
            },
            Tr);

        Assert.That(tip.Title, Is.EqualTo("可萝"));
        Assert.That(tip.Body, Is.EqualTo("红、蓝 战士 犬科、猫科\n造成 6 点伤害。\n消耗"));
    }

    [Test]
    public void Build_omits_none_role_and_race_in_meta()
    {
        var character = new CharacterDto
        {
            DisplayNameId = "char.kemo.name",
            Element = EElement.Red,
            Role = ERole.None,
            Race = ERace.None,
            SkillRefs = [new SkillRefDto { SkillId = "s1" }],
        };

        var tip = CharacterSummaryBuilder.Build(
            character,
            id => id == "s1" ? new SkillDto { Id = "s1", DescId = "d1" } : null,
            Tr);

        Assert.That(tip.Body, Is.EqualTo("红\n造成 6 点伤害。"));
    }
}
