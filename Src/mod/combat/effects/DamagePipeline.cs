using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;
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
    /// <summary>统一结算入口：未截断数额 → 规则/护盾 → 生命写入 → 命中与实际损血事件。</summary>
    public static DamageSettlementResult Settle(
        CombatSimulation simulation, CombatTargetRef source, CombatTargetRef target, float amount,
        string? effectId = null, EDamageKind kind = EDamageKind.Physical, EElement element = EElement.None)
    {
        var asc = CombatGasBridge.ResolveTargetAsc(simulation, target);
        if (asc is null || !float.IsFinite(amount) || amount < 0f)
            return default;
        if (!simulation.EffectBudget.TrySpendStep())
            return default;

        // 攻防公式把伤害压到零仍是命中，反击/受击成长与护盾全吸收同口径。
        if (amount == 0f)
        {
            NotifyAfter(simulation, source, target, 0f, effectId, kind, element);
            return default;
        }

        var shieldBefore = asc.GetCurrentValue(AttributeIds.Shield);
        var finalAmount = RunBefore(simulation, source, target, amount, effectId, kind, element);
        var absorbed = MathF.Max(0f, shieldBefore - asc.GetCurrentValue(AttributeIds.Shield));
        float loss;
        if (target.Side == ECombatSide.Player)
        {
            var before = simulation.PlayerTeam.SharedHpExact;
            simulation.PlayerTeam.ApplySharedDamage(finalAmount);
            loss = MathF.Max(0f, before - simulation.PlayerTeam.SharedHpExact);
        }
        else
        {
            var before = asc.GetCurrentValue(AttributeIds.Health);
            asc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, before - finalAmount));
            loss = MathF.Max(0f, before - asc.GetCurrentValue(AttributeIds.Health));
            if (before > 0f && asc.GetCurrentValue(AttributeIds.Health) <= 0f)
                simulation.Buffs.RefreshEnemyPresenceConditions(simulation);
        }

        NotifyAfter(simulation, source, target, loss, effectId, kind, element);
        return new DamageSettlementResult(amount, absorbed, loss, MathF.Max(0f, finalAmount - loss));
    }

    /// <summary>
    /// 目标受伤倍率（<c>DamageTakenScale</c>）。队伍账本目标没有槽位 ASC，取队伍 ASC 的同名属性
    /// （无该属性时为 0，即不缩放）。
    /// </summary>
    /// <param name="kind">伤害包维度（2026-09-26）：<see cref="EDamageKind.Magical"/> 时额外并入
    /// <see cref="AttributeIds.MagicDamageTakenScale"/>（只吃魔法伤害的受伤倍率）——两者与增伤同桶加算，
    /// 见 <see cref="DamageScaling.CombineBonuses"/>。缺省 <see cref="EDamageKind.Physical"/> 即"只吃全伤害倍率"，
    /// 与旧调用点行为完全一致。</param>
    public static float ResolveTakenScale(
        CombatSimulation simulation,
        CombatTargetRef target,
        EDamageKind kind = EDamageKind.Physical)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var scale = ResolveTakenAttribute(simulation, target, AttributeIds.DamageTakenScale);
        if (kind == EDamageKind.Magical)
            scale += ResolveTakenAttribute(simulation, target, AttributeIds.MagicDamageTakenScale);
        return scale;
    }

    /// <summary>受伤倍率属性取值：队伍账本回落到队伍 ASC，其余目标取槽位 ASC（无 ASC 时为 0）。</summary>
    private static float ResolveTakenAttribute(
        CombatSimulation simulation,
        CombatTargetRef target,
        string attributeId)
    {
        if (SharedHpSettlement.IsPlayerTeamLedger(target))
            return simulation.PlayerTeam.Asc.GetCurrentValue(attributeId);

        return CombatGasBridge.ResolveTargetAsc(simulation, target)
            ?.GetCurrentValue(attributeId) ?? 0f;
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
        if (IsImmune(simulation, target))
            return 0f;
        AbsorbByShield(simulation, source, target, ref packet);
        if (SharedHpSettlement.IsPlayerSlot(target) && !simulation.PlayerTeam.SharedHpLocked &&
            simulation.PlayerTeam.SharedHpExact > 0f && packet.Amount > 0f &&
            packet.Amount >= simulation.PlayerTeam.SharedHpExact)
        {
            simulation.Buffs.FireBeforeFatalDamage(simulation, target);
            if (IsImmune(simulation, target))
                return 0f;
        }
        return MathF.Max(0f, packet.Amount);
    }

    private static bool IsImmune(CombatSimulation simulation, CombatTargetRef target) => target.Side switch
    {
        ECombatSide.Player when target.Index >= 0 && target.Index < simulation.PlayerTeam.Characters.Count =>
            simulation.PlayerTeam.Characters[target.Index].Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage),
        ECombatSide.Enemy when target.Index >= 0 && target.Index < simulation.EnemyTeam.Enemies.Count =>
            simulation.EnemyTeam.Enemies[target.Index].Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage),
        _ => false,
    };

    /// <summary>
    /// 护盾抵扣（2026-09-26）：点名玩家角色槽位、且来源为敌方的伤害，先按 <b>1 点护盾抵 1 点伤害</b>
    /// 从该角色的护盾里扣，扣完的余额才落到队伍共享账本（见战斗规格「护盾」）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>队伍账本（<c>scope: Team</c>）没有具体受击角色，不抵扣——与「分槽护盾不吃账本目标」同口径；</item>
    /// <item>自身结算的伤害（中毒 / 手牌槽伤害，<c>source == target</c>）与友方来源的伤害不抵扣，
    /// 与受击钩子 <c>onDamaged</c> 的「敌方来源」口径一致；</item>
    /// <item>放在规则之后：槽位减伤先算，护盾抵扣的是规则修正后的最终数额。</item>
    /// </list>
    /// </remarks>
    private static void AbsorbByShield(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        ref DamagePacket packet)
    {
        if (packet.Amount <= 0f)
            return;

        if (!SharedHpSettlement.IsPlayerSlot(target))
            return;

        if (source.Side != ECombatSide.Enemy || source == target)
            return;

        var characters = simulation.PlayerTeam.Characters;
        if (target.Index >= characters.Count)
            return;

        var asc = characters[target.Index].Asc;
        var shield = asc.GetCurrentValue(AttributeIds.Shield);
        if (shield <= 0f)
            return;

        var absorbed = asc.Aggregator.ConsumeCurrentValue(AttributeIds.Shield, packet.Amount);
        packet.Amount -= absorbed;
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

        // 受击钩子（onDamaged，2026-09-25）的记账：玩家角色被**敌方来源**的攻击命中即记一次，
        // 与"最终掉没掉血"解耦（2026-09-26 起）——护盾 / 减伤把伤害完全抵消时角色仍然"挨了这一下"，
        // 「受到攻击后回复生命」「受到伤害后获得护盾」都应触发（见战斗规格「护盾」「受击钩子」）。
        // 同一批次先累计、批次结束统一触发（见 CombatSimulation.FlushOnDamagedHits）；
        // 中毒 / 手牌槽伤害等由持有者自身结算（source == target），不计入。
        if (SharedHpSettlement.IsPlayerSlot(target) &&
            source.Side == ECombatSide.Enemy &&
            source != target)
        {
            simulation.RecordDamagedPlayerHit(target.Index, source);
            // 魔法受击账（TookMagicDamageLastTurn 条件，2026-09-26）：与 onDamaged 同门闩，
            // 只多一个"伤害维度 = 魔法"；被护盾 / 减伤完全抵消也算挨了这一下（与上一行同口径）。
            if (kind == EDamageKind.Magical)
                simulation.RecordMagicDamageTaken(target.Index);
        }

        if (appliedAmount <= 0f)
            return;

        var packet = CreatePacket(source, target, appliedAmount, effectId, kind, element);
        simulation.Rules.DispatchAfterDamage(simulation.CreateContext(), in packet);

        // 表现事件（规格 §16）：所有生产伤害写入都经过这里，是唯一的 DamageDealt 记账点。
        PresentationEmitter.EmitDamage(simulation, source, target, appliedAmount, kind, element, effectId);
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