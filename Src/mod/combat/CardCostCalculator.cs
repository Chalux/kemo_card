using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat;

/// <summary>
/// 卡牌当前费用的唯一计算入口。v1 直接取定义值（仅 Energy 计费），
/// 未来 <c>costScaling</c> / 动态费用一律接入此处，队列对账（规格 §3.3）会自动跟随。
/// </summary>
public static class CardCostCalculator
{
    public static int Compute(CombatSimulation simulation, int characterIndex, CardDto card)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(card);

        return card.CostType == ECostType.Energy ? Math.Max(0, card.Cost) : 0;
    }
}