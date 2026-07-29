using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 规格 §2.4：执行阶段目标失效——单体按 <c>RetargetPolicy</c> 重选（缺省 Run RNG 均匀），
/// 多目标对剩余合法子集结算；子集为空即空放。规格 §2.1：结算后（含空放）卡牌进弃牌堆、槽位空出。
/// </summary>
[TestFixture]
public sealed class CardExecutionRetargetTests
{
	private const string CardId = "card.hit";
	private const int Damage = 5;

	private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

	#region 单体重定向

	[Test]
	public void Single_target_default_policy_retargets_into_the_legal_pool()
	{
		using var sim = Build();
		MarkCard(sim, slotIndex: 0, [Enemy(0)]);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);

		RunCardExecution(sim);

		var damaged = DamagedEnemyIndices(sim);
		Assert.That(damaged, Has.Count.EqualTo(1), "单体牌只应结算一次");
		Assert.That(damaged[0], Is.AnyOf(1, 2), "重选目标必须来自重算后的合法池");
	}

	[Test]
	public void Single_target_retarget_is_reproducible_for_the_same_run_seed()
	{
		Assert.That(RetargetPickWithSeed(7), Is.EqualTo(RetargetPickWithSeed(7)));
	}

	[Test]
	public void Single_target_retarget_is_not_pinned_to_the_first_legal_target()
	{
		var picks = Enumerable.Range(1, 40).Select(RetargetPickWithSeed).Distinct().ToList();

		Assert.That(picks, Has.Count.GreaterThan(1), "缺省策略须走 Run RNG 均匀重选，而非固定取合法池第一个");
	}

	[Test]
	public void Single_target_skip_policy_fires_blank()
	{
		using var sim = Build(retargetPolicy: ERetargetPolicy.Skip);
		MarkCard(sim, slotIndex: 0, [Enemy(0)]);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);

		RunCardExecution(sim);

		Assert.That(DamagedEnemyIndices(sim), Is.Empty, "Skip 策略不重选，直接空放");
	}

	#endregion

	#region 多目标子集

	[Test]
	public void Multi_target_settles_on_the_remaining_legal_subset()
	{
		using var sim = Build(scope: ETargetScope.All, targetCount: 3);
		MarkCard(sim, slotIndex: 0, [Enemy(0), Enemy(1), Enemy(2)]);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);

		RunCardExecution(sim);

		Assert.That(DamagedEnemyIndices(sim), Is.EqualTo(new[] { 1, 2 }), "去掉非法目标后对剩余子集结算");
	}

	[Test]
	public void Multi_target_with_an_empty_subset_fires_blank()
	{
		using var sim = Build(scope: ETargetScope.All, targetCount: 3);
		MarkCard(sim, slotIndex: 0, [Enemy(0), Enemy(1)]);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);
		sim.EnemyTeam.Enemies[1].ApplyDamage(999);

		RunCardExecution(sim);

		Assert.That(DamagedEnemyIndices(sim), Is.Empty);
	}

	#endregion

	#region 结算后进弃牌堆

	[Test]
	public void Executed_card_moves_to_the_graveyard_and_frees_the_hand_slot()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		MarkCard(sim, slotIndex: 0, [Enemy(1)]);
		var runtimeInstanceId = holder.HandSlots[0].RuntimeInstanceId;

		RunCardExecution(sim);

		Assert.That(holder.HandSlots[0].IsEmpty, Is.True, "结算后手牌槽空出");
		Assert.That(holder.HandSlots[0].IsMarked, Is.False);
		Assert.That(
			holder.Graveyard.Select(entry => entry.RuntimeInstanceId),
			Contains.Item(runtimeInstanceId),
			"结算后进持有者弃牌堆");
	}

	[Test]
	public void Blank_fire_card_also_moves_to_the_graveyard()
	{
		using var sim = Build(retargetPolicy: ERetargetPolicy.Skip);
		var holder = sim.PlayerTeam.Characters[0];
		MarkCard(sim, slotIndex: 0, [Enemy(0)]);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);

		RunCardExecution(sim);

		Assert.That(holder.HandSlots[0].IsEmpty, Is.True);
		Assert.That(holder.Graveyard, Has.Count.EqualTo(1), "空放也进弃牌堆");
	}

	[Test]
	public void Unmarked_cards_stay_in_hand_across_the_execution_phase()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		MarkCard(sim, slotIndex: 0, [Enemy(1)]);

		RunCardExecution(sim);

		Assert.That(holder.HandSlots[1].IsEmpty, Is.False, "未标记手牌跨回合保留");
		Assert.That(holder.Graveyard, Has.Count.EqualTo(1));
	}

	[Test]
	public void Blank_fire_does_not_roll_back_has_acted()
	{
		using var sim = Build(retargetPolicy: ERetargetPolicy.Skip);
		var holder = sim.PlayerTeam.Characters[0];
		MarkCard(sim, slotIndex: 0, [Enemy(0)]);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);
		holder.SetHasActed(true);

		RunCardExecution(sim);

		Assert.That(holder.HasActed, Is.True, "执行阶段的空放不回滚已行动");
	}

	[Test]
	public void Card_with_an_unknown_definition_still_leaves_the_hand()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		MarkCard(sim, slotIndex: 0, [Enemy(1)]);
		CombatTestHelper.RebuildInto(sim.Definitions);

		RunCardExecution(sim);

		Assert.That(holder.HandSlots[0].IsEmpty, Is.True);
		Assert.That(holder.Graveyard, Has.Count.EqualTo(1));
	}

	#endregion

	private static int RetargetPickWithSeed(int seed)
	{
		using var sim = Build(seed: seed);
		MarkCard(sim, slotIndex: 0, [Enemy(0)]);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);

		RunCardExecution(sim);

		return DamagedEnemyIndices(sim).Single();
	}

	private static void MarkCard(CombatSimulation sim, int slotIndex, IReadOnlyList<CombatTargetRef> targets)
	{
		var result = sim.TryApply(new PlayCardCommand(0, slotIndex, targets));
		Assert.That(result.Success, Is.True, result.Error);
	}

	private static void RunCardExecution(CombatSimulation sim)
	{
		sim.TransitionTo(ECombatPhase.CardExecution);
		sim.AdvancePhase();
	}

	private static List<int> DamagedEnemyIndices(CombatSimulation sim)
	{
		var damaged = new List<int>();
		for (var i = 0; i < sim.EnemyTeam.Enemies.Count; i++)
		{
			var enemy = sim.EnemyTeam.Enemies[i];
			if (enemy.IsAlive && enemy.CurrentHp <= enemy.MaxHp - Damage)
				damaged.Add(i);
		}

		return damaged;
	}

	private static CombatSimulation Build(
		int seed = 1,
		ETargetScope scope = ETargetScope.Single,
		int targetCount = 1,
		ERetargetPolicy retargetPolicy = ERetargetPolicy.Default,
		int enemyCount = 3)
	{
		var registry = CombatTestHelper.CreateFullRegistry(
			cards: new Dictionary<string, CardDto>
			{
				[CardId] = new()
				{
					Id = CardId,
					CostType = ECostType.None,
					Priority = 100,
					TargetSide = ETargetSide.Enemy,
					TargetScope = scope,
					TargetCount = targetCount,
					RetargetPolicy = retargetPolicy,
					SkillRefs = [new SkillRefDto { SkillId = "skill.hit" }],
				},
			},
			skills: new Dictionary<string, SkillDto>
			{
				["skill.hit"] = new()
				{
					Id = "skill.hit",
					EffectRefs = [new EffectRefDto { EffectId = "effect.hit" }],
				},
			},
			effects: new Dictionary<string, EffectDto>
			{
				["effect.hit"] = new()
				{
					Id = "effect.hit",
					Kind = EEffectKind.Damage,
					Params = new Dictionary<string, object> { ["amount"] = Damage },
				},
			});

		var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
		{
			[AttributeIds.MaxHealth] = 10f,
			[AttributeIds.InitialEnergy] = 0f,
			[AttributeIds.MaxEnergy] = 0f,
		};
		var characters = Enumerable.Range(0, 4)
			.Select(i => CharacterBattleInstance.CreateForTests(
				$"c{i}",
				attributes,
				Enumerable.Range(0, 3).Select(slot => new CardRuntimeEntry(CardId, $"rt-c{i}-{slot}"))))
			.ToArray();
		foreach (var character in characters)
			character.DrawCards(3);

		return new CombatSimulation(
			new PlayerTeamState(characters, sharedMaxHp: 40),
			new EnemyTeamState(
				[.. Enumerable.Range(0, enemyCount).Select(i => new EnemyUnit($"e{i}", "slime", maxHp: 100))]),
			new CombatRuleEngine([]),
			registry,
			initialPhase: ECombatPhase.Player,
			runSeed: seed);
	}
}
