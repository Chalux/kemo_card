using KemoCard.Frame.Content.Definitions;
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
		Assert.That(battle.BaseAttributes.HpCap, Is.EqualTo(9));
	}
}
