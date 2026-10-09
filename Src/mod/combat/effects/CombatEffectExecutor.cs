using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;
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
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object>? payloadOverrides = null)
    {
        ExecuteEffectRefCore(effectRef, simulation, source, targets, depth: 0, payloadOverrides);
    }

    /// <summary>钩子次数门闩在条件和执行预算通过之后登记，在载荷开始之前阻断重入。</summary>
    internal void ExecuteEffectRefWhen(
        EffectRefDto effectRef, CombatSimulation simulation, CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets, Func<bool> beforeExecution) =>
        ExecuteEffectRefCore(effectRef, simulation, source, targets, depth: 0, beforeExecution: beforeExecution);

    private void ExecuteEffectRefCore(
        EffectRefDto effectRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth,
        IReadOnlyDictionary<string, object>? payloadOverrides = null,
        Func<bool>? beforeExecution = null)
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

        using var scope = simulation.EffectBudget.TryEnter();
        if (scope is null || (beforeExecution is not null && !beforeExecution()))
            return;

        var mergedParams = ContentParameters.Merge(effect.Params, effectRef.Params, payloadOverrides);
        ExecuteKind(effect.Kind, mergedParams, effect, simulation, source, targets, depth);
    }

    /// <summary>
    /// 求值效果的全部 <c>conditions</c>（AND）。未知 CondType / 参数非法 = 不通过：
    /// 运行期保守失败，内容准入阶段由 <c>ContentDefinitionValidator</c> 提前拦下。
    /// 条件主体缺省 = 来源角色（身份类条件用）。
    /// </summary>
    private static bool ConditionsPass(
        EffectDto effect,
        CombatSimulation simulation,
        CombatTargetRef source)
    {
        if (effect.Conditions.Count == 0)
            return true;

        var (elementFlags, raceFlags) = CombatIdentity.Resolve(simulation, source);
        var context = new CombatCondContext(simulation, source, elementFlags, raceFlags);
        return CombatConditionEvaluator.Pass(effect.Conditions, context, $"effect:{effect.Id}:conditions");
    }

    public void ExecuteSkillActionRef(
        SkillActionRefDto actionRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object>? payloadOverrides = null)
    {
        _skillActionExecutor.ExecuteSkillActionRef(actionRef, simulation, source, targets, payloadOverrides);
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
            case EEffectKind.ModifyDrawCount:
            case EEffectKind.GainShield:
            case EEffectKind.ModifyDrawCountByDiscard:
            case EEffectKind.DispelDebuffs:
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
        var instanceParams = BuffActionParams.BuildScaledInstanceParams(simulation, source, parameters, "buffId");
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
        var amount = ContentParameters.ReadFloat(parameters, "amount", 0f);
        // 攻击次数（2026-09-27）：params 里的 AttackCount / AttackCountByChain / AttackCountMinusDiscard
        // 解析成总次数；两个通道（GAS 公式 / 直伤）都必须同口径，否则同一条效果换条通道次数就变了。
        var attackCount = DamageScaling.ResolveAttackCount(sim, parameters);
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
            // 解析后的总次数覆盖同名参数（ByChain / MinusDiscard 声明的是"来源"，不是次数本身）。
            DamageScaling.ApplyAttackCountOverride(sim, parameters, setByCaller);
            if (_gameplayEffectApplicator.ApplyToTargets(sim, source, targets, gameplayEffectId, setByCaller))
                return;
        }

        // 直伤路径与 GAS 通道同口径（见 DamageScaling）：增伤 + 目标受伤增加同桶加算，连携单独乘算。
        var sourceDealtScale =
            CombatGasBridge.ResolveTargetAsc(sim, source)?.GetCurrentValue(AttributeIds.DamageDealtScale) ?? 0f;
        sourceDealtScale += DamageScaling.ResolveCardDamageBonus(sim, source);
        var chainMultiplier = DamageScaling.ChainMultiplier(sim.CurrentChainBonus);
        // 伤害维度（2026-09-26 魔法专精受伤倍率）：直伤通道自身不带维度声明，按所引用 GE 的声明取
        // （魔法 → 追加 MagicDamageTakenScale）；取不到时按物理，与 DamagePipeline.Settle 的缺省一致。
        var damageType = ResolveDeclaredDamageType(gameplayEffectId);

        foreach (var target in targets)
        {
            var takenScale = DamagePipeline.ResolveTakenScale(sim, target, damageType.Kind);
            var dealt = amount *
                DamageScaling.CombineBonuses(sourceDealtScale, takenScale) *
                chainMultiplier;

            // 逐次结算（每次都是一次独立的伤害事件；数额逐次相同）。
            for (var hit = 0; hit < attackCount; hit++)
                DamagePipeline.Settle(sim, source, target, dealt, effectId, damageType.Kind, damageType.Element);
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

        using var scope = sim.EffectBudget.TryEnter();
        if (scope is null)
            return 0f;

        var total = 0f;
        foreach (var target in targets)
            total += DamagePipeline.Settle(sim, source, target, amount, effectId, kind, element).HealthLoss;
        return total;
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
        var amount = ContentParameters.ReadFloat(parameters, "amount", 0f);
        // 治疗吃源侧治疗强度（与伤害加 100% 源物攻同构），再吃连携加成；DamageDealtScale 不影响治疗。
        // healPowerScale（2026-09-25）：治疗强度占比，缺省 1（全额）；0 = 不吃回复量（「回复 12 + 0% 回复量」）。
        var healPowerScale = MathF.Max(0f, ContentParameters.ReadFloat(parameters, "healPowerScale", 1f));
        var sourceAsc = CombatGasBridge.ResolveTargetAsc(sim, source);
        var sourceHealPower = sourceAsc?.GetCurrentValue(AttributeIds.HealPower) ?? 0f;
        amount = MathF.Max(0f, amount + sourceHealPower * healPowerScale);
        // 治疗输出增加（HealingDealtScale，2026-10-06）：与 DamageDealtScale 对伤害的关系同构，
        // 作用域 = 施疗者；「队伍获得绿属性的恢复的效果 +50%」用它表达。
        var healingDealtScale = sourceAsc?.GetCurrentValue(AttributeIds.HealingDealtScale) ?? 0f;
        amount *= MathF.Max(0f, 1f + healingDealtScale + sim.CurrentOrbHealingBonus);
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
                var before = sim.PlayerTeam.SharedHpExact;
                sim.PlayerTeam.HealShared(amount);
                PresentationEmitter.EmitHeal(sim, source, target, sim.PlayerTeam.SharedHpExact - before);
                continue;
            }

            var targetAsc = CombatGasBridge.ResolveTargetAsc(sim, target);
            if (targetAsc is null)
                continue;

            var maxHealth = targetAsc.GetCurrentValue(AttributeIds.MaxHealth);
            var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
            var updatedHealth = MathF.Min(maxHealth, currentHealth + amount);
            targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, updatedHealth));
            PresentationEmitter.EmitHeal(sim, source, target, MathF.Max(0f, updatedHealth) - currentHealth);
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

    /// <summary>
    /// 直伤通道的伤害维度（2026-09-26 魔法专精受伤倍率用）：直伤本身不带维度声明，
    /// 按所引用 GE 的第一条 <c>Damage</c> 执行取；无 GE / 无伤害执行时按物理
    /// （与 <c>DamagePipeline.Settle</c> 的缺省一致，也即历史行为）。
    /// </summary>
    /// <remarks>
    /// GE 通道的维度由 <c>DamageExecution</c> 自己解析（那条路径不会走到这里）；
    /// 这里只服务"GE 缺失 / 未能应用时回落到直伤"的兜底，避免魔法 GE 的兜底伤害丢掉魔法受伤倍率。
    /// </remarks>
    private DamageTypeSpec ResolveDeclaredDamageType(string? gameplayEffectId)
    {
        if (string.IsNullOrWhiteSpace(gameplayEffectId) ||
            !_registry.Store.TryGetGameplayEffect(gameplayEffectId, out var definition))
        {
            return DamageTypeSpec.Default;
        }

        foreach (var execution in definition.Executions)
        {
            if (string.Equals(execution.Kind, DamageExecution.DamageKind, StringComparison.OrdinalIgnoreCase))
                return DamageTypeParser.Parse(execution.DamageType, execution.Element);
        }

        return DamageTypeSpec.Default;
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object> values, string key, out string result)
    {
        result = string.Empty;
        if (!values.TryGetValue(key, out var value) || value is null)
            return false;

        result = value.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(result);
    }

}