using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat;

/// <summary>
/// 卡牌目标解析的公开口径（供界面与状态机共用）：合法目标集合、是否需要玩家显式点选、
/// 以及 Self / All / Team / RandomN 的默认目标自动填充（战斗规格 §11.7.1：正式战斗 UI 必须自动填这两类默认目标，否则退化成空放）。
/// </summary>
public static class CombatTargeting
{
    /// <summary>
    /// 按目标侧收集当前合法目标：玩家侧全部槽位（Self 只含施法者自己）、敌方只含存活单位。
    /// 与状态机的目标失效重选（规格 §2.4）共用同一口径。
    /// </summary>
    public static List<CombatTargetRef> CollectLegalTargets(
        CombatSimulation simulation,
        ETargetSide side,
        int sourceCharacterIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var legal = new List<CombatTargetRef>();
        if (side is ETargetSide.Self or ETargetSide.Ally or ETargetSide.Any)
        {
            for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
            {
                if (side == ETargetSide.Self && i != sourceCharacterIndex)
                    continue;
                legal.Add(new CombatTargetRef(ECombatSide.Player, i));
            }
        }

        if (side is ETargetSide.Enemy or ETargetSide.Any)
        {
            for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
            {
                if (!simulation.EnemyTeam.Enemies[i].IsAlive)
                    continue;
                legal.Add(new CombatTargetRef(ECombatSide.Enemy, i));
            }
        }

        return legal;
    }

    /// <summary>
    /// 这张卡是否需要玩家显式点选目标：只有「单体 + 非 Self 侧」需要；
    /// Self / All / Team / RandomN 都能自动解析。
    /// </summary>
    public static bool RequiresExplicitTarget(CardDto card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.TargetScope is ETargetScope.Team or ETargetScope.All or ETargetScope.RandomN)
            return false;
        if (card.TargetScope is ETargetScope.Self || card.TargetSide is ETargetSide.Self)
            return false;

        return true;
    }

    /// <summary>
    /// 自动填充默认目标：Self → 施法者；All → 全部合法目标；Team → 队伍账本；
    /// RandomN → 用重选随机流在合法池取 N（不重复）。需要显式点选的卡返回 <c>false</c>。
    /// 合法池为空时返回 <c>true</c> 且 targets 为空集（入队后按空放结算，规格 §2.4）。
    /// </summary>
    public static bool TryResolveAutoTargets(
        CombatSimulation simulation,
        CardDto card,
        int sourceCharacterIndex,
        out IReadOnlyList<CombatTargetRef> targets)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(card);

        if (RequiresExplicitTarget(card))
        {
            targets = [];
            return false;
        }

        if (card.TargetScope is ETargetScope.Team)
        {
            targets = [CombatTargetRef.PlayerTeam];
            return true;
        }

        if (card.TargetScope is ETargetScope.Self || card.TargetSide is ETargetSide.Self)
        {
            targets = [new CombatTargetRef(ECombatSide.Player, sourceCharacterIndex)];
            return true;
        }

        var legal = CollectLegalTargets(simulation, card.TargetSide, sourceCharacterIndex);
        if (card.TargetScope is ETargetScope.RandomN)
        {
            var count = Math.Max(1, card.TargetCount);
            var picked = new List<CombatTargetRef>(Math.Min(count, legal.Count));
            while (picked.Count < count && legal.Count > 0)
            {
                var index = simulation.RetargetRng.NextInt(0, legal.Count);
                picked.Add(legal[index]);
                legal.RemoveAt(index);
            }

            targets = picked;
            return true;
        }

        targets = legal;
        return true;
    }

    /// <summary>某个目标是否在这张卡当前的合法目标集合内（选目标态点单位时用）。</summary>
    public static bool IsLegalTarget(
        CombatSimulation simulation,
        CardDto card,
        int sourceCharacterIndex,
        CombatTargetRef target)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(card);
        return CollectLegalTargets(simulation, card.TargetSide, sourceCharacterIndex).Contains(target);
    }
}