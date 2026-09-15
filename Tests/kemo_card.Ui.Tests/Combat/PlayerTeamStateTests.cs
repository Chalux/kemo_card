using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class PlayerTeamStateTests
{
    [Test]
    public void PlayerTeamState_uses_shared_hp_pool()
    {
        var chars = new[]
        {
            CreateBattle("c0", hpCap: 10),
            CreateBattle("c1", hpCap: 15),
        };
        var team = new PlayerTeamState(chars, sharedMaxHp: 25);

        team.ApplySharedDamage(10);
        Assert.That(team.SharedHp, Is.EqualTo(15));
        team.ApplySharedDamage(20);
        Assert.That(team.IsDefeated, Is.True);
    }

    /// <summary>
    /// 存活判定必须用未取整账本值：<c>MathF.Round</c> 是银行家舍入，0.5 会被舍入为 0，
    /// 账本还有血量时队伍会被提前判负（GAS 公式可能产出小数伤害）。
    /// </summary>
    [Test]
    public void Fractional_ledger_above_zero_is_not_defeated()
    {
        var team = new PlayerTeamState([CreateBattle("c0", hpCap: 10)], sharedMaxHp: 10);

        team.ApplySharedDamage(9.5f);

        Assert.That(team.SharedHp, Is.EqualTo(0), "取整后的展示值本来就是 0");
        Assert.That(team.IsDefeated, Is.False, "0.5 账本不能被判负");
    }

    [Test]
    public void Zero_ledger_is_defeated()
    {
        var team = new PlayerTeamState([CreateBattle("c0", hpCap: 10)], sharedMaxHp: 10);

        team.ApplySharedDamage(10f);

        Assert.That(team.IsDefeated, Is.True);
    }

    /// <summary>未判负时仍可继续扣血直到真正归零。</summary>
    [Test]
    public void Fractional_ledger_can_still_take_damage_until_zero()
    {
        var team = new PlayerTeamState([CreateBattle("c0", hpCap: 10)], sharedMaxHp: 10);

        team.ApplySharedDamage(9.5f);
        team.ApplySharedDamage(0.5f);

        Assert.That(team.IsDefeated, Is.True);
        Assert.That(team.SharedHp, Is.EqualTo(0));
    }

    private static CharacterBattleInstance CreateBattle(string id, int hpCap)
    {
        var attrs = new CharacterAttributes(hpCap, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return CharacterBattleInstance.CreateForTests(id, attrs);
    }
}