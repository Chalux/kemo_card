using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat;

/// <summary>
/// 卡牌当前费用的唯一计算入口。v1 直接取定义值（仅 Energy 计费），
/// 未来 <c>costScaling</c> / 动态费用一律接入此处，队列对账（规格 §3.3）会自动跟随。
/// </summary>
/// <remarks>
/// 2026-09-21 起支持<b>槽位免费</b>：手牌槽上挂有 <see cref="BuiltinBuffTags.SlotFreeCost"/> 的 buff 时，
/// 该槽当前那张牌的费用视为 0（莱因哈特被动6「随机一张手牌费用变为 0」）。
/// 由于标记入队后牌仍留在槽内，判据用 <c>runtimeInstanceId</c> 反查槽位——这样队列对账
/// （<c>QueuedCostReconciler</c>）也能在同一口径下退还/补差，而不需要给队列条目加字段。
/// </remarks>
public static class CardCostCalculator
{
    public static int Compute(
        CombatSimulation simulation,
        int characterIndex,
        CardDto card,
        string? runtimeInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(card);

        if (card.CostType != ECostType.Energy)
            return 0;

        return IsSlotFreeCost(simulation, characterIndex, runtimeInstanceId)
            ? 0
            : Math.Max(0, card.Cost);
    }

    /// <summary>该牌所在的手牌槽是否带"费用为 0"的槽位 buff。</summary>
    private static bool IsSlotFreeCost(CombatSimulation simulation, int characterIndex, string? runtimeInstanceId)
    {
        if (string.IsNullOrWhiteSpace(runtimeInstanceId))
            return false;

        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return false;

        foreach (var slot in characters[characterIndex].HandSlots)
        {
            if (string.Equals(slot.RuntimeInstanceId, runtimeInstanceId, StringComparison.Ordinal))
                return slot.Buffs.HasTag(BuiltinBuffTags.SlotFreeCost);
        }

        return false;
    }
}
