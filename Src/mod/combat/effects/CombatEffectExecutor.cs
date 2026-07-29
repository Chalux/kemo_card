using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Effects;

public sealed class CombatEffectExecutor
{
    private readonly GameDefinitionRegistry _registry;
    private readonly CombatRuleEngine _rules;
    private readonly GameplayEffectApplicator _gameplayEffectApplicator;
    private readonly SkillActionExecutor _skillActionExecutor;

    public CombatEffectExecutor(GameDefinitionRegistry registry, CombatRuleEngine rules)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(rules);
        _registry = registry;
        _rules = rules;
        _gameplayEffectApplicator = new GameplayEffectApplicator(registry);
        _skillActionExecutor = new SkillActionExecutor(registry, _gameplayEffectApplicator, ExecuteEffectRef);
    }

    public void ExecuteEffectRef(
        EffectRefDto effectRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets)
    {
        ArgumentNullException.ThrowIfNull(effectRef);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(targets);

        if (!_registry.Store.TryGetEffect(effectRef.EffectId, out var effect))
            return;

        var mergedParams = MergeParams(effect.Params, effectRef.Params);
        ExecuteKind(effect.Kind, mergedParams, effect, simulation, source, targets);
    }

    public void ExecuteSkillActionRef(
        SkillActionRefDto actionRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets)
    {
        _skillActionExecutor.ExecuteSkillActionRef(actionRef, simulation, source, targets);
    }

    private void ExecuteKind(
        EEffectKind kind,
        IReadOnlyDictionary<string, object> mergedParams,
        EffectDto effect,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets)
    {
        switch (kind)
        {
            case EEffectKind.Damage:
                ApplyDamage(simulation, source, targets, mergedParams, effect.Id);
                break;
            case EEffectKind.Heal:
                ApplyHeal(simulation, source, targets, mergedParams);
                break;
            case EEffectKind.Draw:
            case EEffectKind.Discard:
            case EEffectKind.GainResource:
            case EEffectKind.ExecuteScript:
            case EEffectKind.ChainEffects:
                _skillActionExecutor.ExecuteLegacyAction(kind, effect, mergedParams, simulation, source, targets);
                break;
            case EEffectKind.ApplyBuff:
            case EEffectKind.RemoveBuff:
            case EEffectKind.ModifyStat:
                break;
            default:
                break;
        }
    }

    private void ApplyDamage(
        CombatSimulation sim,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters,
        string? effectId)
    {
        var amount = ReadFloat(parameters, "amount", 0f);
        if (TryGetGameplayEffectId(parameters, "damageGameplayEffectId", out var gameplayEffectId))
        {
            var setByCaller = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Amount"] = amount,
            };
            foreach (var (key, value) in parameters)
                setByCaller[key] = value;
            if (_gameplayEffectApplicator.ApplyToTargets(sim, source, targets, gameplayEffectId, setByCaller))
                return;
        }

        foreach (var target in targets)
        {
            // 规格 §1.3：Team 直伤对账本只结算一次，且不经分槽护盾/减伤钩子。
            if (SharedHpSettlement.IsPlayerTeamLedger(target))
            {
                sim.PlayerTeam.ApplySharedDamage(amount);
                continue;
            }

            var packet = new DamagePacket
            {
                Source = source,
                Target = target,
                Amount = amount,
                EffectId = effectId,
            };
            var ctx = sim.CreateContext();
            _rules.DispatchBeforeDamage(ctx, ref packet);
            if (packet.Amount <= 0)
                continue;

            // 规格 §1.2：玩家槽位没有 Health 当前值，分槽结算的结果直接扣共享账本。
            if (SharedHpSettlement.IsPlayerSlot(target))
            {
                sim.PlayerTeam.ApplySharedDamage(packet.Amount);
                continue;
            }

            var targetAsc = CombatGasBridge.ResolveTargetAsc(sim, target);
            if (targetAsc is null)
                continue;

            var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
            var updatedHealth = MathF.Max(0f, currentHealth - packet.Amount);
            targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, updatedHealth);
        }
    }

    /// <summary>
    /// 规格 §1.3：治疗只回队伍共享账本。点名玩家槽位的治疗是软失败，
    /// 在应用任何 GameplayEffect 之前就被剔除，避免留下半截副作用。
    /// </summary>
    private void ApplyHeal(
        CombatSimulation sim,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var amount = ReadFloat(parameters, "amount", 0f);
        var healableTargets = RejectSlotHealTargets(sim, targets);
        if (healableTargets.Count == 0)
            return;

        if (TryGetGameplayEffectId(parameters, "healGameplayEffectId", out var gameplayEffectId))
        {
            var setByCaller = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Amount"] = amount,
            };
            foreach (var (key, value) in parameters)
                setByCaller[key] = value;
            if (_gameplayEffectApplicator.ApplyToTargets(sim, source, healableTargets, gameplayEffectId, setByCaller))
                return;
        }

        foreach (var target in healableTargets)
        {
            if (SharedHpSettlement.IsPlayerTeamLedger(target))
            {
                sim.PlayerTeam.HealShared(amount);
                continue;
            }

            var targetAsc = CombatGasBridge.ResolveTargetAsc(sim, target);
            if (targetAsc is null)
                continue;

            var maxHealth = targetAsc.GetCurrentValue(AttributeIds.MaxHealth);
            var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
            var updatedHealth = MathF.Min(maxHealth, currentHealth + amount);
            targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, updatedHealth));
        }
    }

    private static List<CombatTargetRef> RejectSlotHealTargets(
        CombatSimulation sim,
        IReadOnlyList<CombatTargetRef> targets)
    {
        var healable = new List<CombatTargetRef>(targets.Count);
        foreach (var target in targets)
        {
            if (SharedHpSettlement.IsPlayerSlot(target))
            {
                sim.PlayerTeam.CountRejectedSlotHeal();
                continue;
            }

            healable.Add(target);
        }

        return healable;
    }

    private static bool TryGetGameplayEffectId(
        IReadOnlyDictionary<string, object> parameters,
        string preferredKey,
        out string gameplayEffectId)
    {
        if (TryGetString(parameters, preferredKey, out gameplayEffectId))
            return true;
        return TryGetString(parameters, "gameplayEffectId", out gameplayEffectId);
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object> values, string key, out string result)
    {
        result = string.Empty;
        if (!values.TryGetValue(key, out var value) || value is null)
            return false;

        result = value.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(result);
    }

    private static IReadOnlyDictionary<string, object> MergeParams(
        IReadOnlyDictionary<string, object>? baseParams,
        IReadOnlyDictionary<string, object>? overrideParams)
    {
        if (baseParams is null || baseParams.Count == 0)
            return overrideParams ?? new Dictionary<string, object>(StringComparer.Ordinal);

        if (overrideParams is null || overrideParams.Count == 0)
            return baseParams;

        var merged = new Dictionary<string, object>(baseParams, StringComparer.Ordinal);
        foreach (var (key, value) in overrideParams)
            merged[key] = value;
        return merged;
    }

    private static float ReadFloat(IReadOnlyDictionary<string, object> parameters, string key, float defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return defaultValue;

        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetSingle(),
            _ => float.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue,
        };
    }
}