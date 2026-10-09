using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat;

/// <summary>
/// 卡牌目标解析的公开口径（供界面与状态机共用）：合法目标集合、是否需要玩家显式点选、
/// 以及 Self / All / Team / RandomN 的默认目标自动填充（战斗规格 §11.7.1：正式战斗 UI 必须自动填这两类默认目标，否则退化成空放）。
/// </summary>
public static class CombatTargeting
{
    /// <summary>扣费前验证目标集合；默认目标由模拟器展开，所有返回值都是只读快照。</summary>
    internal static IReadOnlyList<CombatTargetRef>? ResolvePlayerTargets(
        CombatSimulation simulation, ETargetSide side, ETargetScope scope, int targetCount,
        int sourceIndex, IReadOnlyList<CombatTargetRef> requested, out string error)
    {
        error = "目标数量、唯一性或合法范围不符合定义。";
        if (requested.Distinct().Count() != requested.Count)
            return null;
        var self = new CombatTargetRef(ECombatSide.Player, sourceIndex);
        if (scope == ETargetScope.Team)
            return side != ETargetSide.Enemy && (requested.Count == 0 || (requested.Count == 1 && requested[0] == CombatTargetRef.PlayerTeam))
                ? Array.AsReadOnly(new[] { CombatTargetRef.PlayerTeam }) : null;
        if (scope == ETargetScope.Self || side == ETargetSide.Self)
            return requested.Count == 0 || (requested.Count == 1 && requested[0] == self)
                ? Array.AsReadOnly(new[] { self }) : null;

        var legal = CollectLegalTargetsForScope(simulation, side, scope, sourceIndex);
        if (!requested.All(legal.Contains))
            return null;
        if (scope == ETargetScope.All)
            return requested.Count == 0 || requested.Count == legal.Count ? Array.AsReadOnly(legal.ToArray()) : null;
        if (scope == ETargetScope.RandomN)
        {
            var count = Math.Min(Math.Max(1, targetCount), legal.Count);
            if (requested.Count == 0)
                return Array.AsReadOnly(CombatTargetResolver.PickRandomTargets(legal, count, simulation.RetargetRng).ToArray());
            return requested.Count == count ? Array.AsReadOnly(requested.ToArray()) : null;
        }
        if (requested.Count == 1 || (requested.Count == 0 && legal.Count == 0))
            return Array.AsReadOnly(requested.ToArray());
        return null;
    }

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

    /// <summary>敌方嘲讽限制单体与随机目标，全体/队伍效果沿用完整合法池。</summary>
    public static List<CombatTargetRef> CollectLegalTargetsForScope(CombatSimulation simulation,
        ETargetSide side, ETargetScope scope, int sourceCharacterIndex)
    {
        var legal = CollectLegalTargets(simulation, side, sourceCharacterIndex);
        if (scope is ETargetScope.All or ETargetScope.Team or ETargetScope.Self)
            return legal;
        var highest = legal.Where(target => target.Side == ECombatSide.Enemy)
            .Select(target => simulation.EnemyTeam.Enemies[target.Index].Asc.GetCurrentValue(AttributeIds.Taunt))
            .DefaultIfEmpty(0).Max();
        return highest > 0 ? legal.Where(target => target.Side != ECombatSide.Enemy ||
            simulation.EnemyTeam.Enemies[target.Index].Asc.GetCurrentValue(AttributeIds.Taunt) >= highest).ToList() : legal;
    }

    /// <summary>
    /// 自动填充默认目标：Self → 施法者；All → 全部合法目标；Team → 队伍账本；
    /// RandomN → 用重选随机流在合法池取 N（不重复）。需要显式点选的卡返回 <c>false</c>。
    /// 合法池为空时返回 <c>true</c> 且 targets 为空集（入队后按空放结算，规格 §2.4）。
    /// RandomN 会消耗战斗随机流；界面展示使用 CollectLegalTargets / RequiresExplicitTarget。
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

        var resolved = ResolvePlayerTargets(simulation, card.TargetSide, card.TargetScope, card.TargetCount,
            sourceCharacterIndex, [], out _);
        targets = resolved ?? [];
        return resolved is not null;
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
        return CollectLegalTargetsForScope(simulation, card.TargetSide, card.TargetScope, sourceCharacterIndex).Contains(target);
    }
}