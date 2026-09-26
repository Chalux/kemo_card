namespace KemoCard.Frame.Condition;

/// <summary>
/// Combat 域的条件上下文（2026-09-21 启用）：效果 <c>conditions</c> 在<b>战斗内</b>求值时可用。
/// </summary>
/// <remarks>
/// 刻意只暴露基础类型（<see cref="int"/> 位标志），避免 <c>Frame.Condition</c> 反向依赖内容层枚举
/// （内容是 <c>Frame.Content</c> → <c>Frame.Condition</c> 的调用方向）。
/// 属性/种族位标志的口径与 <c>CardDto.Element</c>、<c>CharacterDto.Race</c> 一致。
/// </remarks>
public interface ICombatCondContext
{
    /// <summary>全场累计回合数（换波不清零）。</summary>
    int TurnNumber { get; }

    /// <summary>波内回合计数（换波清零）。</summary>
    int TurnsIntoWave { get; }

    /// <summary>当前输出来源的角色槽位索引（-1 = 非角色来源）。</summary>
    int SourceCharacterIndex { get; }

    /// <summary>
    /// 条件主体（身份类条件用）的属性/种族位标志（2026-09-26 新增）：效果条件缺省 = 来源角色；
    /// 目标筛选时逐候选设置；buff 持有者条件 = 持有者。未设置时为 0（<c>None</c>）。
    /// </summary>
    int SubjectElementFlags { get; }

    /// <summary>条件主体的种族位标志（与 <see cref="SubjectElementFlags"/> 同口径）。</summary>
    int SubjectRaceFlags { get; }

    /// <summary>
    /// 本回合该角色打出的卡牌张数：只统计属性与 <paramref name="elementFlags"/> 有交集的卡
    /// （<paramref name="elementFlags"/> 为 0 时不筛属性）。含空放——牌离开手牌即算打出。
    /// </summary>
    int CountCardsPlayedThisTurn(int characterIndex, int elementFlags);

    /// <summary>
    /// 本回合连携定档的参与人数（不同角色数）：<paramref name="elementFlags"/> 为 0 时取
    /// <b>当前正在结算的卡牌</b>的属性；取这些属性里人头数的最大值（多属性卡取最优）。
    /// 结算区间之外返回 0（"这张牌够不够 N 连携档"类条件读它）。
    /// </summary>
    int CountChainParticipants(int elementFlags);

    /// <summary>
    /// 上阵名单中同时命中 (elementFlags, raceFlags) 的角色数（0 掩码 = 不筛该维度）：
    /// 两个维度都配置时默认取"或"，<paramref name="matchAll"/> 取"且"（与目标筛选同口径）。
    /// 非玩家侧上下文（槽位等）返回 0。
    /// </summary>
    int CountPartyIdentityMatches(int elementFlags, int raceFlags, bool matchAll);
}
