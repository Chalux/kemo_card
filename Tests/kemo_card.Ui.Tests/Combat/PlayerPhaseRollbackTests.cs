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
/// 规格 §2.3：玩家阶段目标丢失 → 对相关已标记牌取消标记（退 <c>paid</c>）并把持有者回退未确认。
/// 相关 = 目标集合包含已丢失单位；同角色其它仍合法的标记保留。
/// </summary>
[TestFixture]
public sealed class PlayerPhaseRollbackTests
{
	private const string SingleCardId = "card.aim";
	private const string MultiCardId = "card.sweep";

	private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];
	private static readonly IReadOnlyList<CombatTargetRef> Enemy1 = [new CombatTargetRef(ECombatSide.Enemy, 1)];
	private static readonly IReadOnlyList<CombatTargetRef> Enemy0And1 =
	[
		new CombatTargetRef(ECombatSide.Enemy, 0),
		new CombatTargetRef(ECombatSide.Enemy, 1),
	];

	[Test]
	public void Target_loss_cancels_the_related_mark_and_refunds_paid()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		var energyAfterMark = holder.AvailableEnergy;

		KillEnemyZeroWithActiveSkill(sim);

		Assert.That(sim.CardQueue.Count, Is.Zero, "目标丢失后相关标记整张取消");
		Assert.That(holder.HandSlots[0].IsMarked, Is.False);
		Assert.That(holder.HandSlots[0].IsEmpty, Is.False, "取消标记不使卡牌离手");
		Assert.That(holder.AvailableEnergy, Is.EqualTo(energyAfterMark + 1), "退还 paid");
	}

	[Test]
	public void Target_loss_keeps_the_still_legal_marks_of_the_same_character()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new PlayCardCommand(0, 1, Enemy1)).Success, Is.True);

		KillEnemyZeroWithActiveSkill(sim);

		Assert.That(holder.HandSlots[0].IsMarked, Is.False, "指向已丢失单位的标记被取消");
		Assert.That(holder.HandSlots[1].IsMarked, Is.True, "同角色其它合法标记保留");
		Assert.That(sim.CardQueue.Count, Is.EqualTo(1));
	}

	[Test]
	public void Multi_target_mark_is_cancelled_entirely_when_it_contains_a_lost_unit()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 3, Enemy0And1)).Success, Is.True);

		KillEnemyZeroWithActiveSkill(sim);

		Assert.That(sim.CardQueue.Count, Is.Zero, "多选牌只要含丢失单位就整张取消");
		Assert.That(holder.HandSlots[3].IsMarked, Is.False);
	}

	[Test]
	public void Target_loss_rolls_the_holder_back_to_unconfirmed()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);

		KillEnemyZeroWithActiveSkill(sim);

		Assert.That(holder.HasActed, Is.False);
	}

	[Test]
	public void Cast_without_any_target_loss_leaves_marks_and_confirmation_untouched()
	{
		using var sim = Build();
		var holder = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy1)).Success, Is.True);
		Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);

		// 敌人 1 血量足够高，主动技打不死它。
		Assert.That(sim.TryApply(new CastActiveSkillCommand(2, Enemy1)).Success, Is.True);

		Assert.That(sim.CardQueue.Count, Is.EqualTo(1));
		Assert.That(holder.HandSlots[0].IsMarked, Is.True);
		Assert.That(holder.HasActed, Is.True);
	}

	[Test]
	public void Caster_marks_pointing_at_the_lost_unit_are_cancelled_too()
	{
		using var sim = Build();
		var caster = sim.PlayerTeam.Characters[2];
		Assert.That(sim.TryApply(new PlayCardCommand(2, 0, Enemy0)).Success, Is.True);

		KillEnemyZeroWithActiveSkill(sim);

		Assert.That(caster.HandSlots[0].IsMarked, Is.False);
		Assert.That(caster.HasActed, Is.False);
	}

	private static void KillEnemyZeroWithActiveSkill(CombatSimulation sim)
	{
		var result = sim.TryApply(new CastActiveSkillCommand(2, Enemy0));
		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(sim.EnemyTeam.Enemies[0].IsAlive, Is.False, "主动技应当击杀敌人 0");
	}

	private static CombatSimulation Build()
	{
		var registry = CombatTestHelper.CreateFullRegistry(
			cards: new Dictionary<string, CardDto>
			{
				[SingleCardId] = new()
				{
					Id = SingleCardId,
					CostType = ECostType.Energy,
					Cost = 1,
					Priority = 100,
					TargetSide = ETargetSide.Enemy,
					TargetScope = ETargetScope.Single,
					TargetCount = 1,
					SkillRefs = [new SkillRefDto { SkillId = "skill.poke" }],
				},
				[MultiCardId] = new()
				{
					Id = MultiCardId,
					CostType = ECostType.Energy,
					Cost = 1,
					Priority = 100,
					TargetSide = ETargetSide.Enemy,
					TargetScope = ETargetScope.All,
					TargetCount = 2,
					SkillRefs = [new SkillRefDto { SkillId = "skill.poke" }],
				},
			},
			skills: new Dictionary<string, SkillDto>
			{
				["skill.poke"] = new()
				{
					Id = "skill.poke",
					EffectRefs = [new EffectRefDto { EffectId = "effect.poke" }],
				},
				["skill.execute"] = new()
				{
					Id = "skill.execute",
					EffectRefs = [new EffectRefDto { EffectId = "effect.execute" }],
					TargetOverride = new TargetSpecDto
					{
						Side = ETargetSide.Enemy,
						Scope = ETargetScope.Single,
						TargetCount = 1,
					},
				},
			},
			effects: new Dictionary<string, EffectDto>
			{
				["effect.poke"] = new()
				{
					Id = "effect.poke",
					Kind = EEffectKind.Damage,
					Params = new Dictionary<string, object> { ["amount"] = 1 },
				},
				["effect.execute"] = new()
				{
					Id = "effect.execute",
					Kind = EEffectKind.Damage,
					Params = new Dictionary<string, object> { ["amount"] = 999 },
				},
			});

		var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
		{
			[AttributeIds.MaxHealth] = 10f,
			[AttributeIds.InitialEnergy] = 5f,
			[AttributeIds.MaxEnergy] = 5f,
		};

		// 抽牌从列表尾部取，因此手牌槽 0–2 为单体牌、3–4 为多选牌。
		var pile = new[] { MultiCardId, MultiCardId, SingleCardId, SingleCardId, SingleCardId };
		var characters = Enumerable.Range(0, 4)
			.Select(i => CharacterBattleInstance.CreateForTests(
				$"c{i}",
				attributes,
				pile.Select((cardId, slot) => new CardRuntimeEntry(cardId, $"rt-c{i}-{slot}")),
				activeSkillChain: [new ActiveSkillChainEntryDto { SkillId = "skill.execute", Cooldown = 1 }]))
			.ToArray();
		foreach (var character in characters)
		{
			character.DrawCards(CombatConstants.HandSlotCount);
			character.RefillAvailableEnergy();
			character.TickSkillCounter();
		}

		return new CombatSimulation(
			new PlayerTeamState(characters, sharedMaxHp: 40),
			new EnemyTeamState(
			[
				new EnemyUnit("e0", "slime", maxHp: 10),
				new EnemyUnit("e1", "slime", maxHp: 5000),
				new EnemyUnit("e2", "slime", maxHp: 5000),
			]),
			new CombatRuleEngine([]),
			registry,
			initialPhase: ECombatPhase.Player);
	}
}
