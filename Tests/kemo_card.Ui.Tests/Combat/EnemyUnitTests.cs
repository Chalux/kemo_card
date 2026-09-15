using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class EnemyUnitTests
{
    [Test]
    public void EnemyUnit_tracks_hp_and_alive_state()
    {
        var unit = new EnemyUnit("rt-1", "slime", maxHp: 20);
        Assert.That(unit.IsAlive, Is.True);
        unit.ApplyDamage(8);
        Assert.That(unit.CurrentHp, Is.EqualTo(12));
        unit.ApplyDamage(12);
        Assert.That(unit.IsAlive, Is.False);
    }

    /// <summary>
    /// 存活判定必须用未取整血量：<c>MathF.Round</c> 是银行家舍入，剩 0.5 血会被舍入为 0，
    /// 敌人会被提前当作已阵亡（导致击杀判定、胜负规则与目标池错误）。
    /// </summary>
    [Test]
    public void Enemy_at_fractional_hp_is_still_alive()
    {
        var unit = new EnemyUnit("rt-2", "slime", maxHp: 10);

        unit.Asc.Attributes.SetCurrentValue(AttributeIds.Health, 0.5f);

        Assert.That(unit.CurrentHp, Is.EqualTo(0), "取整后的展示值本来就是 0");
        Assert.That(unit.IsAlive, Is.True, "0.5 血不能被判为已阵亡");
    }

    [Test]
    public void Enemy_at_zero_hp_is_not_alive()
    {
        var unit = new EnemyUnit("rt-3", "slime", maxHp: 10);

        unit.ApplyDamage(10);

        Assert.That(unit.IsAlive, Is.False);
    }

    [Test]
    public void CombatTargetRef_is_value_equality()
    {
        var a = new CombatTargetRef(ECombatSide.Enemy, 0);
        var b = new CombatTargetRef(ECombatSide.Enemy, 0);
        Assert.That(a, Is.EqualTo(b));
    }
}