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

    private static CharacterBattleInstance CreateBattle(string id, int hpCap)
    {
        var attrs = new CharacterAttributes(hpCap, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return CharacterBattleInstance.CreateForTests(id, attrs);
    }
}