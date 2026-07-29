using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>规格 §4.3 / §4.6：中途抽牌禁令与弃牌分通道。</summary>
[TestFixture]
public sealed class DiscardChannelTests
{
	private static readonly IReadOnlyList<CombatTargetRef> Self0 = [new CombatTargetRef(ECombatSide.Player, 0)];
	private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];

	#region 中途抽牌禁令

	[Test]
	public void Mid_battle_draw_effect_is_a_no_op_with_diagnostic()
	{
		using var sim = BuildDiscardSim(includeDrawAction: true);
		var character = sim.PlayerTeam.Characters[0];
		var handBefore = character.HandSlots.Count(s => !s.IsEmpty);
		var drawBefore = character.DrawPile.Count;

		sim.SetDiscardChannel(EDiscardChannel.Other);
		sim.EffectExecutor.ExecuteSkillActionRef(
			new SkillActionRefDto { ActionId = "action.draw" },
			sim,
			new CombatTargetRef(ECombatSide.Player, 0),
			Self0);

		Assert.That(character.HandSlots.Count(s => !s.IsEmpty), Is.EqualTo(handBefore));
		Assert.That(character.DrawPile.Count, Is.EqualTo(drawBefore));
		Assert.That(sim.BlockedMidDrawCount, Is.EqualTo(1));
	}

	[Test]
	public void ModifyDrawCount_still_adds_draw_modifiers()
	{
		using var sim = BuildDiscardSim(includeModifyDraw: true);
		var character = sim.PlayerTeam.Characters[0];

		sim.EffectExecutor.ExecuteSkillActionRef(
			new SkillActionRefDto { ActionId = "action.modify_draw" },
			sim,
			new CombatTargetRef(ECombatSide.Player, 0),
			Self0);

		Assert.That(character.ComputeDrawCount(), Is.EqualTo(3), "基准 1 + 最大增益 2");
		Assert.That(sim.BlockedMidDrawCount, Is.Zero);
	}

	#endregion

	#region ActiveSkill 通道

	[Test]
	public void ActiveSkill_discard_of_marked_card_cancels_refunds_and_unconfirms()
	{
		using var sim = BuildDiscardSim(includeDiscardAction: true, seed: 42);
		var character = sim.PlayerTeam.Characters[0];
		// 只留一张已标记牌，保证 ActiveSkill 随机弃牌必中标记。
		ClearUnmarkedHand(character);
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);
		Assert.That(character.HasActed, Is.True);
		Assert.That(character.AvailableEnergy, Is.EqualTo(4));
		var markedId = character.HandSlots[0].RuntimeInstanceId!;

		sim.SetDiscardChannel(EDiscardChannel.ActiveSkill);
		sim.EffectExecutor.ExecuteSkillActionRef(
			new SkillActionRefDto { ActionId = "action.discard" },
			sim,
			new CombatTargetRef(ECombatSide.Player, 0),
			Self0);

		Assert.That(sim.CardQueue.Count, Is.Zero);
		Assert.That(character.HasActed, Is.False, "弃到已标记 → 回退未确认");
		Assert.That(character.AvailableEnergy, Is.EqualTo(5), "退还 paid");
		Assert.That(character.HandSlots[0].IsEmpty, Is.True);
		Assert.That(character.Graveyard.Any(c => c.RuntimeInstanceId == markedId), Is.True);
	}

	#endregion

	#region CardExecution / Other 通道

	[Test]
	public void CardExecution_discard_only_picks_unmarked_and_is_reproducible()
	{
		using var simA = BuildDiscardSim(includeDiscardAction: true, seed: 99, handSize: 4);
		using var simB = BuildDiscardSim(includeDiscardAction: true, seed: 99, handSize: 4);
		MarkFirstSlot(simA);
		MarkFirstSlot(simB);
		var markedA = simA.PlayerTeam.Characters[0].HandSlots[0].RuntimeInstanceId!;
		var markedB = simB.PlayerTeam.Characters[0].HandSlots[0].RuntimeInstanceId!;

		DiscardOnce(simA, EDiscardChannel.CardExecution);
		DiscardOnce(simB, EDiscardChannel.CardExecution);

		var graveA = simA.PlayerTeam.Characters[0].Graveyard.Select(c => c.RuntimeInstanceId).ToArray();
		var graveB = simB.PlayerTeam.Characters[0].Graveyard.Select(c => c.RuntimeInstanceId).ToArray();
		Assert.That(graveA, Is.EqualTo(graveB), "同种子可复现");
		Assert.That(graveA, Does.Not.Contain(markedA));
		Assert.That(simA.PlayerTeam.Characters[0].HandSlots[0].RuntimeInstanceId, Is.EqualTo(markedA));
		Assert.That(simA.CardQueue.Count, Is.EqualTo(1), "已标记牌受队列保护");
		Assert.That(simB.PlayerTeam.Characters[0].HandSlots[0].RuntimeInstanceId, Is.EqualTo(markedB));
	}

	[Test]
	public void Other_channel_empty_unmarked_pool_discards_zero_without_touching_marks()
	{
		using var sim = BuildDiscardSim(includeDiscardAction: true, handSize: 1);
		var character = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);
		var paid = character.AvailableEnergy;

		DiscardOnce(sim, EDiscardChannel.Other);
		DiscardOnce(sim, EDiscardChannel.Other);

		Assert.That(character.Graveyard.Count, Is.Zero);
		Assert.That(sim.CardQueue.Count, Is.EqualTo(1));
		Assert.That(character.HasActed, Is.True, "池空不回滚确认");
		Assert.That(character.AvailableEnergy, Is.EqualTo(paid));
		Assert.That(character.HandSlots[0].IsMarked, Is.True);
	}

	[Test]
	public void Soft_fail_does_not_stop_remaining_actions_in_chain()
	{
		using var sim = BuildDiscardSim(includeDiscardThenGain: true, handSize: 0);
		var character = sim.PlayerTeam.Characters[0];
		character.RefillAvailableEnergy();
		var energyBefore = character.AvailableEnergy;

		sim.SetDiscardChannel(EDiscardChannel.Other);
		sim.EffectExecutor.ExecuteSkillActionRef(
			new SkillActionRefDto { ActionId = "action.discard_then_gain" },
			sim,
			new CombatTargetRef(ECombatSide.Player, 0),
			Self0);

		Assert.That(character.Graveyard.Count, Is.Zero);
		Assert.That(character.AvailableEnergy, Is.EqualTo(energyBefore + 2), "弃牌软失败后后续动作仍执行");
	}

	#endregion

	#region 辅助

	private static void DiscardOnce(CombatSimulation sim, EDiscardChannel channel)
	{
		sim.SetDiscardChannel(channel);
		sim.EffectExecutor.ExecuteSkillActionRef(
			new SkillActionRefDto { ActionId = "action.discard" },
			sim,
			new CombatTargetRef(ECombatSide.Player, 0),
			Self0);
	}

	private static void MarkFirstSlot(CombatSimulation sim)
	{
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
	}

	private static void ClearUnmarkedHand(CharacterBattleInstance character)
	{
		foreach (var slot in character.HandSlots.Skip(1))
		{
			if (slot.IsEmpty)
				continue;
			character.MoveHandCardToGraveyard(slot.RuntimeInstanceId!);
		}
	}

	private static CombatSimulation BuildDiscardSim(
		bool includeDrawAction = false,
		bool includeDiscardAction = false,
		bool includeModifyDraw = false,
		bool includeDiscardThenGain = false,
		int seed = 1,
		int handSize = 3)
	{
		var skillActions = new Dictionary<string, SkillActionDto>(StringComparer.Ordinal);
		if (includeDrawAction)
		{
			skillActions["action.draw"] = new()
			{
				Id = "action.draw",
				Kind = ESkillActionKind.Draw,
				Params = new Dictionary<string, object> { ["count"] = 2 },
			};
		}

		if (includeDiscardAction)
		{
			skillActions["action.discard"] = new()
			{
				Id = "action.discard",
				Kind = ESkillActionKind.Discard,
				Params = new Dictionary<string, object> { ["count"] = 1 },
			};
		}

		if (includeModifyDraw)
		{
			skillActions["action.modify_draw"] = new()
			{
				Id = "action.modify_draw",
				Kind = ESkillActionKind.ModifyDrawCount,
				Params = new Dictionary<string, object> { ["amount"] = 2 },
			};
		}

		if (includeDiscardThenGain)
		{
			skillActions["action.discard"] = new()
			{
				Id = "action.discard",
				Kind = ESkillActionKind.Discard,
				Params = new Dictionary<string, object> { ["count"] = 1 },
			};
			skillActions["action.gain"] = new()
			{
				Id = "action.gain",
				Kind = ESkillActionKind.GainResource,
				Params = new Dictionary<string, object> { ["amount"] = 2 },
			};
			skillActions["action.discard_then_gain"] = new()
			{
				Id = "action.discard_then_gain",
				Kind = ESkillActionKind.ChainActions,
				ActionRefs =
				[
					new SkillActionRefDto { ActionId = "action.discard" },
					new SkillActionRefDto { ActionId = "action.gain" },
				],
			};
		}

		var registry = CombatTestHelper.CreateFullRegistry(
			cards: new Dictionary<string, CardDto>
			{
				[CombatSimulationTestBuilder.MarkableCardId] = new()
				{
					Id = CombatSimulationTestBuilder.MarkableCardId,
					CostType = ECostType.Energy,
					Cost = 1,
					Priority = 100,
					TargetSide = ETargetSide.Enemy,
					TargetScope = ETargetScope.Single,
					TargetCount = 1,
					SkillRefs = [new SkillRefDto { SkillId = "skill.markable" }],
				},
			},
			skills: new Dictionary<string, SkillDto>
			{
				["skill.markable"] = new() { Id = "skill.markable" },
			},
			skillActions: skillActions);

		var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
		{
			[AttributeIds.MaxHealth] = 10f,
			[AttributeIds.MaxEnergy] = 5f,
			[AttributeIds.InitialEnergy] = 5f,
		};
		var characters = Enumerable.Range(0, 4)
			.Select(i => CharacterBattleInstance.CreateForTests(
				$"c{i}",
				attrs,
				Enumerable.Range(0, Math.Max(handSize, 1))
					.Select(slot => new CardRuntimeEntry(
						CombatSimulationTestBuilder.MarkableCardId,
						$"rt-c{i}-{slot}"))))
			.ToArray();
		foreach (var character in characters)
		{
			if (handSize > 0)
				character.DrawCards(handSize);
			character.RefillAvailableEnergy();
		}

		return new CombatSimulation(
			new PlayerTeamState(characters, sharedMaxHp: 40),
			new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]),
			new CombatRuleEngine([]),
			registry,
			initialPhase: ECombatPhase.Player,
			runSeed: seed);
	}

	#endregion
}
