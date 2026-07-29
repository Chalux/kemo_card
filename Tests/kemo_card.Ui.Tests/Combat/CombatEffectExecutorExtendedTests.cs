using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatEffectExecutorExtendedTests
{
	[Test]
	public void Draw_is_blocked_mid_battle_and_counts_diagnostic()
	{
		var drawPile = new[]
		{
			new CardRuntimeEntry("card_a", "rt-a"),
			new CardRuntimeEntry("card_b", "rt-b"),
			new CardRuntimeEntry("card_c", "rt-c"),
		};
		var attrs = new CharacterAttributes(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
		var character = CharacterBattleInstance.CreateForTests("c0", attrs, drawPile);
		var registry = CombatTestHelper.CreateFullRegistry(
			effects: new Dictionary<string, EffectDto>
			{
				["draw2"] = new()
				{
					Id = "draw2",
					Kind = EEffectKind.Draw,
					Params = new Dictionary<string, object> { ["count"] = 2 },
				},
			});
		var enemy = new EnemyUnit("e0", "slime", maxHp: 20);
		var player = new PlayerTeamState([character], sharedMaxHp: 10);
		var sim = new CombatSimulation(
			player,
			new EnemyTeamState([enemy]),
			new CombatRuleEngine([]),
			registry);
		var executor = new CombatEffectExecutor(registry, sim.Rules);
		var source = new CombatTargetRef(ECombatSide.Player, 0);

		executor.ExecuteEffectRef(new EffectRefDto { EffectId = "draw2" }, sim, source, [source]);

		Assert.That(character.DrawPile, Has.Count.EqualTo(3), "规格 §4.3：中途 Draw 无操作");
		Assert.That(character.HandSlots.Count(slot => !slot.IsEmpty), Is.Zero);
		Assert.That(sim.BlockedMidDrawCount, Is.EqualTo(1));
	}

	[Test]
	public void GainResource_adds_energy_to_available_pool_only()
	{
		var attrs = new CharacterAttributes(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 1);
		var character = CharacterBattleInstance.CreateForTests("c0", attrs);
		character.RefillAvailableEnergy();
		var registry = CombatTestHelper.CreateFullRegistry(
			effects: new Dictionary<string, EffectDto>
			{
				["gain_energy"] = new()
				{
					Id = "gain_energy",
					Kind = EEffectKind.GainResource,
					Params = new Dictionary<string, object>
					{
						["resource"] = "Energy",
						["amount"] = 2,
					},
				},
			});
		var enemy = new EnemyUnit("e0", "slime", maxHp: 20);
		var player = new PlayerTeamState([character], sharedMaxHp: 10);
		var sim = new CombatSimulation(
			player,
			new EnemyTeamState([enemy]),
			new CombatRuleEngine([]),
			registry);
		var executor = new CombatEffectExecutor(registry, sim.Rules);
		var source = new CombatTargetRef(ECombatSide.Player, 0);

		executor.ExecuteEffectRef(new EffectRefDto { EffectId = "gain_energy" }, sim, source, [source]);

		Assert.That(character.AvailableEnergy, Is.EqualTo(3));
		Assert.That(character.CurrentEnergy, Is.EqualTo(1), "效果只灌可用池，不动当前能量");
	}
}
