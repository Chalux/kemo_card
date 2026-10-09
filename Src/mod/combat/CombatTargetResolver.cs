using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat;

/// <summary>敌方自动目标、嘲讽与已入队卡牌失效重选；玩家入队验证由 CombatTargeting 共用。</summary>
internal static class CombatTargetResolver
{
    #region 目标解析
    public static IReadOnlyList<CombatTargetRef> ResolveEnemySkillTargets(
        CombatSimulation simulation,
        SkillDto skill,
        int enemyIndex)
    {
        var spec = skill.TargetOverride;
        if (spec is null)
            return [CombatTargetRef.PlayerTeam];

        return ResolveTargetsFromSpec(simulation, spec, enemyIndex);
    }

    private static IReadOnlyList<CombatTargetRef> ResolveTargetsFromSpec(
        CombatSimulation simulation,
        TargetSpecDto spec,
        int sourceEnemyIndex)
    {
        // 规格 §1.3：scope: Team 对该侧队伍账本一次结算（敌方来源时「Enemy 侧」即玩家队伍）。
        if (spec.Scope is ETargetScope.Team)
            return ResolveTeamLedgerTarget(simulation, spec.Side is ETargetSide.Enemy or ETargetSide.Any);

        var legal = CollectLegalTargetsForEnemy(simulation, spec.Side, sourceEnemyIndex);
        if (legal.Count == 0)
            return [];

        // 嘲讽（2026-09-25）：指向玩家角色的单体 / 随机挑选只从"嘲讽值最高"的合法目标里取；
        // 范围（All）与账本（Team）不受影响——嘲讽吸引的是点名攻击。
        if (spec.Scope is not ETargetScope.All)
            legal = ApplyTauntPriority(simulation, legal);

        return spec.Scope switch
        {
            ETargetScope.All => legal,
            ETargetScope.RandomN => PickRandomTargets(legal, Math.Max(1, spec.TargetCount), simulation.RetargetRng),
            _ => legal.Count <= spec.TargetCount || spec.TargetCount <= 0
                ? [legal[0]]
                : legal.Take(spec.TargetCount).ToList(),
        };
    }

    /// <summary>
    /// 嘲讽优先（2026-09-25，见战斗规格「嘲讽」）：合法目标里存在嘲讽值 &gt; 0 的角色时，
    /// 只保留嘲讽值最高的那些（并列时保持原顺序）；没有嘲讽者时原样返回。
    /// </summary>
    private static List<CombatTargetRef> ApplyTauntPriority(
        CombatSimulation simulation,
        List<CombatTargetRef> legal)
    {
        var highest = 0f;
        foreach (var target in legal)
        {
            if (target.Side != ECombatSide.Player || target.Index < 0 ||
                target.Index >= simulation.PlayerTeam.Characters.Count)
            {
                continue;
            }

            var taunt = simulation.PlayerTeam.Characters[target.Index].Asc.GetCurrentValue(AttributeIds.Taunt);
            if (taunt > highest)
                highest = taunt;
        }

        if (highest <= 0f)
            return legal;

        var filtered = new List<CombatTargetRef>(legal.Count);
        foreach (var target in legal)
        {
            if (target.Side != ECombatSide.Player || target.Index < 0 ||
                target.Index >= simulation.PlayerTeam.Characters.Count)
            {
                // 非玩家侧目标（如敌方自身指向）不参与嘲讽筛选。
                filtered.Add(target);
                continue;
            }

            if (Math.Abs(simulation.PlayerTeam.Characters[target.Index].Asc.GetCurrentValue(AttributeIds.Taunt) - highest) <= 0.0001f)
                filtered.Add(target);
        }

        return filtered.Count > 0 ? filtered : legal;
    }

    /// <summary>
    /// 规格 §6.3：<c>scope: RandomN</c> 从合法目标中<b>无放回随机</b>抽取 N 个。
    /// </summary>
    /// <remarks>
    /// 不能退化成 <c>legal.Take(N)</c>：那样「随机」会变成固定取前 N 个（敌方技能永远打 0 号槽），
    /// 枚举名与规格承诺都与行为不符。
    /// </remarks>
    internal static List<CombatTargetRef> PickRandomTargets(
        List<CombatTargetRef> legal,
        int count,
        HostRng rng)
    {
        if (count >= legal.Count)
            return [.. legal];

        // 部分 Fisher-Yates：只需洗出前 count 个，避免整表全量打乱。
        var pool = legal.ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = rng.NextInt(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return [.. pool[..count]];
    }

    private static List<CombatTargetRef> CollectLegalTargetsForEnemy(
        CombatSimulation simulation,
        ETargetSide side,
        int sourceEnemyIndex)
    {
        var legal = new List<CombatTargetRef>();
        if (side is ETargetSide.Self)
        {
            if (sourceEnemyIndex >= 0 && sourceEnemyIndex < simulation.EnemyTeam.Enemies.Count &&
                simulation.EnemyTeam.Enemies[sourceEnemyIndex].IsAlive)
            {
                legal.Add(new CombatTargetRef(ECombatSide.Enemy, sourceEnemyIndex));
            }

            return legal;
        }

        if (side is ETargetSide.Ally or ETargetSide.Any)
        {
            for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
            {
                if (side == ETargetSide.Ally && i == sourceEnemyIndex)
                    continue;
                if (!simulation.EnemyTeam.Enemies[i].IsAlive)
                    continue;
                legal.Add(new CombatTargetRef(ECombatSide.Enemy, i));
            }
        }

        if (side is ETargetSide.Enemy or ETargetSide.Any && !simulation.PlayerTeam.IsDefeated)
        {
            // 规格 §1.3：玩家侧点选的是槽位角色（分槽结算 D2）；要打账本必须显式写 scope: Team。
            for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
                legal.Add(new CombatTargetRef(ECombatSide.Player, i));
        }

        return legal;
    }

    /// <summary>规格 §1.3：v1 只有玩家侧有队伍账本；指向敌方队伍的 Team 目标无处结算，退化为空放。</summary>
    private static IReadOnlyList<CombatTargetRef> ResolveTeamLedgerTarget(
        CombatSimulation simulation,
        bool isPlayerSide)
    {
        if (!isPlayerSide || simulation.PlayerTeam.IsDefeated)
            return [];

        return [CombatTargetRef.PlayerTeam];
    }

    /// <summary>
    /// 规格 §2.4：单体在合法池中按 <see cref="ERetargetPolicy"/> 重选，池空即空放；
    /// 多目标去掉非法目标后对剩余合法子集结算，子集为空即空放。两者都不回滚已行动。
    /// </summary>
    public static IReadOnlyList<CombatTargetRef> ResolveCardTargets(
        CombatSimulation simulation,
        QueuedCardEntry entry,
        CardDto card)
    {
        // 规格 §1.3：scope: Team 的卡牌恒结算到队伍账本，标记时点选的槽位不参与。
        if (card.TargetScope is ETargetScope.Team)
            return ResolveTeamLedgerTarget(simulation, card.TargetSide is not ETargetSide.Enemy);

        if (entry.Targets.Count == 0)
            return [];

        var validTargets = entry.Targets
            .Where(target => IsValidTarget(simulation, card, entry.CharacterIndex, target))
            .ToList();
        if (validTargets.Count == entry.Targets.Count)
            return validTargets;
        if (!IsSingleTargetCard(card))
            return validTargets;
        if (validTargets.Count > 0)
            return [validTargets[0]];

        var retargeted = TryRetargetSingleTarget(simulation, card, entry.CharacterIndex);
        return retargeted.HasValue ? [retargeted.Value] : [];
    }

    private static bool IsSingleTargetCard(CardDto card) =>
        card.TargetScope is ETargetScope.Single or ETargetScope.Self || card.TargetCount <= 1;

    /// <summary>规格 §2.4：缺省与 <see cref="ERetargetPolicy.RandomLegal"/> 都走 Run RNG 在合法池均匀取一。</summary>
    private static CombatTargetRef? TryRetargetSingleTarget(CombatSimulation simulation, CardDto card, int sourceCharacterIndex)
    {
        if (card.RetargetPolicy == ERetargetPolicy.Skip)
            return null;

        var legal = CombatTargeting.CollectLegalTargetsForScope(simulation, card.TargetSide, card.TargetScope, sourceCharacterIndex);
        if (legal.Count == 0)
            return null;

        return card.RetargetPolicy switch
        {
            ERetargetPolicy.HighestHp => legal.MaxBy(target => GetTargetHp(simulation, target)),
            ERetargetPolicy.LowestHp => legal.MinBy(target => GetTargetHp(simulation, target)),
            _ => legal[simulation.RetargetRng.NextInt(0, legal.Count)],
        };
    }

    /// <summary>合法目标口径与界面共用一份（<see cref="CombatTargeting.CollectLegalTargets"/>）。</summary>
    private static List<CombatTargetRef> CollectLegalTargets(
        CombatSimulation simulation,
        ETargetSide side,
        int sourceCharacterIndex) =>
        CombatTargeting.CollectLegalTargets(simulation, side, sourceCharacterIndex);

    private static bool IsValidTarget(
        CombatSimulation simulation,
        CardDto card,
        int sourceCharacterIndex,
        CombatTargetRef target)
    {
        return card.TargetSide switch
        {
            ETargetSide.Self => target.Side == ECombatSide.Player && target.Index == sourceCharacterIndex,
            ETargetSide.Ally => target.Side == ECombatSide.Player &&
                target.Index >= 0 &&
                target.Index < simulation.PlayerTeam.Characters.Count,
            ETargetSide.Enemy => target.Side == ECombatSide.Enemy &&
                target.Index >= 0 &&
                target.Index < simulation.EnemyTeam.Enemies.Count &&
                simulation.EnemyTeam.Enemies[target.Index].IsAlive,
            ETargetSide.Any => IsValidAnyTarget(simulation, target),
            _ => false,
        };
    }

    private static bool IsValidAnyTarget(CombatSimulation simulation, CombatTargetRef target)
    {
        if (target.Side == ECombatSide.Player)
        {
            return target.Index >= 0 &&
                target.Index < simulation.PlayerTeam.Characters.Count;
        }

        return target.Side == ECombatSide.Enemy &&
            target.Index >= 0 &&
            target.Index < simulation.EnemyTeam.Enemies.Count &&
            simulation.EnemyTeam.Enemies[target.Index].IsAlive;
    }

    private static int GetTargetHp(CombatSimulation simulation, CombatTargetRef target)
    {
        if (target.Side == ECombatSide.Enemy)
            return simulation.EnemyTeam.Enemies[target.Index].CurrentHp;
        return simulation.PlayerTeam.SharedHp;
    }
    #endregion
}