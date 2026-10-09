using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Effects;

/// <summary>
/// 兼容 Instant GE 的 Health 修饰符：玩家槽位临时使用共享账本量级计算变化，写入后复位。
/// 变化量交给统一 DamagePipeline 或队伍治疗入口；槽位治疗被拒绝。
/// Damage execution 自身由 GameplayEffectSpec.DamageReceiver 接收未截断数额，不通过 Health 差值推算。
/// 此边界只包住数值写入，onApply 等生命周期钩子在边界外分发，避免重入伤害重复转移。
/// </summary>
internal static class SharedHpSettlement
{
    #region 共享生命兼容转移
    /// <summary>玩家槽位角色（分槽结算 D2 的对象）。</summary>
    public static bool IsPlayerSlot(CombatTargetRef target) =>
        target.Side == ECombatSide.Player && target.Index >= 0;

    /// <summary>玩家队伍账本（<c>scope: Team</c> 解析出的哨兵引用）。</summary>
    public static bool IsPlayerTeamLedger(CombatTargetRef target) =>
        target.Side == ECombatSide.Player && target.Index < 0;

    /// <summary>
    /// 在 <paramref name="target"/> 上执行一次 GAS 写入。玩家侧目标走「应用后转移」，其余目标原样放行；
    /// 扣血变化量统一过伤害规则管线（见类型注释）。
    /// </summary>
    /// <param name="kind"><paramref name="apply"/> 产生的伤害包类型（物理/魔法/元素），
    /// 由 <c>ExecutionDefDto.damageType</c> 解析而来——规则靠它区分"吃哪条防御/哪些抗性"。</param>
    /// <param name="element">伤害包的属性标签（可多属性或空）。</param>
    public static float RunTransferred(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        Action apply,
        string? effectId = null,
        EDamageKind kind = EDamageKind.Physical,
        EElement element = EElement.None)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(apply);

        if (IsPlayerSlot(target))
        {
            return RunOnSlot(simulation, source, target, apply, effectId, kind, element);
        }

        if (IsPlayerTeamLedger(target))
        {
            return RunOnLedger(simulation, source, target, apply, effectId, kind, element);
        }

        return RunOnTarget(simulation, source, target, apply, effectId, kind, element);
    }

    private static float RunOnSlot(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        Action apply,
        string? effectId,
        EDamageKind kind,
        EElement element)
    {
        var characters = simulation.PlayerTeam.Characters;
        if (target.Index >= characters.Count)
            return 0f;

        var asc = characters[target.Index].Asc;
        var restoreBase = asc.GetBaseValue(AttributeIds.Health);
        var restoreCurrent = asc.GetCurrentValue(AttributeIds.Health);

        // 工作血量只供 Health 修饰符计算变化；Damage execution 不在这里写 Health。
        var working = SetHealth(asc, simulation.PlayerTeam.SharedHpExact);
        float delta;
        try
        {
            apply();
            delta = working - asc.GetCurrentValue(AttributeIds.Health);
        }
        finally
        {
            asc.Attributes.SetBaseValue(AttributeIds.Health, restoreBase);
            asc.Attributes.SetCurrentValue(AttributeIds.Health, restoreCurrent);
        }

        if (delta <= 0f)
        {
            // 负变化量即「回血打到了槽位」，规格 §1.3 只认 Team 治疗，这里软失败丢弃。
            if (delta < 0f)
                simulation.PlayerTeam.CountRejectedSlotHeal();
            return 0f;
        }

        return DamagePipeline.Settle(simulation, source, target, delta, effectId, kind, element).HealthLoss;
    }

    /// <summary>
    /// 账本目标同样先应用再转移：GE 直接写队伍 ASC 会绕过 BattleStart 写锁与阵亡判定，
    /// 复位后重走 <see cref="PlayerTeamState"/> 才能让这些约束生效。
    /// </summary>
    private static float RunOnLedger(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        Action apply,
        string? effectId,
        EDamageKind kind,
        EElement element)
    {
        var team = simulation.PlayerTeam;
        var asc = team.Asc;
        var restoreBase = asc.GetBaseValue(AttributeIds.Health);
        var restoreCurrent = asc.GetCurrentValue(AttributeIds.Health);

        apply();
        var delta = restoreCurrent - asc.GetCurrentValue(AttributeIds.Health);
        if (delta == 0f)
            return 0f;

        asc.Attributes.SetBaseValue(AttributeIds.Health, restoreBase);
        asc.Attributes.SetCurrentValue(AttributeIds.Health, restoreCurrent);

        if (delta < 0f)
        {
            // GE 把账本写成回血：走与其它治疗相同的表现记账（写入量可能被上限截断，按实际回血量记）。
            var before = team.SharedHpExact;
            team.HealShared(-delta);
            PresentationEmitter.EmitHeal(simulation, source, target, team.SharedHpExact - before);
            return 0f;
        }

        return DamagePipeline.Settle(simulation, source, target, delta, effectId, kind, element).HealthLoss;
    }

    /// <summary>
    /// 敌方（及其它非玩家侧）目标：GAS 已经直接写进目标 Health，这里按写入前后的差值补一次规则管线；
    /// 规则改了数额就按最终数额回写（例如槽位护盾把伤害压到 1，或完全抵消到 0）。
    /// 差值为负即 GE 写成了回血：补一条 <c>HealedEvent</c> 后直接返回（回血不过伤害规则）。
    /// </summary>
    private static float RunOnTarget(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        Action apply,
        string? effectId,
        EDamageKind kind,
        EElement element)
    {
        var asc = CombatGasBridge.ResolveTargetAsc(simulation, target);
        if (asc is null)
        {
            apply();
            return 0f;
        }

        var before = asc.GetCurrentValue(AttributeIds.Health);
        apply();
        var delta = before - asc.GetCurrentValue(AttributeIds.Health);
        if (delta < 0f)
        {
            // GE 把目标写成回血：补一条治疗表现事件，与其它治疗通道口径一致。
            PresentationEmitter.EmitHeal(simulation, source, target, -delta);
            return 0f;
        }

        if (delta == 0f)
            return 0f;

        asc.Attributes.SetCurrentValue(AttributeIds.Health, before);
        return DamagePipeline.Settle(simulation, source, target, delta, effectId, kind, element).HealthLoss;
    }

    private static float SetHealth(AbilitySystemComponent asc, float value)
    {
        asc.Attributes.SetBaseValue(AttributeIds.Health, value);
        asc.Aggregator.Recalculate(AttributeIds.Health);
        return asc.GetCurrentValue(AttributeIds.Health);
    }

    #endregion
}