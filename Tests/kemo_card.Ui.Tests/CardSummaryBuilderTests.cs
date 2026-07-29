using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardSummaryBuilderTests
{
    private static string Tr(string key) => key switch
    {
        "UI_CARD_TIP_COST_SUFFIX" => "费",
        "UI_CARD_TIP_COST_SUFFIX_X" => "费",
        "UI_CARD_TIP_EXCLUSIVE_PREFIX" => "专属于此角色：",
        "UI_ELEMENT_RED" => "红",
        "UI_ELEMENT_BLUE" => "蓝",
        "UI_ROLE_WARRIOR" => "战士",
        "card.strike.name" => "打击",
        "d1" => "造成 6 点伤害。",
        "d2" => "[url=kw:exhaust]消耗[/url]",
        _ => key,
    };

    [Test]
    public void FormatCostForTip_energy_appends_suffix()
    {
        Assert.That(
            CardUiDefinitions.FormatCostForTip(ECostType.Energy, 3, Tr),
            Is.EqualTo("3费"));
    }

    [Test]
    public void FormatCostForTip_x_uses_x_suffix()
    {
        Assert.That(
            CardUiDefinitions.FormatCostForTip(ECostType.X, 0, Tr),
            Is.EqualTo("X费"));
    }

    [Test]
    public void FormatCostForTip_none_returns_empty()
    {
        Assert.That(
            CardUiDefinitions.FormatCostForTip(ECostType.None, 5, Tr),
            Is.EqualTo(""));
    }

    [Test]
    public void TryGetCostTipSuffixKey_unknown_falls_back_to_normal()
    {
        Assert.That(CardUiDefinitions.TryGetCostTipSuffixKey(ECostType.Health, out var key), Is.True);
        Assert.That(key, Is.EqualTo("UI_CARD_TIP_COST_SUFFIX"));
    }

    [Test]
    public void StripRichText_removes_bbcode_keeps_inner()
    {
        Assert.That(
            CardSummaryBuilder.StripRichText("造成[url=kw:exhaust]消耗[/url]伤害"),
            Is.EqualTo("造成消耗伤害"));
    }

    [Test]
    public void Build_formats_meta_effect_and_exclusive_lines()
    {
        var card = new CardDto
        {
            Id = "strike",
            DisplayNameId = "card.strike.name",
            CostType = ECostType.Energy,
            Cost = 3,
            Element = (int)EElement.Red,
            Role = ERole.Warrior,
            IsExclusive = true,
            SkillRefs = [new SkillRefDto { SkillId = "s1" }],
        };

        var tip = CardSummaryBuilder.Build(
            card,
            id => id == "s1" ? new SkillDto { Id = "s1", DescId = "d1" } : null,
            Tr,
            cardId => cardId == "strike" ? "可萝" : null);

        Assert.That(tip.Title, Is.EqualTo("打击"));
        Assert.That(tip.Body, Is.EqualTo("3费 红 战士\n造成 6 点伤害。\n专属于此角色：可萝"));
    }

    [Test]
    public void Build_omits_exclusive_when_resolver_returns_null()
    {
        var card = new CardDto
        {
            DisplayNameId = "card.strike.name",
            CostType = ECostType.X,
            Cost = 0,
            Element = 0,
            Role = ERole.None,
            IsExclusive = false,
            SkillRefs = [new SkillRefDto { SkillId = "s1" }],
        };

        var tip = CardSummaryBuilder.Build(
            card,
            id => id == "s1" ? new SkillDto { Id = "s1", DescId = "d1" } : null,
            Tr,
            _ => null);

        Assert.That(tip.Title, Is.EqualTo("打击"));
        Assert.That(tip.Body, Is.EqualTo("X费\n造成 6 点伤害。"));
    }

    [Test]
    public void Build_strips_bbcode_in_effect()
    {
        var card = new CardDto
        {
            DisplayNameId = "card.strike.name",
            CostType = ECostType.None,
            SkillRefs = [new SkillRefDto { SkillId = "s2" }],
        };

        var tip = CardSummaryBuilder.Build(
            card,
            id => id == "s2" ? new SkillDto { Id = "s2", DescId = "d2" } : null,
            Tr,
            _ => null);

        Assert.That(tip.Body, Is.EqualTo("消耗"));
    }
}