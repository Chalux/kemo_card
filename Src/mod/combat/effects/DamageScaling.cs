namespace KemoCard.Mod.Combat.Effects;

using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Runtime;

/// <summary>
/// 伤害缩放口径（战斗规格「增伤与受伤增加一律加算」，2026-09-21 决议）：
/// <code>最终伤害 = base × (1 + Σ增伤 + Σ受伤增加) × (1 + 连携加成)</code>
/// </summary>
/// <remarks>
/// 关键点：**所有"增伤"与所有"受到伤害增加"都进同一个加算桶**，不再互相乘算；
/// 只有<b>连携</b>是独立的乘算因子（它是"多人协奏"的档位奖励，语义上不属于单次伤害的增伤）。
/// 这样多条增伤之间的收益是可预期的线性叠加，不会因为同时吃到受伤倍率而指数放大。
/// </remarks>
internal static class DamageScaling
{
    /// <summary>增伤与受伤增加同桶加算后的倍率（下限 0）。</summary>
    public static float CombineBonuses(float dealtBonus, float takenBonus) =>
        MathF.Max(0f, 1f + dealtBonus + takenBonus);

    /// <summary>连携乘算因子（下限 0）。</summary>
    public static float ChainMultiplier(float chainBonus) => MathF.Max(0f, 1f + chainBonus);

    /// <summary>完整口径：<c>(1 + 增伤 + 受伤增加) × (1 + 连携)</c>。</summary>
    public static float Combine(float dealtBonus, float takenBonus, float chainBonus) =>
        CombineBonuses(dealtBonus, takenBonus) * ChainMultiplier(chainBonus);

    /// <summary>卡牌专属增伤只读当前结算卡的实际玩家来源，不能传给队友、普攻或充能球。</summary>
    public static float ResolveCardDamageBonus(CombatSimulation simulation, CombatTargetRef source)
    {
        if (simulation.CurrentCardType is null || source.Side != ECombatSide.Player ||
            source.Index < 0 || source.Index != simulation.CurrentCardSourceIndex)
            return 0f;

        return CombatGasBridge.ResolveSourceAsc(simulation, source)?.GetCurrentValue(AttributeIds.CardDamageDealtScale) ?? 0f;
    }

    /// <summary>
    /// 动态攻击系数：参数 <c>attackScaleFromOrbs = { elementMask, perOrb, maxBonus }</c> →
    /// <c>attackScale = 1 + min(maxBonus, perOrb × 本回合已触发的命中球数)</c>。
    /// 「直到此卡打出前，本回合每触发 1 个绿球 +100% 魔攻（最多 +300%）」即 <c>{4, 1, 3}</c>。
    /// </summary>
    /// <returns>参数存在且可解析时返回 true（结果写进 <paramref name="attackScale"/>）。</returns>
    public static bool TryResolveOrbScaledAttackScale(
        CombatSimulation simulation,
        IReadOnlyDictionary<string, object> parameters,
        out float attackScale)
    {
        attackScale = 1f;
        if (!parameters.TryGetValue("attackScaleFromOrbs", out var value) || value is null)
            return false;

        if (value is not JsonElement element || element.ValueKind != JsonValueKind.Object)
            return false;

        var elementMask = 0;
        var perOrb = 1f;
        var maxBonus = 0f;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "elementMask" when property.Value.TryGetInt32(out var mask):
                    elementMask = mask;
                    break;
                case "perOrb" when property.Value.TryGetSingle(out var per):
                    perOrb = per;
                    break;
                case "maxBonus" when property.Value.TryGetSingle(out var max):
                    maxBonus = max;
                    break;
            }
        }

        var triggered = simulation.CountOrbsTriggeredThisTurn(elementMask);
        attackScale = 1f + MathF.Min(MathF.Max(0f, maxBonus), perOrb * triggered);
        return true;
    }

    /// <summary>
    /// 把动态攻击系数写进 SetByCaller 覆盖键（<see cref="DamageExecution.SetByCallerAttackScale"/>）。
    /// 两个执行器（效果 / 技能动作）共用，避免两条通道对同一参数有不同解释。
    /// </summary>
    public static void ApplyAttackScaleOverride(
        CombatSimulation simulation,
        IReadOnlyDictionary<string, object> parameters,
        IDictionary<string, object> setByCaller)
    {
        if (TryResolveOrbScaledAttackScale(simulation, parameters, out var attackScale))
            setByCaller[DamageExecution.SetByCallerAttackScale] = attackScale;
    }

    /// <summary>固定次数参数（整数，总次数）。</summary>
    public const string AttackCountParam = "AttackCount";

    /// <summary>按连携人头数取次数参数（布尔 true 启用）。</summary>
    public const string AttackCountByChainParam = "AttackCountByChain";

    /// <summary>按"基准次数 − 已弃牌数"取次数参数（整数 = 基准次数）。</summary>
    public const string AttackCountMinusDiscardParam = "AttackCountMinusDiscard";
    /// <summary>
    /// 攻击次数（2026-09-27 新增）：<c>params</c> 里三选一，解析成本次执行的**总次数**——
    /// <list type="bullet">
    /// <item><c>AttackCount</c>（整数，缺省 1）= 固定次数，<c>0</c> 表示本次执行不造成伤害；</item>
    /// <item><c>AttackCountByChain</c>（布尔）= 当前连携参与人数（<see cref="CombatSimulation.CountChainParticipants"/>，
    /// 结算区间之外为 0 → 下限 1）；</item>
    /// <item><c>AttackCountMinusDiscard</c>（整数 = 基准次数）= <c>基准 − <see cref="CombatSimulation.LastDiscardCount"/></c>，
    /// 下限 1（弃得越多打得越少，但至少 1 次）。</item>
    /// </list>
    /// 优先级 <c>AttackCount</c> &gt; <c>AttackCountByChain</c> &gt; <c>AttackCountMinusDiscard</c>：
    /// 固定次数最具体，动态口径只在没有显式固定值时参与（内容只应声明一个）。
    /// 次数对每个目标分别生效，每次都是一次独立的伤害事件。
    /// </summary>
    /// <remarks>
    /// 物理卡牌加成（2026-10-06）：卡牌自身声明的次数之外，若当前结算卡是
    /// <c>ECardType.Physics</c>，再叠加 <see cref="AttributeIds.PhysicalCardAttackCount"/>
    /// （「物理攻击的卡牌攻击次数 +1」）。加成加在"下限 1"之后，因此未声明次数参数的物理卡
    /// 也能从 1 段变 2 段；非物理卡牌与卡牌结算区间之外不受影响。
    /// </remarks>
    public static int ResolveAttackCount(
        CombatSimulation simulation,
        IReadOnlyDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(parameters);

        var declared = ResolveDeclaredAttackCount(simulation, parameters);
        // 显式声明 0 次 = 本次执行不造成伤害：物理卡牌加成也不该把它拉回 1 段。
        if (declared <= 0)
            return 0;

        return (int)Math.Clamp((long)declared + ResolvePhysicalCardBonus(simulation), 1, DamageExecution.MaxAttackCount);
    }

    /// <summary>卡牌自身声明的总次数（不含物理卡牌加成）；未声明任何参数时基准 1。</summary>
    private static int ResolveDeclaredAttackCount(
        CombatSimulation simulation,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (parameters.ContainsKey(AttackCountParam))
            return Math.Max(0, ContentParameters.ReadInt(parameters, AttackCountParam, 1));

        // 连携人头数按"当前结算卡"的属性位取（0 = 用 CurrentChainCardElementFlags）；
        // 卡牌结算区间之外连携是空的 → 0 → 下限 1（规格：没有连携时照常打一次）。
        if (IsTrueFlag(parameters, AttackCountByChainParam))
            return Math.Max(1, simulation.CountChainParticipants(0));

        if (parameters.ContainsKey(AttackCountMinusDiscardParam))
        {
            var baseCount = ContentParameters.ReadInt(parameters, AttackCountMinusDiscardParam, 1);
            return (int)Math.Clamp((long)baseCount - simulation.LastDiscardCount, 1, DamageExecution.MaxAttackCount);
        }

        return 1;
    }

    /// <summary>
    /// 物理卡牌加成：仅当"当前结算卡是物理卡"且来源角色带有
    /// <see cref="AttributeIds.PhysicalCardAttackCount"/> 时返回其非负整数值，否则 0。
    /// </summary>
    public static int ResolvePhysicalCardBonus(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        if (simulation.CurrentCardType != ECardType.Physics)
            return 0;

        var source = CombatGasBridge.ResolveSourceAsc(
            simulation,
            new CombatTargetRef(ECombatSide.Player, simulation.CurrentCardSourceIndex));
        var raw = source?.GetCurrentValue(AttributeIds.PhysicalCardAttackCount) ?? 0f;
        return float.IsFinite(raw) && raw > 0f
            ? (int)MathF.Floor(Math.Min(raw, DamageExecution.MaxAttackCount)) : 0;
    }

    /// <summary>
    /// 把解析好的总次数写进 SetByCaller（<see cref="DamageExecution.SetByCallerAttackCount"/>）。
    /// </summary>
    /// <remarks>
    /// 三个次数参数都没声明、且当前结算卡不是"带物理卡牌加成的物理卡"时<b>不写</b>，
    /// 单次结算路径与历史行为逐字节一致。
    /// </remarks>
    public static void ApplyAttackCountOverride(
        CombatSimulation simulation,
        IReadOnlyDictionary<string, object> parameters,
        IDictionary<string, object> setByCaller)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(setByCaller);
        if (!HasAttackCountParam(parameters) && ResolvePhysicalCardBonus(simulation) <= 0)
            return;

        setByCaller[DamageExecution.SetByCallerAttackCount] = ResolveAttackCount(simulation, parameters);
    }

    /// <summary>是否声明了任意一个攻击次数参数（内容只应声明一个，这里只做"有没有"判断）。</summary>
    private static bool HasAttackCountParam(IReadOnlyDictionary<string, object> parameters) =>
        parameters.ContainsKey(AttackCountParam) ||
        parameters.ContainsKey(AttackCountByChainParam) ||
        parameters.ContainsKey(AttackCountMinusDiscardParam);

    /// <summary>布尔参数判定（JSON 载入后是 JsonElement 布尔，程序化构造时是 <c>bool</c>）。</summary>
    private static bool IsTrueFlag(IReadOnlyDictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return false;

        return value is bool flag
            ? flag
            : string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }
}