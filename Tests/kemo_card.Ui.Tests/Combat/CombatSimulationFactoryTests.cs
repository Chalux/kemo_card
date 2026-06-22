using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatSimulationFactoryTests
{
	[Test]
	public void Factory_merges_battle_and_run_rules_once()
	{
		var battle = new BattleDto
		{
			Id = "test_battle",
			CombatRuleIds = ["builtin.shared_hp_defeat"],
			Waves = [new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = "slime", Count = 1 }] }],
		};
		var registry = CombatTestHelper.CreateFullRegistry(
			cards: new Dictionary<string, CardDto>
			{
				[CombatSimulationTestBuilder.PartyHpCardId] = new()
				{
					Id = CombatSimulationTestBuilder.PartyHpCardId,
					Stats = new CardStatBlockDto { HpCap = 10 },
				},
			},
			enemies: new Dictionary<string, EnemyDto> { ["slime"] = new() { Id = "slime", MaxHp = 10 } },
			battles: new Dictionary<string, BattleDto> { ["test_battle"] = battle });

		var catalog = CombatRuleCatalog.CreateDefault();
		var party = CombatSimulationTestBuilder.CreateParty(count: 4, registry);
		var sim = CombatSimulationFactory.TryCreate(
			battle, party, registry, new HostRng(1, "combat"), runSeed: 1,
			runRuleIds: ["builtin.shared_hp_defeat"],
			scriptHost: null, modId: "test.mod", catalog, out var error);

		Assert.That(error, Is.Null);
		Assert.That(sim!.Rules.Rules, Has.Count.EqualTo(1));
	}
}
