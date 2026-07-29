using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>规格 §2.2 / §4.1：出牌 = 手牌标记入队（不离手）；§2.2 确认锁定与显式取消确认。</summary>
[TestFixture]
public sealed class HandMarkQueueTests
{
	private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];

	#region 标记入队

	[Test]
	public void PlayCard_marks_slot_and_keeps_card_in_hand()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5);
		var character = sim.PlayerTeam.Characters[0];

		var result = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));

		Assert.That(result.Success, Is.True, result.Error);
		var slot = character.HandSlots[0];
		Assert.That(slot.IsEmpty, Is.False, "入队不离手");
		Assert.That(slot.IsMarked, Is.True);
		Assert.That(sim.CardQueue.Count, Is.EqualTo(1));

		var entry = sim.CardQueue.PeekAllOrdered().Single();
		Assert.That(entry.RuntimeInstanceId, Is.EqualTo(slot.RuntimeInstanceId));
		Assert.That(entry.Paid, Is.EqualTo(2));
		Assert.That(slot.MarkedSequence, Is.EqualTo(entry.Sequence));
		Assert.That(character.AvailableEnergy, Is.EqualTo(3), "入队实扣可用能量");
	}

	[Test]
	public void PlayCard_with_no_cost_type_records_zero_paid()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(
			costType: ECostType.None,
			cost: 3,
			energy: 5);
		var character = sim.PlayerTeam.Characters[0];

		var result = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));

		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(sim.CardQueue.PeekAllOrdered().Single().Paid, Is.Zero);
		Assert.That(character.AvailableEnergy, Is.EqualTo(5), "None 费用不扣能量");
	}

	[Test]
	[TestCase(ECostType.Health)]
	[TestCase(ECostType.Gold)]
	[TestCase(ECostType.Discard)]
	[TestCase(ECostType.X)]
	public void PlayCard_rejects_cost_types_not_implemented_in_v1(ECostType costType)
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(costType: costType, cost: 1, energy: 5);

		var result = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));

		Assert.That(result.Success, Is.False);
		Assert.That(sim.CardQueue.Count, Is.Zero);
		Assert.That(sim.PlayerTeam.Characters[0].HandSlots[0].IsMarked, Is.False);
	}

	[Test]
	public void PlayCard_rejects_when_available_energy_is_insufficient()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 3, energy: 2);

		var result = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));

		Assert.That(result.Success, Is.False);
		Assert.That(sim.CardQueue.Count, Is.Zero);
		Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(2));
	}

	[Test]
	public void PlayCard_rejects_already_marked_slot()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);

		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		var second = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));

		Assert.That(second.Success, Is.False);
		Assert.That(sim.CardQueue.Count, Is.EqualTo(1));
		Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(4), "拒绝时不重复扣费");
	}

	[Test]
	public void PlayCard_rejects_empty_slot()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(handSize: 1);

		var result = sim.TryApply(new PlayCardCommand(0, 4, Enemy0));

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public void Marking_multiple_cards_allocates_increasing_sequences()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);

		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new PlayCardCommand(0, 1, Enemy0)).Success, Is.True);

		var sequences = sim.CardQueue.PeekAllOrdered().Select(e => e.Sequence).ToArray();
		Assert.That(sequences, Is.Ordered.Ascending);
		Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(3));
	}

	#endregion

	#region 取消标记

	[Test]
	public void CancelQueuedCard_refunds_paid_and_unmarks_but_keeps_card_in_hand()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5);
		var character = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		var sequence = sim.CardQueue.PeekAllOrdered().Single().Sequence;

		var result = sim.TryApply(new CancelQueuedCardCommand(0, sequence));

		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(sim.CardQueue.Count, Is.Zero);
		Assert.That(character.AvailableEnergy, Is.EqualTo(5), "退还 paid");
		Assert.That(character.HandSlots[0].IsEmpty, Is.False, "取消标记只去标，牌仍在手");
		Assert.That(character.HandSlots[0].IsMarked, Is.False);
	}

	[Test]
	public void CancelQueuedCard_does_not_touch_confirmation_state()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
		var character = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);

		Assert.That(sim.TryApply(new CancelQueuedCardCommand(0)).Success, Is.True);

		Assert.That(character.HasActed, Is.False, "本就未确认，取消标记不改确认态");
	}

	[Test]
	public void CancelQueuedCard_keeps_other_marks_of_same_character()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
		var character = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new PlayCardCommand(0, 1, Enemy0)).Success, Is.True);
		var firstRuntimeId = character.HandSlots[0].RuntimeInstanceId;

		Assert.That(
			sim.TryApply(new CancelQueuedCardCommand(0, CardRuntimeInstanceId: firstRuntimeId)).Success,
			Is.True);

		Assert.That(character.HandSlots[0].IsMarked, Is.False);
		Assert.That(character.HandSlots[1].IsMarked, Is.True);
		Assert.That(sim.CardQueue.Count, Is.EqualTo(1));
		Assert.That(character.AvailableEnergy, Is.EqualTo(4));
	}

	[Test]
	public void CancelQueuedCard_fails_when_nothing_matches()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();

		var result = sim.TryApply(new CancelQueuedCardCommand(0, QueueSequence: 999));

		Assert.That(result.Success, Is.False);
	}

	#endregion

	#region 确认锁定与取消确认

	[Test]
	public void Confirmed_character_cannot_play_more_cards()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
		Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);

		var result = sim.TryApply(new PlayCardCommand(0, 0, Enemy0));

		Assert.That(result.Success, Is.False);
		Assert.That(sim.CardQueue.Count, Is.Zero);
	}

	[Test]
	public void Confirmed_character_cannot_cancel_marks()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
		var character = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);

		var result = sim.TryApply(new CancelQueuedCardCommand(0));

		Assert.That(result.Success, Is.False, "已确认锁定队列，须先显式取消确认");
		Assert.That(sim.CardQueue.Count, Is.EqualTo(1));
		Assert.That(character.AvailableEnergy, Is.EqualTo(4), "拒绝时不退费");
	}

	[Test]
	public void UnconfirmCharacter_unlocks_editing_without_clearing_existing_marks()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
		var character = sim.PlayerTeam.Characters[0];
		Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
		Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);

		var result = sim.TryApply(new UnconfirmCharacterCommand(0));

		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(character.HasActed, Is.False);
		Assert.That(sim.CardQueue.Count, Is.EqualTo(1), "取消确认不清已标记项");
		Assert.That(character.HandSlots[0].IsMarked, Is.True);
		Assert.That(sim.TryApply(new PlayCardCommand(0, 1, Enemy0)).Success, Is.True, "解锁后可继续编辑");
	}

	[Test]
	public void UnconfirmCharacter_rejects_invalid_index()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();

		Assert.That(sim.TryApply(new UnconfirmCharacterCommand(9)).Success, Is.False);
	}

	[Test]
	public void Empty_confirmation_is_allowed_and_four_confirmations_enter_card_execution()
	{
		using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);

		for (var index = 0; index < 4; index++)
			Assert.That(sim.TryApply(new ConfirmCharacterCommand(index)).Success, Is.True);

		Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
		Assert.That(sim.CardQueue.Count, Is.Zero, "零标记也可确认");
	}

	#endregion
}
