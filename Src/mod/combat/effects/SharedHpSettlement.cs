using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Effects;

/// <summary>
/// 规格 §1.2 / §1.3：玩家角色没有 Health 当前值，整支队伍只有一份共享账本。
/// 但伤害公式、槽位护盾与减伤都长在 GAS 上，所以玩家侧的写入仍照常打在目标 ASC 上，
/// 由本类做「应用后转移」：给槽位铺一份工作血量 → 跑原本的 GAS 写入 → 记下 Health 的变化量
/// → 把变化量转到 <see cref="PlayerTeamState.ApplySharedDamage"/> → 把 ASC 的 Health 复位。
/// </summary>
internal static class SharedHpSettlement
{
    /// <summary>玩家槽位角色（分槽结算 D2 的对象）。</summary>
    public static bool IsPlayerSlot(CombatTargetRef target) =>
        target.Side == ECombatSide.Player && target.Index >= 0;

    /// <summary>玩家队伍账本（<c>scope: Team</c> 解析出的哨兵引用）。</summary>
    public static bool IsPlayerTeamLedger(CombatTargetRef target) =>
        target.Side == ECombatSide.Player && target.Index < 0;

    /// <summary>
    /// 在 <paramref name="target"/> 上执行一次 GAS 写入。玩家侧目标走「应用后转移」，其余目标原样放行。
    /// </summary>
    public static void RunTransferred(CombatSimulation simulation, CombatTargetRef target, Action apply)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(apply);

        if (IsPlayerSlot(target))
        {
            RunOnSlot(simulation, target.Index, apply);
            return;
        }

        if (IsPlayerTeamLedger(target))
        {
            RunOnLedger(simulation, apply);
            return;
        }

        apply();
    }

    private static void RunOnSlot(CombatSimulation simulation, int slotIndex, Action apply)
    {
        var characters = simulation.PlayerTeam.Characters;
        if (slotIndex >= characters.Count)
            return;

        var asc = characters[slotIndex].Asc;
        var restoreBase = asc.GetBaseValue(AttributeIds.Health);
        var restoreCurrent = asc.GetCurrentValue(AttributeIds.Health);

        // 工作血量取账本当前值：GAS 公式在正确量级上运算，过量伤害也按账本余额自然截断。
        var working = SetHealth(asc, simulation.PlayerTeam.SharedHpExact);
        apply();
        var delta = working - asc.GetCurrentValue(AttributeIds.Health);

        asc.Attributes.SetBaseValue(AttributeIds.Health, restoreBase);
        asc.Attributes.SetCurrentValue(AttributeIds.Health, restoreCurrent);
        TransferSlotDelta(simulation, delta);
    }

    /// <summary>
    /// 账本目标同样先应用再转移：GE 直接写队伍 ASC 会绕过 BattleStart 写锁与阵亡判定，
    /// 复位后重走 <see cref="PlayerTeamState"/> 才能让这些约束生效。
    /// </summary>
    private static void RunOnLedger(CombatSimulation simulation, Action apply)
    {
        var team = simulation.PlayerTeam;
        var asc = team.Asc;
        var restoreBase = asc.GetBaseValue(AttributeIds.Health);
        var restoreCurrent = asc.GetCurrentValue(AttributeIds.Health);

        apply();
        var delta = restoreCurrent - asc.GetCurrentValue(AttributeIds.Health);
        if (delta == 0f)
            return;

        asc.Attributes.SetBaseValue(AttributeIds.Health, restoreBase);
        asc.Attributes.SetCurrentValue(AttributeIds.Health, restoreCurrent);
        if (delta > 0f)
            team.ApplySharedDamage(delta);
        else
            team.HealShared(-delta);
    }

    private static void TransferSlotDelta(CombatSimulation simulation, float delta)
    {
        if (delta > 0f)
        {
            simulation.PlayerTeam.ApplySharedDamage(delta);
            return;
        }

        // 负变化量即「回血打到了槽位」，规格 §1.3 只认 Team 治疗，这里软失败丢弃。
        if (delta < 0f)
            simulation.PlayerTeam.CountRejectedSlotHeal();
    }

    private static float SetHealth(AbilitySystemComponent asc, float value)
    {
        asc.Attributes.SetBaseValue(AttributeIds.Health, value);
        asc.Aggregator.Recalculate(AttributeIds.Health);
        return asc.GetCurrentValue(AttributeIds.Health);
    }
}