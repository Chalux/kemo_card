using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

/// <summary>
/// <c>SetBaseValue</c> 的两条路径必须区分：
/// ASC 路径（<see cref="AbilitySystemComponent.SetBaseValue"/>）写入后按修饰符重算；
/// 裸 <see cref="AttributeSet.SetBaseValue"/> 是「无修饰符」语义，会覆盖聚合结果。
/// </summary>
[TestFixture]
public sealed class AscSetBaseValueTests
{
    [Test]
    public void Asc_SetBaseValue_preserves_modifier_contribution()
    {
        var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.MaxHealth, 10f));
        asc.Aggregator.SetModifiers(
            AttributeIds.MaxHealth,
            [new AttributeModifier(EAttributeModifierOp.Multiply, 2f, order: 0)]);
        Assert.That(asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(20f));

        asc.SetBaseValue(AttributeIds.MaxHealth, 15f);

        Assert.That(asc.GetBaseValue(AttributeIds.MaxHealth), Is.EqualTo(15f));
        Assert.That(
            asc.GetCurrentValue(AttributeIds.MaxHealth),
            Is.EqualTo(30f),
            "写入 base 后必须重算，保留 Multiply 修饰符贡献");
    }

    [Test]
    public void Asc_SetBaseValue_without_modifiers_equals_base()
    {
        var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.MaxHealth, 10f));

        asc.SetBaseValue(AttributeIds.MaxHealth, 15f);

        Assert.That(asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(15f));
    }

    [Test]
    public void Raw_AttributeSet_SetBaseValue_overwrites_aggregated_value()
    {
        // 契约固化：裸 set 没有聚合器，写入 base 即当前值。
        // 持有 ASC 的调用方必须用 AbilitySystemComponent.SetBaseValue（见本 fixture 首个用例）。
        var set = GasTestHelper.CreateAttributeSet((AttributeIds.MaxHealth, 10f));

        set.SetBaseValue(AttributeIds.MaxHealth, 15f);

        Assert.That(set.GetBaseValue(AttributeIds.MaxHealth), Is.EqualTo(15f));
        Assert.That(set.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(15f));
    }

    /// <summary>
    /// 真实故障路径：队伍 ASC 上挂着 MaxSharedHp 修饰符时，
    /// 角色 MaxHealth 变化触发的重算此前会把修饰符贡献抹掉。
    /// </summary>
    [Test]
    public void Team_max_health_recompute_keeps_team_asc_modifier()
    {
        var first = CharacterBattleInstance.CreateForTests(
            "c0",
            new CharacterAttributes(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var second = CharacterBattleInstance.CreateForTests(
            "c1",
            new CharacterAttributes(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var team = new PlayerTeamState([first, second], sharedMaxHp: 20);
        using var coordinator = new TeamMaxHealthCoordinator(team);

        // 队伍 ASC 上的域 GE 修饰符（等价于 TeamDomainManager 挂的无限域效果）
        team.Asc.Aggregator.SetModifiers(
            AttributeIds.MaxHealth,
            [new AttributeModifier(EAttributeModifierOp.Multiply, 2f, order: 0)]);
        Assert.That(team.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(40f));

        // 角色 MaxHealth +5 → 触发 TeamMaxHealthCoordinator.RecomputeAndClamp
        var ge = GasTestHelper.InstantAddModifier(AttributeIds.MaxHealth, 5f);
        first.Asc.ApplyGameplayEffect(new GameplayEffectSpec(ge, targetAsc: first.Asc));

        Assert.That(team.Asc.GetBaseValue(AttributeIds.MaxHealth), Is.EqualTo(25f));
        Assert.That(
            team.Asc.GetCurrentValue(AttributeIds.MaxHealth),
            Is.EqualTo(50f),
            "重算后必须保留队伍 ASC 的 Multiply 修饰符贡献（此前被覆盖成 25）");
    }
}