using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Gas;
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

        var applied = false;
        foreach (var target in targets)
        {
            var targetAsc = CombatGasBridge.ResolveTargetAsc(simulation, target);
            if (targetAsc is null)
                continue;

            // 玩家侧目标的 Health 变化在此被转到共享账本（规格 §1.2 的「应用后转移」）；
            // 扣血变化量统一过伤害规则管线（DamagePipeline）。
            SharedHpSettlement.RunTransferred(simulation, source, target, () =>
            {
                var result = targetAsc.ApplyGameplayEffect(
                    new GameplayEffectSpec(def, sourceAsc, targetAsc, setByCaller));
                applied |= result.Success;
            }, gameplayEffectId);

            // 规格 §2.5：效果挂上封印后立刻清标记并视作已行动。
            if (target.Side == ECombatSide.Player &&
                target.Index >= 0 &&
                target.Index < simulation.PlayerTeam.Characters.Count &&
                simulation.PlayerTeam.Characters[target.Index].IsSealed)
            {
                CombatStateMachine.EnforceSeal(simulation, target.Index);
            }
        }

        return applied;
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