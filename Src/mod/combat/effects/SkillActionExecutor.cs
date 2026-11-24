using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Effects;

public sealed class SkillActionExecutor
{
    /// <summary>
    /// 链式引用最大展开深度。内容准入已拒绝环，这里是兜底手写/热更内容绕过校验的情形，
    /// 避免无限递归把进程打成 StackOverflow（.NET 下不可捕获）。
    /// </summary>
    private const int MaxChainDepth = 32;

    private readonly GameDefinitionRegistry _registry;
    private readonly GameplayEffectApplicator _gameplayEffectApplicator;
    private readonly Action<EffectRefDto, CombatSimulation, CombatTargetRef, IReadOnlyList<CombatTargetRef>, int> _executeLegacyEffectRef;

    public SkillActionExecutor(
        GameDefinitionRegistry registry,
        GameplayEffectApplicator gameplayEffectApplicator,
        Action<EffectRefDto, CombatSimulation, CombatTargetRef, IReadOnlyList<CombatTargetRef>, int> executeLegacyEffectRef)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(gameplayEffectApplicator);
        ArgumentNullException.ThrowIfNull(executeLegacyEffectRef);
        _registry = registry;
        _gameplayEffectApplicator = gameplayEffectApplicator;
        _executeLegacyEffectRef = executeLegacyEffectRef;
    }

    public void ExecuteSkillActionRef(
        SkillActionRefDto actionRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object>? payloadOverrides = null)
    {
        ExecuteSkillActionRefCore(actionRef, simulation, source, targets, depth: 0, payloadOverrides);
    }

    private void ExecuteSkillActionRefCore(
        SkillActionRefDto actionRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth,
        IReadOnlyDictionary<string, object>? payloadOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(actionRef);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(targets);

        // 防御性上限：内容准入（ContentDefinitionValidator.ValidateReferenceCycles）已拒绝环，
        // 这里兜底手写/热更内容绕过校验的情形，避免 StackOverflow 直接杀掉进程。
        if (depth > MaxChainDepth)
            return;

        if (!_registry.Store.TryGetSkillAction(actionRef.ActionId, out var action))
            return;

        var mergedParams = ContentParameters.Merge(action.Params, actionRef.Params, payloadOverrides);
        ExecuteAction(action, mergedParams, simulation, source, targets, depth, discardReference: actionRef);
    }

    public void ExecuteLegacyAction(
        EEffectKind kind,
        EffectDto effect,
        EffectRefDto effectRef,
        IReadOnlyDictionary<string, object> mergedParams,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth)
    {
        var legacyAction = new SkillActionDto
        {
            Id = effect.Id,
            Kind = kind switch
            {
                EEffectKind.Draw => ESkillActionKind.Draw,
                EEffectKind.Discard => ESkillActionKind.Discard,
                EEffectKind.GainResource => ESkillActionKind.GainResource,
                EEffectKind.ExecuteScript => ESkillActionKind.ExecuteScript,
                EEffectKind.ChainEffects => ESkillActionKind.ChainActions,
                EEffectKind.AttachSlotBuff => ESkillActionKind.AttachSlotBuff,
                EEffectKind.SetActionCount => ESkillActionKind.SetActionCount,
                EEffectKind.SetDomain => ESkillActionKind.SetDomain,
                EEffectKind.DiscardSlot => ESkillActionKind.DiscardSlot,
                EEffectKind.ModifyDrawCount => ESkillActionKind.ModifyDrawCount,
                EEffectKind.GainShield => ESkillActionKind.GainShield,
                EEffectKind.ModifyDrawCountByDiscard => ESkillActionKind.ModifyDrawCountByDiscard,
                EEffectKind.DispelDebuffs => ESkillActionKind.DispelDebuffs,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            },
            ScriptPath = effect.ScriptPath,
            ScriptEntry = effect.ScriptEntry,
        };

        if (kind == EEffectKind.ChainEffects)
        {
            foreach (var child in effect.EffectRefs)
                _executeLegacyEffectRef(child, simulation, source, targets, depth + 1);
            return;
        }

        ExecuteAction(legacyAction, mergedParams, simulation, source, targets, depth, EContentCategory.Effect, discardReference: effectRef);
    }

    private void ExecuteAction(
        SkillActionDto action,
        IReadOnlyDictionary<string, object> mergedParams,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth,
        EContentCategory originCategory = EContentCategory.SkillAction,
        string? inheritedOwner = null,
        object? discardReference = null)
    {
        using var scope = simulation.EffectBudget.TryEnter();
        if (scope is null)
            return;
        if (simulation.Phase == ECombatPhase.Marble)
        {
            if (action.Kind == ESkillActionKind.DiscardAndRecord)
            {
                simulation.SetLastDiscardCount(0);
                return;
            }
            if (action.Kind is ESkillActionKind.Draw or ESkillActionKind.Discard or ESkillActionKind.DrawByDiscard
                or ESkillActionKind.FillHand or ESkillActionKind.ModifyDrawCount or ESkillActionKind.ModifyDrawCountByDiscard ||
                action.Kind == ESkillActionKind.GainResource && ReadResourceName(mergedParams) == "energy")
                return;
        }
        switch (action.Kind)
        {
            case ESkillActionKind.SummonEnemy:
                if (BuffActionParams.TryGetString(mergedParams, "enemyId", out var summonEnemyId))
                    simulation.EnemySummons.Summon(source, summonEnemyId,
                        ContentParameters.ReadInt(mergedParams, "count", 1),
                        ContentParameters.ReadInt(mergedParams, "maxAlive", 1),
                        ContentParameters.ReadInt(mergedParams, "maxSummons", 0),
                        mergedParams.TryGetValue("trackLifeBarSummons", out var trackSummonsValue) &&
                        bool.TryParse(trackSummonsValue?.ToString(), out var trackSummonsEnabled) && trackSummonsEnabled);
                break;
            case ESkillActionKind.Draw:
                ApplyDraw(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.Discard:
                // 实际弃置张数只有 DiscardAndRecord 需要；本动作保持原语义（不记账）。
                ApplyDiscard(simulation, source, targets, ContentParameters.ReadInt(mergedParams, "count", 1), mergedParams, discardReference);
                break;
            case ESkillActionKind.DiscardAndRecord:
                simulation.SetLastDiscardCount(
                    ApplyDiscard(simulation, source, targets, ContentParameters.ReadInt(mergedParams, "count", 1), mergedParams, discardReference));
                break;
            case ESkillActionKind.GainResource:
                ApplyGainResource(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.ModifyDrawCount:
                ApplyModifyDrawCount(simulation, source, targets, mergedParams, ContentParameters.ReadInt(mergedParams, "amount", 0));
                break;
            case ESkillActionKind.ModifyDrawCountByDiscard:
                ApplyModifyDrawCountByDiscard(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.DrawByDiscard:
                ApplyDrawByDiscard(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.FillHand:
                ApplyFillHand(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.GainShield:
                ApplyGainShield(simulation, source, targets, mergedParams, ContentParameters.ReadInt(mergedParams, "amount", 0));
                break;
            case ESkillActionKind.ExecuteScript:
                ApplyExecuteScript(action, mergedParams, simulation, source, targets, depth, originCategory, inheritedOwner);
                break;
            case ESkillActionKind.ChainActions:
                foreach (var child in action.ActionRefs)
                    ExecuteSkillActionRefCore(child, simulation, source, targets, depth + 1);
                break;
            case ESkillActionKind.ApplyGameplayEffect:
                ApplyGameplayEffect(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.RemoveGameplayEffect:
                RemoveGameplayEffect(simulation, targets, mergedParams);
                break;
            case ESkillActionKind.ApplyBuff:
                ApplyBuff(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.RemoveBuff:
                RemoveBuff(simulation, targets, mergedParams);
                break;
            case ESkillActionKind.DispelDebuffs:
                var cleanseTargets = BuffActionParams.HasTargetSelector(mergedParams)
                    ? CombatTargetSelector.Resolve(simulation, source, mergedParams) : targets;
                foreach (var target in cleanseTargets.Distinct())
                    simulation.Buffs.DispelDebuffs(simulation, target);
                break;
            case ESkillActionKind.AttachSlotBuff:
            case ESkillActionKind.DiscardSlot:
                // 固定手牌槽效果已停用。引用它们的旧技能仍按原时机施放。
                break;
            case ESkillActionKind.GainOrb:
                GainOrb(simulation, source, mergedParams);
                break;
            case ESkillActionKind.SetActionCount:
                SetActionCount(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.SetDomain:
                SetDomain(simulation, mergedParams);
                break;
        }
    }

    /// <summary>
    /// 设置目标敌人的行动计数（<c>params.count</c>，缺省 2，下限 1）：计数 &gt; 1 时该敌人在接下来的
    /// 敌方阶段只递减、不行动，因此 2 = 把它的行动推迟到下个回合。非敌方目标静默跳过。
    /// </summary>
    private static void SetActionCount(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var count = ContentParameters.ReadInt(parameters, "count", 2);

        // 目标解析优先用 hookTargets / targetFilter（"随机敌方单体"这类钩子载荷），
        // 与伤害、挂 buff 同口径；没配选择器时用调用方给的 targets。
        var resolved = BuffActionParams.HasTargetSelector(parameters)
            ? CombatTargetSelector.Resolve(simulation, source, parameters)
            : targets;

        foreach (var target in resolved)
        {
            if (target.Side != ECombatSide.Enemy ||
                target.Index < 0 ||
                target.Index >= simulation.EnemyTeam.Enemies.Count)
            {
                continue;
            }

            simulation.EnemyTeam.Enemies[target.Index].ActionCount = count;
        }
    }

    /// <summary>
    /// 展开队伍领域：<c>params.gameplayEffectId</c> 必填、<c>params.turns</c> 可选（&gt; 0 = 持续回合数，
    /// 到期由 <see cref="TeamDomainManager"/> 在回合结束自动收起）。旧领域被顶替。
    /// </summary>
    private static void SetDomain(
        CombatSimulation simulation,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!BuffActionParams.TryGetString(parameters, "gameplayEffectId", out var gameplayEffectId))
            return;

        // gameplayEffectId / turns 是控制键（不是 SetByCaller 取值），不进领域实例参数。
        var instanceParams = parameters
            .Where(pair => pair.Key is not ("gameplayEffectId" or "turns"))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var turns = ContentParameters.ReadInt(parameters, "turns", 0);

        simulation.DomainManager.TrySetPlayerDomain(
            gameplayEffectId,
            instanceParams.Count > 0 ? instanceParams : null,
            turns > 0 ? turns : null);
    }

    /// <summary>
    /// 弃置来源角色指定手牌槽的牌（<c>params.slotIndex</c>，0 起）：只弃未标记的牌，
    /// 已入队/已确认的牌不动（规格 §2.2"其它通道"不得借钩子回滚确认态）。
    /// </summary>
    private void ApplyExecuteScript(
        SkillActionDto action,
        IReadOnlyDictionary<string, object> mergedParams,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth,
        EContentCategory originCategory,
        string? inheritedOwner)
    {
        if (string.IsNullOrWhiteSpace(action.ScriptPath))
            return;

        var context = BuildScriptContext(mergedParams, simulation, source);
        var entry = string.IsNullOrWhiteSpace(action.ScriptEntry) ? "execute" : action.ScriptEntry;
        var owner = inheritedOwner ?? (_registry.TryGetOwnerModId(originCategory, action.Id, out var ownerModId)
            ? ownerModId : simulation.ModId);
        if (!simulation.ScriptHost.TryExecute(
                owner,
                action.ScriptPath,
                entry,
                context,
                out var proposedEffects))
        {
            return;
        }

        ExecuteProposedEffects(proposedEffects, simulation, source, targets, depth + 1, owner);
    }

    private void ExecuteProposedEffects(
        IReadOnlyList<Dictionary<string, object>> proposedEffects,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth,
        string owner)
    {
        foreach (var proposed in proposedEffects)
        {
            if (TryGetString(proposed, "effectId", out var effectId))
            {
                _executeLegacyEffectRef(
                    new EffectRefDto
                    {
                        EffectId = effectId,
                        Params = ExtractParamsDictionary(proposed),
                    },
                    simulation,
                    source,
                    targets,
                    depth);
                continue;
            }

            if (TryGetString(proposed, "actionId", out var actionId))
            {
                ExecuteSkillActionRefCore(
                    new SkillActionRefDto
                    {
                        ActionId = actionId,
                        Params = ExtractParamsDictionary(proposed),
                    },
                    simulation,
                    source,
                    targets,
                    depth);
                continue;
            }

            if (!TryGetString(proposed, "kind", out var kindText) ||
                !Enum.TryParse<ESkillActionKind>(kindText, ignoreCase: true, out var kind))
            {
                continue;
            }

            var inlineParams = ExtractParamsDictionary(proposed) ?? new Dictionary<string, object>(StringComparer.Ordinal);
            var inlineAction = new SkillActionDto
            {
                Id = kindText,
                Kind = kind,
                Params = inlineParams,
                ScriptPath = TryGetString(proposed, "scriptPath", out var scriptPath) ? scriptPath : null,
                ScriptEntry = TryGetString(proposed, "scriptEntry", out var scriptEntry) ? scriptEntry : null,
            };
            ExecuteAction(inlineAction, inlineParams, simulation, source, targets, depth, inheritedOwner: owner);
        }
    }

    private void ApplyGameplayEffect(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!TryGetString(parameters, "gameplayEffectId", out var gameplayEffectId))
            return;

        // 动态攻击系数（"本回合每触发 1 个绿球 +100% 魔攻"）在两条通道上必须同口径，
        // 因此把覆盖值并进 SetByCaller 后再交给应用器；攻击次数（AttackCount / AttackCountByChain /
        // AttackCountMinusDiscard）同理——它声明的是"打几次"，由 DamageExecution 逐次结算。
        var setByCaller = new Dictionary<string, object>(parameters, StringComparer.Ordinal);
        DamageScaling.ApplyAttackScaleOverride(simulation, parameters, setByCaller);
        DamageScaling.ApplyAttackCountOverride(simulation, parameters, setByCaller);

        _gameplayEffectApplicator.ApplyToTargets(simulation, source, targets, gameplayEffectId, setByCaller);
    }

    private void RemoveGameplayEffect(
        CombatSimulation simulation,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!TryGetString(parameters, "gameplayEffectId", out var gameplayEffectId))
            return;

        _gameplayEffectApplicator.RemoveFromTargets(simulation, targets, gameplayEffectId);
    }

    /// <summary>
    /// 对每个目标挂 buff：params.buffId 必填，其余参数进入实例参数。
    /// 带 <c>hookTargets</c> / <c>targetFilter</c> 时按目标选择器重解析（"伤害敌方全体 + 增益自身"这类卡用）。
    /// </summary>
    private static void ApplyBuff(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!BuffActionParams.TryGetBuffId(parameters, out var buffId))
            return;

        var resolvedTargets = BuffActionParams.HasTargetSelector(parameters)
            ? CombatTargetSelector.Resolve(simulation, source, parameters)
            : targets;
        var instanceParams = BuffActionParams.BuildScaledInstanceParams(simulation, source, parameters,
            "buffId", "stacks", "stackMultiplier", "requiredEnemyTag");
        foreach (var target in resolvedTargets)
        {
            if (BuffActionParams.TryGetString(parameters, "requiredEnemyTag", out var tag) &&
                (target.Side != ECombatSide.Enemy || target.Index < 0 || target.Index >= simulation.EnemyTeam.Enemies.Count ||
                 !simulation.Definitions.Store.TryGetEnemy(simulation.EnemyTeam.Enemies[target.Index].DefinitionId, out var definition) ||
                 !definition.Tags.Contains(tag)))
                continue;
            var count = Math.Clamp(ContentParameters.ReadInt(parameters, "stacks", 1), 1, 100);
            if (parameters.ContainsKey("stackMultiplier"))
            {
                var current = target.Side == ECombatSide.Enemy && target.Index >= 0 && target.Index < simulation.EnemyTeam.Enemies.Count
                    ? simulation.EnemyTeam.Enemies[target.Index].Buffs.Find(buffId)?.Stacks ?? 0
                    : target.Side == ECombatSide.Player && target.Index >= 0 && target.Index < simulation.PlayerTeam.Characters.Count
                        ? simulation.PlayerTeam.Characters[target.Index].Buffs.Find(buffId)?.Stacks ?? 0 : 0;
                count = (int)Math.Min(100, (long)current * (Math.Clamp(ContentParameters.ReadInt(parameters, "stackMultiplier", 1), 1, 100) - 1));
            }
            for (var stack = 0; stack < count; stack++)
                simulation.Buffs.Apply(simulation, target, buffId, instanceParams);
        }
    }

    /// <summary>授予充能球：params.orbTypeId 必填、params.count 可选（默认 1）；产球者 = 来源角色。</summary>
    private static void GainOrb(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!BuffActionParams.TryGetString(parameters, "orbTypeId", out var orbTypeId))
            return;

        var count = ContentParameters.ReadInt(parameters, "count", 1);
        simulation.Orbs.Grant(simulation, orbTypeId, BuffActionParams.ResolveProducerIndex(source), count);
    }

    /// <summary>驱散目标 buff（params.buffId 或 params.withTags）；不可驱散 tag 自动跳过。</summary>
    private static void RemoveBuff(
        CombatSimulation simulation,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        BuffActionParams.TryGetBuffId(parameters, out var buffId);
        var withTags = BuffActionParams.TryGetTagList(parameters, "withTags");
        if (string.IsNullOrWhiteSpace(buffId) && withTags is null)
            return;

        foreach (var target in targets)
            simulation.Buffs.Dispel(simulation, target, buffId, withTags);
    }

    private static Dictionary<string, object>? BuildScriptContext(
        IReadOnlyDictionary<string, object> mergedParams,
        CombatSimulation simulation,
        CombatTargetRef source)
    {
        var context = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["runSeed"] = simulation.RunSeed,
            ["sourceSide"] = source.Side.ToString(),
            ["sourceIndex"] = source.Index,
        };

        foreach (var (key, value) in mergedParams)
            context[key] = value;

        return context;
    }

    private static void ApplyDraw(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var count = ContentParameters.ReadInt(parameters, "count", 1);
        var hasSelector = BuffActionParams.HasTargetSelector(parameters);
        var resolved = hasSelector ? CombatTargetSelector.Resolve(simulation, source, parameters) : targets;
        DrawForPlayerTargets(simulation, resolved, hasSelector, count);
    }

    /// <summary>将受影响角色的手牌补到上限。</summary>
    private static void ApplyFillHand(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var hasSelector = BuffActionParams.HasTargetSelector(parameters);
        var resolved = hasSelector ? CombatTargetSelector.Resolve(simulation, source, parameters) : targets;
        if (hasSelector && resolved.Count == 0)
            return;

        foreach (var character in ResolvePlayerCharacters(simulation, source, resolved, allowSourceFallback: !hasSelector))
            PresentationEmitter.DrawAndEmit(simulation, IndexOfCharacter(simulation, character),
                simulation.PlayerTeam.CardPiles.RemainingHandSpace);
    }

    /// <summary>规格 §4.3：按实际弃牌数即时补抽，保留弃牌记录供后续伤害读取。</summary>
    private static void ApplyDrawByDiscard(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var amount = (int)Math.Min(int.MaxValue,
            (long)simulation.LastDiscardCount * Math.Max(0, ContentParameters.ReadInt(parameters, "perCard", 1)));
        if (amount <= 0)
            return;

        var hasSelector = BuffActionParams.HasTargetSelector(parameters);
        var resolvedTargets = hasSelector
            ? CombatTargetSelector.Resolve(simulation, source, parameters)
            : targets;

        DrawForPlayerTargets(simulation, resolvedTargets, hasSelector, amount);
    }

    /// <summary>
    /// 按技能的显式目标抽牌：每个被点名的角色各抽 <paramref name="count"/> 张，并只取该角色归属的牌；
    /// 没有显式玩家目标时（队伍抽牌 / 仅敌方目标）从共享牌堆抽一次，不限定归属。
    /// 选择器显式筛选但未命中玩家时绝不回落到来源，避免把 AoE / 敌方技能误奖励给施法者。
    /// </summary>
    private static void DrawForPlayerTargets(
        CombatSimulation simulation,
        IReadOnlyList<CombatTargetRef> resolved,
        bool hasSelector,
        int count)
    {
        if (count <= 0)
            return;

        var characterCount = simulation.PlayerTeam.Characters.Count;
        var playerIndices = resolved
            .Where(target => target.Side == ECombatSide.Player && target.Index >= 0 && target.Index < characterCount)
            .Select(target => target.Index)
            .Distinct()
            .Order()
            .ToArray();
        if (playerIndices.Length > 0)
        {
            foreach (var index in playerIndices)
                PresentationEmitter.DrawTeamAndEmit(simulation, count, requiredOwner: index);
            return;
        }

        if (hasSelector)
            return;

        PresentationEmitter.DrawTeamAndEmit(simulation, count, requiredOwner: null);
    }

    /// <summary>规格 §4.6：按 <see cref="CombatSimulation.CurrentDiscardChannel"/> 分流弃牌。</summary>
    /// <returns>
    /// 实际移入弃牌堆的总张数（可弃池不足时小于 <paramref name="count"/>，目标多名角色时为各角色之和）。
    /// <see cref="ESkillActionKind.Discard"/> 忽略它，<see cref="ESkillActionKind.DiscardAndRecord"/> 用它记账。
    /// </returns>
    private static int ApplyDiscard(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int count,
        IReadOnlyDictionary<string, object> parameters,
        object? reference)
    {
        if (count <= 0)
            return 0;

        var random = DiscardSelection.ReadFlag(parameters, "random");
        if (!random && (simulation.CurrentDiscardChannel != EDiscardChannel.ActiveSkill ||
            simulation.SelectedDiscardCards is null && simulation.PlannedDiscards is null))
        {
            simulation.CountBlockedUnselectedDiscard();
            return 0;
        }

        return DiscardSelection.ResolvePools(simulation, source, targets, parameters)
            .Sum(owners => DiscardFromPool(simulation, owners, count, random, reference));
    }

    private static int IndexOfCharacter(CombatSimulation simulation, CharacterBattleInstance character)
    {
        var characters = simulation.PlayerTeam.Characters;
        for (var i = 0; i < characters.Count; i++)
        {
            if (ReferenceEquals(characters[i], character))
                return i;
        }

        return -1;
    }

    private static int DiscardFromPool(CombatSimulation simulation, IReadOnlyList<int> owners, int count, bool random, object? reference)
    {
        var characters = simulation.PlayerTeam.Characters;
        IReadOnlyList<HandCardChoice>? selected = simulation.SelectedDiscardCards;
        if (!random && simulation.PlannedDiscards is { } plan)
        {
            var index = plan.FindIndex(group => ReferenceEquals(group.Requirement.Reference, reference) &&
                group.Requirement.Owners.SequenceEqual(owners));
            if (index < 0)
                return 0;
            selected = plan[index].Cards;
            plan.RemoveAt(index);
        }
        var pool = owners.SelectMany(owner => characters[owner].HandSlots
            .Where(slot => !slot.IsEmpty)
            .Select(slot => new HandCardChoice(owner, slot.RuntimeInstanceId!))).ToList();
        if (random && simulation.PlannedDiscards is { Count: > 0 } remaining)
        {
            var reserved = remaining.SelectMany(group => group.Cards).ToHashSet();
            pool.RemoveAll(reserved.Contains);
        }
        var discarded = 0;
        while (discarded < count && pool.Count > 0)
        {
            var index = random
                ? simulation.DiscardRng.NextInt(0, pool.Count)
                : pool.IndexOf(selected!.FirstOrDefault(pool.Contains));
            if (index < 0)
                break;

            var card = pool[index];
            pool.RemoveAt(index);
            var character = characters[card.CharacterIndex];
            var slot = character.HandSlots.First(candidate => candidate.RuntimeInstanceId == card.RuntimeInstanceId);
            var evt = new CardDiscardedEvent(card.CharacterIndex, slot.SlotIndex, slot.CardId!, simulation.CurrentDiscardChannel, card.RuntimeInstanceId);
            if (!character.MoveHandCardToGraveyard(card.RuntimeInstanceId))
                continue;

            simulation.SelectedDiscardCards?.Remove(card);
            simulation.Presentation.Emit(evt);
            discarded++;
        }

        return discarded;
    }

    /// <summary>规格 §4.3：投放抽牌数量修正，供阶段开始公式取最大 ±N。</summary>
    /// <remarks>
    /// 带 <c>hookTargets</c> / <c>targetFilter</c> 时按目标选择器重解析（<c>hookTargets: "allies"</c>
    /// = 己方全体逐个投放，2026-09-25 参宿四的「己方全体抽卡 +N」用它）；
    /// 缺省沿用卡牌/技能解析出的目标。
    /// </remarks>
    private static void ApplyModifyDrawCount(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters,
        int amount)
    {
        if (amount == 0)
            return;

        var resolvedTargets = BuffActionParams.HasTargetSelector(parameters)
            ? CombatTargetSelector.Resolve(simulation, source, parameters)
            : targets;

        foreach (var character in ResolvePlayerCharacters(simulation, source, resolvedTargets,
                     allowSourceFallback: !BuffActionParams.HasTargetSelector(parameters)))
            character.AddDrawModifier(amount);
    }

    /// <summary>
    /// 按"最近一次 <see cref="ESkillActionKind.DiscardAndRecord"/> 实际弃置的张数"投放抽牌数量修正
    /// （2026-09-27「弃 X 张，则下次抽牌 +X」）：增量 = <see cref="CombatSimulation.LastDiscardCount"/> ×
    /// <c>params.perCard</c>（缺省 1，负值按 0）；<c>additive: true</c> 则与其他抽牌效果累加。
    /// </summary>
    /// <remarks>
    /// 目标解析与 <see cref="ApplyModifyDrawCount"/> 同口径：带 <c>hookTargets</c> / <c>targetFilter</c> 时按
    /// 目标选择器重解析，显式筛选未命中时不生效；缺省沿用卡牌/技能解析出的目标（无玩家目标时回退到来源角色）。
    /// 读取<b>不清账</b>——同一回合内的其它读取方（如按弃牌数扣减行动次数）仍能看到同一数值；
    /// 弃牌记录由 <c>PlayerPhasePipeline</c> 在每个玩家阶段开始时清零，弃 0 张 / 本回合没弃过牌时零操作。
    /// </remarks>
    private static void ApplyModifyDrawCountByDiscard(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var amount = (int)Math.Min(int.MaxValue, (long)simulation.LastDiscardCount * Math.Max(0, ContentParameters.ReadInt(parameters, "perCard", 1)));
        if (amount <= 0)
            return;

        var resolvedTargets = BuffActionParams.HasTargetSelector(parameters)
            ? CombatTargetSelector.Resolve(simulation, source, parameters)
            : targets;

        var additive = parameters.TryGetValue("additive", out var value) && bool.TryParse(value?.ToString(), out var flag) && flag;
        foreach (var character in ResolvePlayerCharacters(simulation, source, resolvedTargets,
                     allowSourceFallback: !BuffActionParams.HasTargetSelector(parameters)))
        {
            if (additive)
                character.AddExtraDrawModifier(amount);
            else
                character.AddDrawModifier(amount);
        }
    }

    /// <summary>
    /// 授予护盾（2026-09-26）：<c>params.amount</c> 加到目标玩家角色的护盾属性上（可叠加、无上限）。
    /// 带 <c>hookTargets</c> / <c>targetFilter</c> 时按目标选择器重解析（「己方 1 人获得 100 点护盾」用卡牌
    /// 点选出的目标；buff 钩子缺省落在持有者自身）。
    /// </summary>
    /// <remarks>
    /// 写属性 <b>base</b> 值（<see cref="KemoCard.Frame.Gas.AbilitySystemComponent.SetBaseValue"/>）：
    /// 增加基础授予量，聚合后的可用余额由 Aggregator 的消耗记录扣减；不能把 current 的修饰值再写入 base。
    /// </remarks>
    private static void ApplyGainShield(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters,
        int amount)
    {
        if (amount == 0)
            return;

        var resolvedTargets = BuffActionParams.HasTargetSelector(parameters)
            ? CombatTargetSelector.Resolve(simulation, source, parameters)
            : targets;

        foreach (var character in ResolvePlayerCharacters(simulation, source, resolvedTargets,
                     allowSourceFallback: !BuffActionParams.HasTargetSelector(parameters)))
        {
            var asc = character.Asc;
            asc.SetBaseValue(AttributeIds.Shield, asc.GetBaseValue(AttributeIds.Shield) + amount);
        }
    }

    /// <summary>
    /// 资源给到「当前可用能量」（规格 §3.2），或按 <c>resource: "SkillCounter"</c> 显式加技能计数器
    /// <c>S</c>（规格 §5.3 连发）；<c>CurrentEnergy</c> 的负数减少每回合能量额度与可用余额。
    /// </summary>
    private static void ApplyGainResource(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var amount = ContentParameters.ReadInt(parameters, "amount", 0);
        if (amount == 0)
            return;

        var hasSelector = BuffActionParams.HasTargetSelector(parameters);
        var resolvedTargets = hasSelector ? CombatTargetSelector.Resolve(simulation, source, parameters) : targets;
        // 显式筛选未命中时不能回落到来源，否则属性/种族充能会错误奖励施法者。
        if (hasSelector && resolvedTargets.Count == 0)
            return;

        switch (ReadResourceName(parameters))
        {
            case "energy":
                foreach (var character in ResolvePlayerCharacters(simulation, source, resolvedTargets, allowSourceFallback: !hasSelector))
                {
                    if (amount > 0)
                        character.GainAvailableEnergy(amount);
                }
                break;
            case "currentenergy":
                foreach (var character in ResolvePlayerCharacters(simulation, source, resolvedTargets, allowSourceFallback: !hasSelector))
                {
                    character.ReduceCurrentEnergy(amount < 0 ? (int)Math.Min(int.MaxValue, -(long)amount) : 0);
                    var index = Enumerable.Range(0, simulation.PlayerTeam.Characters.Count)
                        .First(i => ReferenceEquals(simulation.PlayerTeam.Characters[i], character));
                    PresentationEmitter.EmitEnergy(simulation, index);
                }
                break;
            case "skillcounter":
                if (amount < 0)
                    break;
                foreach (var character in ResolvePlayerCharacters(simulation, source, resolvedTargets, allowSourceFallback: !hasSelector))
                    character.GainSkillCounter(amount);
                break;
        }
    }

    private static IEnumerable<CharacterBattleInstance> ResolvePlayerCharacters(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        bool allowSourceFallback = true)
    {
        var indices = new SortedSet<int>();
        foreach (var target in targets)
        {
            if (target.Side == ECombatSide.Player && target.Index >= 0)
                indices.Add(target.Index);
        }

        if (allowSourceFallback && indices.Count == 0 && source.Side == ECombatSide.Player && source.Index >= 0)
            indices.Add(source.Index);

        foreach (var index in indices)
        {
            if (index < simulation.PlayerTeam.Characters.Count)
                yield return simulation.PlayerTeam.Characters[index];
        }
    }

    /// <summary>缺省资源为 Energy（保持既有内容不写 <c>resource</c> 时的行为）。</summary>
    private static string ReadResourceName(IReadOnlyDictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("resource", out var value) || value is null)
            return "energy";

        return (value.ToString() ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static Dictionary<string, object>? ExtractParamsDictionary(IReadOnlyDictionary<string, object> proposed)
    {
        if (!proposed.TryGetValue("params", out var value) || value is null)
            return null;

        return value switch
        {
            Dictionary<string, object> dict => new Dictionary<string, object>(dict, StringComparer.Ordinal),
            IReadOnlyDictionary<string, object> readOnly =>
                new Dictionary<string, object>(readOnly, StringComparer.Ordinal),
            _ => null,
        };
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