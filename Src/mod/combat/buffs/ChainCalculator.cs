using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// 连携（乖离性 MA 式）：结算阶段开始时按出牌队列一次性统计各属性的"不同角色数"定档，
/// 档位作用于本回合全部该属性伤害/治疗卡——无次序、无首角色惩罚、无回溯。
/// 仅卡牌类型 Physics/Magical/Healing 适用；加成与 DamageDealtScale 同桶加算。
/// </summary>
public static class ChainCalculator
{
    public const float TwoChainScale = 0.25f;
    public const float ThreeChainScale = 0.5f;
    public const float FourChainScale = 1.0f;

    private static readonly EElement[] Elements = [EElement.Red, EElement.Blue, EElement.Green, EElement.Yellow];

    public static bool AppliesToCard(CardDto card) =>
        card.CardType is ECardType.Physics or ECardType.Magical or ECardType.Healing;

    /// <summary>
    /// 按结算队列统计各属性的不同角色数。只有连携适用的卡牌类型（Physics/Magical/Healing）
    /// 参与统计：控制/诅咒等非输出卡不把人头数堆进档位。chalux 被动2（连携注入红）：持有
    /// <see cref="BuiltinBuffTags.TraitChainInjectRed"/> 的角色打出的卡在统计上额外计入红属性
    /// （双属性卡 = 各属性 + 红各自计入）。
    /// </summary>
    public static Dictionary<EElement, int> CountDistinctCharacters(CombatSimulation simulation)
    {
        var byElement = new Dictionary<EElement, HashSet<int>>();
        foreach (var queued in simulation.CardQueue.PeekAllOrdered())
        {
            if (!simulation.Definitions.Store.TryGetCard(queued.CardId, out var card))
                continue;

            if (!AppliesToCard(card))
                continue;

            var flags = CardElementFlagsForCount(simulation, card, queued.CharacterIndex);
            foreach (var element in Elements)
            {
                if ((flags & (int)element) == 0)
                    continue;

                if (!byElement.TryGetValue(element, out var characters))
                {
                    characters = [];
                    byElement[element] = characters;
                }

                characters.Add(queued.CharacterIndex);
            }
        }

        return byElement.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Count);
    }

    /// <summary>结算某张卡时适用的连携加成：取该卡各属性（含被动2注入）中最高档；多属性卡取最优。</summary>
    public static float BonusForCard(
        IReadOnlyDictionary<EElement, int> counts,
        CardDto card,
        CharacterBattleInstance source)
    {
        if (!AppliesToCard(card))
            return 0f;

        var flags = CardElementFlagsForCount(source, card);
        var best = 0f;
        foreach (var element in Elements)
        {
            if ((flags & (int)element) == 0)
                continue;

            counts.TryGetValue(element, out var count);
            best = MathF.Max(best, TierScale(count));
        }

        return best;
    }

    public static float TierScale(int distinctCharacterCount) => distinctCharacterCount switch
    {
        >= 4 => FourChainScale,
        3 => ThreeChainScale,
        2 => TwoChainScale,
        _ => 0f,
    };

    private static int CardElementFlagsForCount(CombatSimulation simulation, CardDto card, int characterIndex)
    {
        if (characterIndex < 0 || characterIndex >= simulation.PlayerTeam.Characters.Count)
            return card.Element;

        return CardElementFlagsForCount(simulation.PlayerTeam.Characters[characterIndex], card);
    }

    private static int CardElementFlagsForCount(CharacterBattleInstance source, CardDto card) =>
        source.Buffs.HasTag(BuiltinBuffTags.TraitChainInjectRed)
            ? card.Element | (int)EElement.Red
            : card.Element;
}
