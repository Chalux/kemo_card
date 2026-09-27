using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 充能球位悬停文案：标题 = 球名；正文 = 触发效果（与词典共用 <c>UI_GLOSSARY_ORB_BODY_*</c>）+ 产球者。
/// </summary>
[TestFixture]
public sealed class CombatOrbTipsTests
{
    private static string Tr(string key) => key switch
    {
        "orb.blue.name" => "蓝属性球",
        "UI_GLOSSARY_ORB_BODY_ELEMENTAL" => "元素球：每球 {0} 点 + 攻击力的 {1}%；不吃防御。",
        "UI_GLOSSARY_ORB_BODY_SUPPORT" => "支援球：不造成伤害；执行自身携带的效果。",
        "UI_COMBAT_ORB_PRODUCER" => "产球者：{0}",
        "UI_COMBAT_ORB_PRODUCER_UNKNOWN" => "产球者：触发时按全队最高攻击者",
        _ => key,
    };

    [Test]
    public void Build_includes_effect_and_producer()
    {
        var orb = new OrbTypeDto
        {
            Id = "orb.blue",
            DisplayNameId = "orb.blue.name",
            DamageKind = EDamageKind.Elemental,
            PerOrbAmount = 6f,
            AttackBonusScale = 1f,
        };

        var (title, body) = CombatOrbTips.Build(
            orb,
            orb.Id,
            producerIndex: 2,
            _ => "克鲁克斯",
            Tr);

        Assert.That(title, Is.EqualTo("蓝属性球"));
        Assert.That(body, Is.EqualTo("元素球：每球 6 点 + 攻击力的 100%；不吃防御。\n产球者：克鲁克斯"));
    }

    [Test]
    public void Build_uses_support_body_and_falls_back_to_id_without_display_name()
    {
        var orb = new OrbTypeDto { Id = "orb.special", DisplayNameId = "", DealsDamage = false };

        var (title, body) = CombatOrbTips.Build(orb, orb.Id, producerIndex: 0, _ => "图灵", Tr);

        Assert.That(title, Is.EqualTo("orb.special"), "无显示名回落 id");
        Assert.That(body, Does.StartWith("支援球："));
        Assert.That(body, Does.EndWith("产球者：图灵"));
    }

    [Test]
    public void Build_falls_back_to_unknown_producer_line()
    {
        var (_, body) = CombatOrbTips.Build(null, "orb.ghost", producerIndex: -1, _ => "", Tr);

        Assert.That(body, Is.EqualTo("产球者：触发时按全队最高攻击者"));
    }

    [Test]
    public void Build_throws_on_empty_orb_id()
    {
        Assert.That(() => CombatOrbTips.Build(null, "", 0, _ => "", Tr), Throws.ArgumentException);
    }
}
