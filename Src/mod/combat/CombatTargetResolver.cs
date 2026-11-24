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

    #endregion
}