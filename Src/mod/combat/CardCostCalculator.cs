using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

/// <summary>计算打出卡牌所需的能量。</summary>
public static class CardCostCalculator
{
    public static int Compute(CardDto card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.CostType == ECostType.Energy ? Math.Max(0, card.Cost) : 0;
    }
}
