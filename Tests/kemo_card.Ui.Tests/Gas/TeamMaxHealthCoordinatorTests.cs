using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class TeamMaxHealthCoordinatorTests
{
	[Test]
	public void Character_max_health_increase_raises_max_but_leaves_shared_hp_untouched()
	{
		var team = CreateTeam(10, 10);
		using var coordinator = new TeamMaxHealthCoordinator(team);
		team.ApplySharedDamage(4);

		var ge = GasTestHelper.InstantAddModifier(AttributeIds.MaxHealth, 5f);
		team.Characters[0].Asc.ApplyGameplayEffect(new GameplayEffectSpec(ge, targetAsc: team.Characters[0].Asc));

		Assert.That(team.MaxHp, Is.EqualTo(25));
		Assert.That(team.SharedHp, Is.EqualTo(16), "规格 §1.2：重算 MaxSharedHp 不跟涨 SharedHp");
	}

	[Test]
	public void Character_max_health_increase_does_not_top_up_a_full_team()
	{
		var team = CreateTeam(10, 10);
		using var coordinator = new TeamMaxHealthCoordinator(team);

		var ge = GasTestHelper.InstantAddModifier(AttributeIds.MaxHealth, 5f);
		team.Characters[0].Asc.ApplyGameplayEffect(new GameplayEffectSpec(ge, targetAsc: team.Characters[0].Asc));

		Assert.That(team.MaxHp, Is.EqualTo(25));
		Assert.That(team.SharedHp, Is.EqualTo(20), "上升不补血，需要补满时走 FreezeAndFillSharedHp");
	}

	[Test]
	public void Character_max_health_decrease_clamps_team_health_to_new_max()
	{
		var team = CreateTeam(10, 10);
		using var coordinator = new TeamMaxHealthCoordinator(team);

		team.ApplySharedDamage(2);
		var ge = GasTestHelper.InstantAddModifier(AttributeIds.MaxHealth, -5f);
		team.Characters[0].Asc.ApplyGameplayEffect(new GameplayEffectSpec(ge, targetAsc: team.Characters[0].Asc));

		Assert.That(team.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(15f));
		Assert.That(team.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(15f));
		Assert.That(team.MaxHp, Is.EqualTo(15));
		Assert.That(team.SharedHp, Is.EqualTo(15));
	}

	private static PlayerTeamState CreateTeam(int firstMaxHealth, int secondMaxHealth)
	{
		var first = CharacterBattleInstance.CreateForTests(
			"c0",
			new CharacterAttributes(firstMaxHealth, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
		var second = CharacterBattleInstance.CreateForTests(
			"c1",
			new CharacterAttributes(secondMaxHealth, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
		return new PlayerTeamState([first, second], firstMaxHealth + secondMaxHealth);
	}
}
