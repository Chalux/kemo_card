using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Effects;

public sealed class CombatEffectExecutor
{
    /// <summary>
    /// 链式引用最大展开深度。内容准入（<c>ContentDefinitionValidator.ValidateReferenceCycles</c>）
    /// 已拒绝环，这里是兜底绕过校验的内容，避免无限递归导致 StackOverflow。
    /// </summary>
    private const int MaxChainDepth = 32;

    private readonly GameDefinitionRegistry _registry;
    private readonly GameplayEffectApplicator _gameplayEffectApplicator;
    private readonly SkillActionExecutor _skillActionExecutor;

    /// <summary>由 <see cref="CombatSimulation"/> 构造后回填（二者互相依赖，避免构造环）。</summary>
    internal BuffRuntime? BuffRuntime { get; private set; }

    internal void AttachBuffRuntime(BuffRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        BuffRuntime = runtime;
    }

    /// <summary>
    /// 伤害规则一律经 <c>simulation.Rules</c> 分发（<see cref="DamagePipeline"/>），
    /// 因此执行器不再持有独立的规则引擎实例。
    /// </summary>
    public CombatEffectExecutor(GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _gameplayEffectApplicator = new GameplayEffectApplicator(registry);
        _skillActionExecutor = new SkillActionExecutor(
            registry,
            _gameplayEffectApplicator,
            (effectRef, simulation, source, targets, depth) =>
                ExecuteEffectRefCore(effectRef, simulation, source, targets, depth));
    }

    public void ExecuteEffectRef(
        EffectRefDto effectRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets)
    {
        ExecuteEffectRefCore(effectRef, simulation, source, targets, depth: 0);
    }

    private void ExecuteEffectRefCore(
        EffectRefDto effectRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(effectRef);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(targets);

        if (depth > MaxChainDepth)
            return;

        if (!_registry.Store.TryGetEffect(effectRef.EffectId, out var effect))
            return;

        // 效果级条件（EffectDto.conditions）：战斗域求值，不通过则整条效果不执行。
        if (!ConditionsPass(effect, simulation, source))
            return;

        var mergedParams = MergeParams(effect.Params, effectRef.Params);
        ExecuteKind(effect.Kind, mergedParams, effect, simulation, source, targets, depth);
    }

    /// <summary>
    /// 求值效果的全部 <c>conditions</c>（AND）。未知 CondType / 参数非法 = 不通过：
    /// 运行期保守失败，内容准入阶段由 <c>ContentDefinitionValidator</c> 提前拦下。
    /// </summary>
    private static bool ConditionsPass(
        EffectDto effect,
        CombatSimulation simulation,
        CombatTargetRef source)
    {
        if (effect.Conditions.Count == 0)
            return true;

        var context = new CombatCondContext(simulation, source.Index);
        foreach (var condition in effect.Conditions)
        {
            if (!ConditionDomains.Combat.TryGet(condition.Kind, out var handler) || handler is null)
                return false;

            var args = JsonSerializer.SerializeToElement(
                condition.Params ?? new Dictionary<string, object>(StringComparer.Ordinal));
            var sourcePath = $"effect:{effect.Id}:conditions.{condition.Kind}";
            if (!handler.TryParse(args, sourcePath, out var parsedArgs, out _) || parsedArgs is null)
                return false;

            if (!handler.Check(parsedArgs, context).Passed)
                return false;
        }

        return true;
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
        IReadOnlyList<CombatTargetRef> targets,
        int depth)
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
            case EEffectKind.AttachSlotBuff:
            case EEffectKind.SetActionCount:
            case EEffectKind.SetDomain:
            case EEffectKind.DiscardSlot:
                _skillActionExecutor.ExecuteLegacyAction(kind, effect, mergedParams, simulation, source, targets, depth);
                break;
            case EEffectKind.ApplyBuff:
                ApplyBuffEffect(simulation, source, targets, mergedParams);
                break;
            case EEffectKind.RemoveBuff:
                RemoveBuffEffect(simulation, targets, mergedParams);
                break;
            case EEffectKind.GainOrb:
                ApplyGainOrb(simulation, source, mergedParams);
                break;
            case EEffectKind.GainOrbPerPlayedCard:
                ApplyGainOrbPerPlayedCard(simulation, source, mergedParams);
                break;
            case EEffectKind.GainOrbByDeckCount:
                ApplyGainOrbByDeckCount(simulation, source, mergedParams);
                break;
            case EEffectKind.ModifyStat:
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 对每个目标挂 buff：params.buffId 必填，其余参数（amount/charge 等）进入实例参数。
    /// 带 <c>hookTargets</c> / <c>targetFilter</c> 时按目标选择器重解析（"伤害敌方全体 + 增益自身"这类卡用）。
    /// </summary>
    private void ApplyBuffEffect(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (BuffRuntime is null || !BuffActionParams.TryGetBuffId(parameters, out var buffId))
            return;

        var resolvedTargets = BuffActionParams.HasTargetSelector(parameters)
            ? CombatTargetSelector.Resolve(simulation, source, parameters)
            : targets;
        var instanceParams = BuffActionParams.BuildInstanceParams(parameters, "buffId");
        foreach (var target in resolvedTargets)
            BuffRuntime.Apply(simulation, target, buffId, instanceParams);
    }

    /// <summary>授予充能球：params.orbTypeId 必填、params.count 可选（默认 1）；产球者 = 来源角色。</summary>
    private static void ApplyGainOrb(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!BuffActionParams.TryGetString(parameters, "orbTypeId", out var orbTypeId))
            return;

        var count = BuffActionParams.ReadInt(parameters, "count", 1);
        simulation.Orbs.Grant(simulation, orbTypeId, BuffActionParams.ResolveProducerIndex(source), count);
    }

    /// <summary>
    /// 按本回合出牌数授予充能球：数量 = <c>max(0, 出牌数 × perCard + offset)</c>（缺省 perCard 1 / offset 0）。
    /// </summary>
    private static void ApplyGainOrbPerPlayedCard(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!BuffActionParams.TryGetString(parameters, "orbTypeId", out var orbTypeId))
            return;

        var perCard = BuffActionParams.ReadInt(parameters, "perCard", 1);
        var offset = BuffActionParams.ReadInt(parameters, "offset", 0);
        var played = simulation.CountCardsPlayedThisTurn(source.Index, 0);
        var count = Math.Max(0, (played * perCard) + offset);
        if (count == 0)
            return;

        simulation.Orbs.Grant(simulation, orbTypeId, BuffActionParams.ResolveProducerIndex(source), count);
    }

    /// <summary>
    /// 按"自身卡组内命中筛选项的卡牌数"分档授予充能球：
    /// 取 <c>params.tiers</c>（<c>[[最小张数, 球数], …]</c>）中满足条件的最高档，没有命中则不发球。
    /// </summary>
    private void ApplyGainOrbByDeckCount(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!BuffActionParams.TryGetString(parameters, "orbTypeId", out var orbTypeId))
            return;
        if (source.Side != ECombatSide.Player ||
            source.Index < 0 ||
            source.Index >= simulation.PlayerTeam.Characters.Count)
        {
            return;
        }

        var tiers = ReadTiers(parameters);
        if (tiers.Count == 0)
            return;

        var elementMask = BuffActionParams.ReadInt(parameters, "elementMask", 0);
        var character = simulation.PlayerTeam.Characters[source.Index];
        var cardCount = 0;
        foreach (var cardId in character.OwnedCardIds)
        {
            if (!_registry.Store.TryGetCard(cardId, out var card))
                continue;
            if (elementMask == 0 || (card.Element & elementMask) != 0)
                cardCount++;
        }

        var orbCount = 0;
        foreach (var (minCount, count) in tiers)
        {
            if (cardCount >= minCount && count > orbCount)
                orbCount = count;
        }

        if (orbCount <= 0)
            return;

        simulation.Orbs.Grant(simulation, orbTypeId, BuffActionParams.ResolveProducerIndex(source), orbCount);
    }

    /// <summary>解析 <c>params.tiers</c>：形如 <c>[[0,2],[4,3]]</c> 的 [最小张数, 球数] 列表。</summary>
    private static List<(int MinCount, int OrbCount)> ReadTiers(IReadOnlyDictionary<string, object> parameters)
    {
        var tiers = new List<(int, int)>();
        if (!parameters.TryGetValue("tiers", out var value) || value is null)
            return tiers;

        if (value is not JsonElement element || element.ValueKind != JsonValueKind.Array)
            return tiers;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2)
                continue;
            if (!item[0].TryGetInt32(out var minCount) || !item[1].TryGetInt32(out var orbCount))
                continue;

            tiers.Add((minCount, orbCount));
        }

        return tiers;
    }

    /// <summary>驱散：params.buffId 精确匹配或 params.withTags 列表匹配；不可驱散 tag 自动跳过。</summary>
    private void RemoveBuffEffect(
        CombatSimulation simulation,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (BuffRuntime is null)
            return;

        BuffActionParams.TryGetBuffId(parameters, out var buffId);
        var withTags = BuffActionParams.TryGetTagList(parameters, "withTags");
        if (string.IsNullOrWhiteSpace(buffId) && withTags is null)
            return;

        foreach (var target in targets)
            BuffRuntime.Dispel(simulation, target, buffId, withTags);
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
            if (sim.CurrentChainBonus > 0f)
                setByCaller[DamageExecution.SetByCallerChainBonusScale] = sim.CurrentChainBonus;
            // 动态攻击系数（如"本回合每触发 1 个绿球 +100% 魔攻，最多 +300%"）：算好后覆盖 GE 的静态 attackScale。
            DamageScaling.ApplyAttackScaleOverride(sim, parameters, setByCaller);
            if (_gameplayEffectApplicator.ApplyToTargets(sim, source, targets, gameplayEffectId, setByCaller))
                return;
        }

        // 直伤路径与 GAS 通道同口径（见 DamageScaling）：增伤 + 目标受伤增加同桶加算，连携单独乘算。
        var sourceDealtScale =
            CombatGasBridge.ResolveTargetAsc(sim, source)?.GetCurrentValue(AttributeIds.DamageDealtScale) ?? 0f;
        var chainMultiplier = DamageScaling.ChainMultiplier(sim.CurrentChainBonus);

        foreach (var target in targets)
        {
            var dealt = amount *
                DamageScaling.CombineBonuses(sourceDealtScale, DamagePipeline.ResolveTakenScale(sim, target)) *
                chainMultiplier;

            // 规格 §1.3：Team 直伤对账本只结算一次（不分槽逐次）。伤害包仍过规则管线，
            // 但目标不是槽位，分槽护盾类规则按 target.Index < 0 自然不匹配。
            if (SharedHpSettlement.IsPlayerTeamLedger(target))
            {
                var shared = DamagePipeline.RunBefore(sim, source, target, dealt, effectId);
                if (shared <= 0f)
                    continue;

                sim.PlayerTeam.ApplySharedDamage(shared);
                DamagePipeline.NotifyAfter(sim, source, target, shared, effectId);
                continue;
            }

            var applied = DamagePipeline.RunBefore(sim, source, target, dealt, effectId);
            if (applied <= 0f)
                continue;

            ApplyAmountToTarget(sim, source, target, applied, effectId);
        }
    }

    /// <summary>
    /// 定值伤害通道（充能球 / 普通攻击等外部系统）：调用方已算好最终数额（含源侧全伤害增加与目标受伤倍率），
    /// 这里只走伤害规则管线与血量写入——不套 GAS 的物攻/物防公式，也不乘连携。
    /// </summary>
    /// <returns>实际写入的总伤害（规则可能改数额或完全抵消；无写入返回 0）。</returns>
    public float ApplyFixedDamage(
        CombatSimulation sim,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        float amount,
        string? effectId = null,
        EDamageKind kind = EDamageKind.Physical,
        EElement element = EElement.None)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(targets);
        if (amount <= 0f)
            return 0f;

        var total = 0f;
        foreach (var target in targets)
        {
            var applied = DamagePipeline.RunBefore(sim, source, target, amount, effectId, kind, element);
            if (applied <= 0f)
                continue;

            if (SharedHpSettlement.IsPlayerTeamLedger(target))
            {
                sim.PlayerTeam.ApplySharedDamage(applied);
                DamagePipeline.NotifyAfter(sim, source, target, applied, effectId, kind, element);
                total += applied;
                continue;
            }

            total += ApplyAmountToTarget(sim, source, target, applied, effectId, kind, element);
        }

        return total;
    }

    /// <summary>
    /// 把已过规则管线的伤害数额写进目标血量（玩家槽位转共享账本，其余写目标 ASC）。
    /// </summary>
    /// <returns>实际写入的数额；目标 ASC 解析不出（例如索引越界）时为 0，不虚报伤害。</returns>
    private static float ApplyAmountToTarget(
        CombatSimulation sim,
        CombatTargetRef source,
        CombatTargetRef target,
        float amount,
        string? effectId,
        EDamageKind kind = EDamageKind.Physical,
        EElement element = EElement.None)
    {
        // 规格 §1.2：玩家槽位没有 Health 当前值，分槽结算的结果直接扣共享账本。
        if (SharedHpSettlement.IsPlayerSlot(target))
        {
            sim.PlayerTeam.ApplySharedDamage(amount);
            DamagePipeline.NotifyAfter(sim, source, target, amount, effectId, kind, element);
            return amount;
        }

        var targetAsc = CombatGasBridge.ResolveTargetAsc(sim, target);
        if (targetAsc is null)
            return 0f;

        var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
        var updatedHealth = MathF.Max(0f, currentHealth - amount);
        targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, updatedHealth);
        DamagePipeline.NotifyAfter(sim, source, target, amount, effectId, kind, element);
        return amount;
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
        // 治疗吃源侧治疗强度（与伤害加 100% 源物攻同构），再吃连携加成；DamageDealtScale 不影响治疗。
        var sourceHealPower =
            CombatGasBridge.ResolveTargetAsc(sim, source)?.GetCurrentValue(AttributeIds.HealPower) ?? 0f;
        amount = MathF.Max(0f, amount + sourceHealPower);
        if (sim.CurrentChainBonus > 0f)
            amount *= 1f + sim.CurrentChainBonus;
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