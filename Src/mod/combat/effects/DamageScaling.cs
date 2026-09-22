namespace KemoCard.Mod.Combat.Effects;

using System.Text.Json;
using KemoCard.Frame.Gas.Executions;
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
}
