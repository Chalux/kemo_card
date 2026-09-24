using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// 连携（乖离性 MA 式）：结算阶段开始时按出牌队列一次性统计各属性的"不同角色数"定档，
/// 档位作用于本回合全部该属性伤害/治疗卡——无次序、无首角色惩罚、无回溯。
/// 加成与 DamageDealtScale 同桶加算。
/// </summary>
/// <remarks>
/// 统计侧与加成侧口径<b>不同</b>（2026-09-21 修正）：统计侧统计队列里的**所有**卡，
/// Support / Curse 等非输出卡同样把它打出的角色计入人头（否则队友一张增益卡就白出，
/// 档位无法反映"这一回合有多少人参与了该属性"）；加成侧仍只作用于
/// Physics / Magical / Healing —— 见 <see cref="AppliesToCard"/>，它只被
/// <see cref="BonusForCard"/> 使用，<see cref="CountDistinctCharacters"/> 不再用它过滤。
/// </remarks>
public static class ChainCalculator
{
    public const float TwoChainScale = 0.25f;
    public const float ThreeChainScale = 0.5f;
    public const float FourChainScale = 1.0f;

    private static readonly EElement[] Elements = [EElement.Red, EElement.Blue, EElement.Green, EElement.Yellow];

    /// <summary>
    /// 连携加成是否作用于该卡（<b>加成侧</b>规则，只被 <see cref="BonusForCard"/> 使用）：
    /// 只有 Physics / Magical / Healing 吃加成，控制/诅咒等非输出卡即使人头堆到了档位也不吃。
    /// </summary>
    public static bool AppliesToCard(CardDto card) =>
        card.CardType is ECardType.Physics or ECardType.Magical or ECardType.Healing;

    /// <summary>
    /// 按结算队列统计各属性的不同角色数。<b>队列里的每一张卡都参与统计</b>（与卡牌类型无关）：
    /// 只要该卡带某属性，打出它的角色就为该属性贡献 1 人头（同一角色多张只计 1 人）。
    /// chalux 被动2（连携注入红）：持有 <see cref="BuiltinBuffTags.TraitChainInjectRed"/> 的角色
    /// 打出的卡在统计上额外计入红属性（双属性卡 = 各属性 + 红各自计入）。
    /// 加成是否真的落到某张卡上由 <see cref="BonusForCard"/> 判定。
    /// </summary>
    public static Dictionary<EElement, int> CountDistinctCharacters(CombatSimulation simulation)
    {
        var byElement = new Dictionary<EElement, HashSet<int>>();
        foreach (var queued in simulation.CardQueue.PeekAllOrdered())
        {
            if (!simulation.Definitions.Store.TryGetCard(queued.CardId, out var card))
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

    /// <summary>
    /// 该卡参与连携统计时的属性位：基础属性之外，被动可注入额外属性。
    /// <list type="bullet">
    /// <item><c>trait.chain_inject_red</c>（chalux 被动2）：打出的卡额外计入红属性。</item>
    /// <item><c>trait.chain_yellow_counts_blue</c>（冯·诺依曼 被动5）：含黄属性的卡额外计入<b>蓝属性</b>
    /// （"打出蓝属性时计算连携也会计入黄属性卡牌"，配 <c>applyScope: AllAllies</c> 即全队生效）。</item>
    /// </list>
    /// </summary>
    private static int CardElementFlagsForCount(CharacterBattleInstance source, CardDto card)
    {
        var flags = card.Element;
        if (source.Buffs.HasTag(BuiltinBuffTags.TraitChainInjectRed))
            flags |= (int)EElement.Red;
        if (source.Buffs.HasTag(BuiltinBuffTags.TraitChainYellowCountsBlue) &&
            (card.Element & (int)EElement.Yellow) != 0)
            flags |= (int)EElement.Blue;

        return flags;
    }
}