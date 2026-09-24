using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Effects;

public sealed class GameplayEffectApplicator
{
    private readonly GameDefinitionRegistry _registry;

    public GameplayEffectApplicator(GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public bool ApplyToTargets(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        string gameplayEffectId,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(targets);
        if (!_registry.Store.TryGetGameplayEffect(gameplayEffectId, out var def))
            return false;

        var sourceAsc = CombatGasBridge.ResolveSourceAsc(simulation, source);
        var setByCaller = BuildSetByCaller(parameters);
        var descriptor = ResolveDamageDescriptor(def);

        var applied = false;
        // 吸血（2026-09-24）：统计本 GE 对**非玩家侧**目标造成的实际生命损失，结算完再按系数回一次队伍账本。
        var damageDealt = 0f;
        foreach (var target in targets)
        {
            var targetAsc = CombatGasBridge.ResolveTargetAsc(simulation, target);
            if (targetAsc is null)
                continue;

            var healthBefore = targetAsc.GetCurrentValue(AttributeIds.Health);

            // 玩家侧目标的 Health 变化在此被转到共享账本（规格 §1.2 的「应用后转移」）；
            // 扣血变化量统一过伤害规则管线（DamagePipeline），并带上该 GE 声明的伤害维度
            // （damageType/element → 物理/魔法/元素 + 属性标签），否则规则只能看到"物理无属性"。
            SharedHpSettlement.RunTransferred(simulation, source, target, () =>
            {
                var result = targetAsc.ApplyGameplayEffect(
                    new GameplayEffectSpec(def, sourceAsc, targetAsc, setByCaller));
                applied |= result.Success;
            }, gameplayEffectId, descriptor.Kind, descriptor.Element);

            if (target.Side != ECombatSide.Player)
            {
                damageDealt += MathF.Max(0f, healthBefore - targetAsc.GetCurrentValue(AttributeIds.Health));
            }

            // 规格 §2.5：效果挂上封印后立刻清标记并视作已行动；免疫封印 / 免疫中毒的角色先把标签摘掉。
            RemoveGrantedTagIfImmune(simulation, target, BuiltinBuffTags.TraitImmuneSeal, CombatConstants.SealedTag);
            RemoveGrantedTagIfImmune(simulation, target, BuiltinBuffTags.TraitImmunePoison, CombatConstants.PoisonTag);
            if (target.Side == ECombatSide.Player &&
                target.Index >= 0 &&
                target.Index < simulation.PlayerTeam.Characters.Count &&
                simulation.PlayerTeam.Characters[target.Index].IsSealed)
            {
                CombatStateMachine.EnforceSeal(simulation, target.Index);
            }
        }

        // 吸血只对"玩家来源 → 非玩家目标"成立：账本是玩家侧的血，敌人打自己不该给玩家回血。
        if (def.LifestealScale > 0f && damageDealt > 0f && source.Side == ECombatSide.Player)
        {
            var before = simulation.PlayerTeam.SharedHpExact;
            simulation.PlayerTeam.HealShared(damageDealt * def.LifestealScale);
            PresentationEmitter.EmitHeal(
                simulation,
                source,
                CombatTargetRef.PlayerTeam,
                simulation.PlayerTeam.SharedHpExact - before);
        }

        return applied;
    }

    /// <summary>
    /// 标签免疫（<paramref name="immuneTrait"/> → <paramref name="grantedTag"/>）：带该特征的角色在标签挂上后
    /// 立刻摘掉授予该标签的 Gameplay Effect——只抵消这一个标签，
    /// 同一个 GE 附带的其它效果（属性修饰、其它标签）照常保留。
    /// 目前两处使用：封印免疫（<see cref="BuiltinBuffTags.TraitImmuneSeal"/>）与中毒免疫
    /// （<see cref="BuiltinBuffTags.TraitImmunePoison"/>）。
    /// </summary>
    private static void RemoveGrantedTagIfImmune(
        CombatSimulation simulation,
        CombatTargetRef target,
        string immuneTrait,
        string grantedTag)
    {
        if (target.Side != ECombatSide.Player ||
            target.Index < 0 ||
            target.Index >= simulation.PlayerTeam.Characters.Count)
        {
            return;
        }

        var character = simulation.PlayerTeam.Characters[target.Index];
        if (!character.Buffs.HasTag(immuneTrait))
            return;

        var handles = character.Asc.ActiveEffects
            .Where(effect => effect.Def.GrantedTags.Contains(grantedTag, StringComparer.Ordinal))
            .Select(effect => effect.Handle)
            .ToArray();
        foreach (var handle in handles)
            character.Asc.RemoveActiveEffect(handle);
    }

    public bool RemoveFromTargets(
        CombatSimulation simulation,
        IReadOnlyList<CombatTargetRef> targets,
        string gameplayEffectId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(targets);

        var removed = false;
        foreach (var target in targets)
        {
            var targetAsc = CombatGasBridge.ResolveTargetAsc(simulation, target);
            if (targetAsc is null)
                continue;

            var handles = targetAsc.ActiveEffects
                .Where(effect => string.Equals(effect.Def.Id, gameplayEffectId, StringComparison.Ordinal))
                .Select(effect => effect.Handle)
                .ToArray();
            foreach (var handle in handles)
                removed |= targetAsc.RemoveActiveEffect(handle);
        }

        return removed;
    }

    /// <summary>
    /// 取该 Gameplay Effect 的伤害维度声明（第一条 <c>Damage</c> 执行上的 <c>damageType</c>/<c>element</c>）。
    /// 无伤害执行时回落到缺省（物理 + 无属性）——增伤/治疗类 GE 的伤害包语义本就无关紧要。
    /// </summary>
    private static DamageTypeSpec ResolveDamageDescriptor(GameplayEffectDefDto def)
    {
        foreach (var execution in def.Executions)
        {
            if (string.Equals(execution.Kind, DamageExecution.DamageKind, StringComparison.OrdinalIgnoreCase))
                return DamageTypeParser.Parse(execution.DamageType, execution.Element);
        }

        return DamageTypeSpec.Default;
    }

    private static Dictionary<string, float>? BuildSetByCaller(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return null;

        var setByCaller = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            if (TryReadFloat(value, out var number))
                setByCaller[key] = number;
        }

        return setByCaller.Count == 0 ? null : setByCaller;
    }

    private static bool TryReadFloat(object? value, out float number)
    {
        number = 0f;
        if (value is null)
            return false;

        switch (value)
        {
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case byte byteValue:
                number = byteValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case double doubleValue:
                number = (float)doubleValue;
                return true;
            case decimal decimalValue:
                number = (float)decimalValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetSingle(out number);
            default:
                return float.TryParse(value.ToString(), out number);
        }
    }
}