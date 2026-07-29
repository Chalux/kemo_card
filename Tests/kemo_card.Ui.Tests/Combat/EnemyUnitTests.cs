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

    [Test]
    public void CombatTargetRef_is_value_equality()
    {
        var a = new CombatTargetRef(ECombatSide.Enemy, 0);
        var b = new CombatTargetRef(ECombatSide.Enemy, 0);
        Assert.That(a, Is.EqualTo(b));
    }
}