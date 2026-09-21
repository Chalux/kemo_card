using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Effects;

/// <summary>
/// 伤害包管线（2026-09-19 统一）：<b>所有</b>伤害写入都必须先过 <c>OnBeforeDamage</c>（可改数额、
/// 可完全抵消），写入完成后再广播 <c>OnAfterDamage</c>（只读观测最终数额）。
/// </summary>
/// <remarks>
/// <para>统一的起因：此前三条通道彼此不一致——GAS 公式通道（<c>DamageExecution</c>）吃目标受伤倍率
/// 但绕过规则；直伤定值通道吃规则但不吃目标受伤倍率；充能球通道两者都吃。现在三者共用本管线。</para>
/// <para>通道差异只保留在"数额怎么算"：GAS = <c>Amount + 源物攻 + 源 Damage − 目标物防</c>；
/// 直伤 = 定值；充能球 = 定值 + 产球者攻击加成。三者都乘源全伤害增加 / 连携（各自口径）
/// 与 <see cref="ResolveTakenScale"/> 目标受伤倍率，然后走本管线。</para>
/// </remarks>
internal static class DamagePipeline
{
    /// <summary>
    /// 目标受伤倍率（<c>DamageTakenScale</c>）。队伍账本目标没有槽位 ASC，取队伍 ASC 的同名属性
    /// （无该属性时为 0，即不缩放）。
    /// </summary>
    public static float ResolveTakenScale(CombatSimulation simulation, CombatTargetRef target)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (SharedHpSettlement.IsPlayerTeamLedger(target))
            return simulation.PlayerTeam.Asc.GetCurrentValue(AttributeIds.DamageTakenScale);

        return CombatGasBridge.ResolveTargetAsc(simulation, target)
            ?.GetCurrentValue(AttributeIds.DamageTakenScale) ?? 0f;
    }

    /// <summary>过 <c>OnBeforeDamage</c>；返回规则修正后（≥ 0）的数额。</summary>
    public static float RunBefore(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        float amount,
        string? effectId = null,
        EDamageKind kind = EDamageKind.Physical,
        EElement element = EElement.None)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (amount <= 0f)
            return 0f;

        var packet = CreatePacket(source, target, amount, effectId, kind, element);
        simulation.Rules.DispatchBeforeDamage(simulation.CreateContext(), ref packet);
        return MathF.Max(0f, packet.Amount);
    }

    /// <summary>写入完成后广播 <c>OnAfterDamage</c>（数额 ≤ 0 视为未发生伤害事件，不广播）。</summary>
    public static void NotifyAfter(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        float appliedAmount,
        string? effectId = null,
        EDamageKind kind = EDamageKind.Physical,
        EElement element = EElement.None)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (appliedAmount <= 0f)
            return;

        var packet = CreatePacket(source, target, appliedAmount, effectId, kind, element);
        simulation.Rules.DispatchAfterDamage(simulation.CreateContext(), in packet);
    }

    private static DamagePacket CreatePacket(
        CombatTargetRef source,
        CombatTargetRef target,
        float amount,
        string? effectId,
        EDamageKind kind,
        EElement element) => new()
        {
            Source = source,
            Target = target,
            Amount = amount,
            EffectId = effectId,
            Kind = kind,
            Element = element,
        };
}
