using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

/// <summary>
/// 规格 §3.3：已标记卡牌的费用变化在玩家阶段内即时对账。
/// 新费用更低则退差额；更高且可用能量足够则补差；不够则整项自动取消并回退未确认。
/// </summary>
public static class QueuedCostReconciler
{
	/// <returns>因涨费不足而被自动取消标记的持有者槽位索引集合。</returns>
	public static IReadOnlySet<int> Reconcile(CombatSimulation simulation)
	{
		ArgumentNullException.ThrowIfNull(simulation);

		var autoCancelledHolders = new HashSet<int>();
		foreach (var entry in simulation.CardQueue.PeekAllOrdered().ToList())
		{
			if (!simulation.Definitions.Store.TryGetCard(entry.CardId, out var card))
				continue;
			if (card.CostType != ECostType.Energy)
				continue;

			var characters = simulation.PlayerTeam.Characters;
			if (entry.CharacterIndex < 0 || entry.CharacterIndex >= characters.Count)
				continue;

			var character = characters[entry.CharacterIndex];
			var currentCost = CardCostCalculator.Compute(simulation, entry.CharacterIndex, card);
			if (currentCost == entry.Paid)
				continue;

			if (currentCost < entry.Paid)
			{
				character.RefundAvailableEnergy(entry.Paid - currentCost);
				RepriceInPlace(simulation, entry, currentCost);
				continue;
			}

			if (character.TryConsumeAvailableEnergy(currentCost - entry.Paid))
			{
				RepriceInPlace(simulation, entry, currentCost);
				continue;
			}

			CombatStateMachine.CancelMarkAndRefund(simulation, entry);
			character.SetHasActed(false);
			autoCancelledHolders.Add(entry.CharacterIndex);
		}

		return autoCancelledHolders;
	}

	/// <summary>沿用原 <see cref="QueuedCardEntry.Sequence"/> 重新入队，保证执行顺序不受对账影响。</summary>
	private static void RepriceInPlace(CombatSimulation simulation, QueuedCardEntry entry, int newPaid)
	{
		simulation.CardQueue.TryRemove(candidate => candidate.Sequence == entry.Sequence, out _);
		simulation.CardQueue.Enqueue(entry with { Paid = newPaid });
	}
}
