using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CharacterBattleInstanceTests
{
	[Test]
	public void TryCreate_builds_shuffled_draw_pile_and_energy_from_attributes()
	{
		var registry = CombatTestHelper.CreateRegistry(
			new CardDto
			{
				Id = "strike",
				Stats = new CardStatBlockDto { HpCap = 4, MaxEnergy = 2, InitialEnergy = 1 },
			},
			new CardDto
			{
				Id = "strike_plus",
				Stats = new CardStatBlockDto { HpCap = 5, MaxEnergy = 1, InitialEnergy = 0 },
			});
		var source = new CharacterInstance(new CharacterDto { Id = "kemo", Cards = ["strike", "strike_plus"] });
		var rng = new HostRng(42, "combat.deck");

		var battle = CharacterBattleInstance.TryCreate(source, registry, rng, out var error);

		Assert.That(error, Is.Null);
		Assert.That(battle, Is.Not.Null);
		Assert.That(battle!.DrawPile, Has.Count.EqualTo(2));
		Assert.That(battle.HandSlots, Has.Length.EqualTo(CombatConstants.HandSlotCount));
		Assert.That(battle.HandSlots.All(slot => slot.IsEmpty), Is.True);
		Assert.That(battle.CurrentEnergy, Is.EqualTo(1));
		Assert.That(battle.MaxEnergy, Is.EqualTo(3));
		Assert.That(battle.BaseAttributes[AttributeIds.MaxHealth], Is.EqualTo(9f));
	}

	#region 能量二分（当前能量 / 当前可用能量）

	[Test]
	public void Battle_start_sets_current_energy_from_initial_and_leaves_available_pool_empty()
	{
		var character = CreateCharacter(initialEnergy: 2, maxEnergy: 5);

		Assert.That(character.CurrentEnergy, Is.EqualTo(2));
		Assert.That(character.AvailableEnergy, Is.Zero);
	}

	[Test]
	public void Battle_start_clamps_initial_energy_into_max_energy()
	{
		var character = CreateCharacter(initialEnergy: 9, maxEnergy: 4);

		Assert.That(character.CurrentEnergy, Is.EqualTo(4));
	}

	[Test]
	public void RegenCurrentEnergy_grows_by_one_and_stops_at_max_energy()
	{
		var character = CreateCharacter(initialEnergy: 2, maxEnergy: 3);

		character.RegenCurrentEnergy();
		Assert.That(character.CurrentEnergy, Is.EqualTo(3));

		character.RegenCurrentEnergy();
		Assert.That(character.CurrentEnergy, Is.EqualTo(3));
	}

	[Test]
	public void RefillAvailableEnergy_overwrites_instead_of_accumulating()
	{
		var character = CreateCharacter(initialEnergy: 3, maxEnergy: 5);

		character.RefillAvailableEnergy();
		Assert.That(character.AvailableEnergy, Is.EqualTo(3));

		character.GainAvailableEnergy(4);
		Assert.That(character.AvailableEnergy, Is.EqualTo(7));

		character.RefillAvailableEnergy();
		Assert.That(character.AvailableEnergy, Is.EqualTo(3), "灌入是覆盖，不累加残留");
	}

	[Test]
	public void GainAvailableEnergy_may_exceed_max_energy()
	{
		var character = CreateCharacter(initialEnergy: 1, maxEnergy: 2);
		character.RefillAvailableEnergy();

		character.GainAvailableEnergy(10);

		Assert.That(character.AvailableEnergy, Is.EqualTo(11));
		Assert.That(character.MaxEnergy, Is.EqualTo(2));
		Assert.That(character.CurrentEnergy, Is.EqualTo(1), "效果不提高当前能量成长曲线");
	}

	[Test]
	public void TryConsumeAvailableEnergy_spends_from_available_pool_only()
	{
		var character = CreateCharacter(initialEnergy: 3, maxEnergy: 5);
		character.RefillAvailableEnergy();

		Assert.That(character.TryConsumeAvailableEnergy(2), Is.True);
		Assert.That(character.AvailableEnergy, Is.EqualTo(1));
		Assert.That(character.CurrentEnergy, Is.EqualTo(3), "扣费不动当前能量");

		Assert.That(character.TryConsumeAvailableEnergy(2), Is.False);
		Assert.That(character.AvailableEnergy, Is.EqualTo(1), "余额不足时不做部分扣费");
	}

	[Test]
	public void RefundAvailableEnergy_has_no_upper_bound()
	{
		var character = CreateCharacter(initialEnergy: 1, maxEnergy: 2);
		character.RefillAvailableEnergy();

		character.RefundAvailableEnergy(5);

		Assert.That(character.AvailableEnergy, Is.EqualTo(6));
	}

	#endregion

	private static CharacterBattleInstance CreateCharacter(int initialEnergy, int maxEnergy) =>
		CharacterBattleInstance.CreateForTests(
			"hero",
			new Dictionary<string, float>(StringComparer.Ordinal)
			{
				[AttributeIds.InitialEnergy] = initialEnergy,
				[AttributeIds.MaxEnergy] = maxEnergy,
			});
}
