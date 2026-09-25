using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
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
        IReadOnlyList<CombatTargetRef> targets)
    {
        ExecuteSkillActionRefCore(actionRef, simulation, source, targets, depth: 0);
    }

    private void ExecuteSkillActionRefCore(
        SkillActionRefDto actionRef,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth)
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

        var mergedParams = MergeParams(action.Params, actionRef.Params);
        ExecuteAction(action, mergedParams, simulation, source, targets, depth);
    }

    public void ExecuteLegacyAction(
        EEffectKind kind,
        EffectDto effect,
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
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            },
            ScriptPath = effect.ScriptPath,
            ScriptEntry = effect.ScriptEntry,
            ActionRefs = kind == EEffectKind.ChainEffects
                ? []
                : [],
        };

        if (kind == EEffectKind.ChainEffects)
        {
            foreach (var child in effect.EffectRefs)
                _executeLegacyEffectRef(child, simulation, source, targets, depth + 1);
            return;
        }

        ExecuteAction(legacyAction, mergedParams, simulation, source, targets, depth);
    }

    private void ExecuteAction(
        SkillActionDto action,
        IReadOnlyDictionary<string, object> mergedParams,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int depth)
    {
        switch (action.Kind)
        {
            case ESkillActionKind.Draw:
                ApplyDraw(simulation, source, targets, ReadInt(mergedParams, "count", 1));
                break;
            case ESkillActionKind.Discard:
                ApplyDiscard(simulation, source, targets, ReadInt(mergedParams, "count", 1));
                break;
            case ESkillActionKind.GainResource:
                ApplyGainResource(simulation, source, targets, mergedParams);
                break;
            case ESkillActionKind.ModifyDrawCount:
                ApplyModifyDrawCount(simulation, source, targets, mergedParams, ReadInt(mergedParams, "amount", 0));
                break;
            case ESkillActionKind.ExecuteScript:
                ApplyExecuteScript(action, mergedParams, simulation, source, targets);
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
            case ESkillActionKind.AttachSlotBuff:
                AttachSlotBuff(simulation, source, mergedParams);
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
            case ESkillActionKind.DiscardSlot:
                DiscardSlot(simulation, source, mergedParams);
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
        var count = ReadInt(parameters, "count", 2);

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
        var turns = ReadInt(parameters, "turns", 0);

        simulation.DomainManager.TrySetPlayerDomain(
            gameplayEffectId,
            instanceParams.Count > 0 ? instanceParams : null,
            turns > 0 ? turns : null);
    }

    /// <summary>
    /// 弃置来源角色指定手牌槽的牌（<c>params.slotIndex</c>，0 起）：只弃未标记的牌，
    /// 已入队/已确认的牌不动（规格 §2.2"其它通道"不得借钩子回滚确认态）。
    /// </summary>
    private static void DiscardSlot(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object> parameters)
    {
        var slotIndex = ReadInt(parameters, "slotIndex", -1);
        if (source.Side != ECombatSide.Player ||
            source.Index < 0 ||
            source.Index >= simulation.PlayerTeam.Characters.Count)
        {
            return;
        }

        var character = simulation.PlayerTeam.Characters[source.Index];
        var before = PresentationEmitter.SnapshotHand(character);
        character.DiscardSlotCard(slotIndex);
        PresentationEmitter.EmitDiscardsByDiff(simulation, source.Index, before, simulation.CurrentDiscardChannel);
    }

    private void ApplyExecuteScript(
        SkillActionDto action,
        IReadOnlyDictionary<string, object> mergedParams,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets)
    {
        if (string.IsNullOrWhiteSpace(action.ScriptPath))
            return;

        var context = BuildScriptContext(mergedParams, simulation, source);
        var entry = string.IsNullOrWhiteSpace(action.ScriptEntry) ? "execute" : action.ScriptEntry;
        if (!simulation.ScriptHost.TryExecute(
                simulation.ModId,
                action.ScriptPath,
                entry,
                context,
                out var proposedEffects))
        {
            return;
        }

        ExecuteProposedEffects(proposedEffects, simulation, source, targets);
    }

    private void ExecuteProposedEffects(
        IReadOnlyList<Dictionary<string, object>> proposedEffects,
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets)
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
                    0);
                continue;
            }

            if (TryGetString(proposed, "actionId", out var actionId))
            {
                ExecuteSkillActionRef(
                    new SkillActionRefDto
                    {
                        ActionId = actionId,
                        Params = ExtractParamsDictionary(proposed),
                    },
                    simulation,
                    source,
                    targets);
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
            ExecuteAction(inlineAction, inlineParams, simulation, source, targets, depth: 0);
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
        // 因此把覆盖值并进 SetByCaller 后再交给应用器。
        var setByCaller = new Dictionary<string, object>(parameters, StringComparer.Ordinal);
        DamageScaling.ApplyAttackScaleOverride(simulation, parameters, setByCaller);

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
        var instanceParams = BuffActionParams.BuildInstanceParams(parameters, "buffId");
        foreach (var target in resolvedTargets)
            simulation.Buffs.Apply(simulation, target, buffId, instanceParams);
    }

    /// <summary>授予充能球：params.orbTypeId 必填、params.count 可选（默认 1）；产球者 = 来源角色。</summary>
    private static void GainOrb(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (!BuffActionParams.TryGetString(parameters, "orbTypeId", out var orbTypeId))
            return;

        var count = BuffActionParams.ReadInt(parameters, "count", 1);
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

    /// <summary>
    /// 给来源角色的手牌槽位挂 buff：<c>params.buffId</c> + 槽位选择。
    /// 槽位选择：<c>params.slotIndex</c>（0 起，显式指定）；
    /// <c>params.slotSelection: "randomNonEmpty"</c>（当前有牌的槽里随机一个，用于「随机一张手牌费用变为 0」）；
    /// <c>params.slotSelection: "all"</c>（全部手牌槽，空槽也挂，用于「1~5 号槽都获得充能」）。都不给时零操作。
    /// </summary>
    private static void AttachSlotBuff(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object> parameters)
    {
        if (source.Side != ECombatSide.Player ||
            source.Index < 0 || source.Index >= simulation.PlayerTeam.Characters.Count)
            return;

        if (!BuffActionParams.TryGetBuffId(parameters, out var buffId))
            return;

        var instanceParams = BuffActionParams.BuildInstanceParams(parameters, "buffId", "slotIndex", "slotSelection");
        foreach (var slotIndex in ResolveSlotIndexes(simulation, source.Index, parameters))
        {
            simulation.Buffs.ApplyToSlot(simulation, source.Index, slotIndex, buffId, instanceParams);
        }
    }

    /// <summary>解析目标槽位集合：显式 <c>slotIndex</c> &gt; <c>slotSelection</c>（randomNonEmpty / all）。</summary>
    private static IEnumerable<int> ResolveSlotIndexes(
        CombatSimulation simulation,
        int characterIndex,
        IReadOnlyDictionary<string, object> parameters)
    {
        var explicitIndex = ReadInt(parameters, "slotIndex", -1);
        if (explicitIndex >= 0)
        {
            yield return explicitIndex;
            yield break;
        }

        if (!BuffActionParams.TryGetString(parameters, "slotSelection", out var selection))
            yield break;

        if (string.Equals(selection, "all", StringComparison.OrdinalIgnoreCase))
        {
            var slotCount = simulation.PlayerTeam.Characters[characterIndex].HandSlots.Count;
            for (var index = 0; index < slotCount; index++)
                yield return index;
            yield break;
        }

        if (!string.Equals(selection, "randomNonEmpty", StringComparison.OrdinalIgnoreCase))
            yield break;

        var candidates = new List<int>();
        var slots = simulation.PlayerTeam.Characters[characterIndex].HandSlots;
        for (var index = 0; index < slots.Count; index++)
        {
            if (!slots[index].IsEmpty)
                candidates.Add(index);
        }

        if (candidates.Count > 0)
            yield return candidates[simulation.RetargetRng.NextInt(0, candidates.Count)];
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

    /// <summary>规格 §4.3：禁止战斗中途即时抽牌；计诊断并无操作。</summary>
    private static void ApplyDraw(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int count)
    {
        _ = source;
        _ = targets;
        _ = count;
        simulation.CountBlockedMidDraw();
    }

    /// <summary>规格 §4.6：按 <see cref="CombatSimulation.CurrentDiscardChannel"/> 分流弃牌。</summary>
    private static void ApplyDiscard(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        int count)
    {
        if (count <= 0)
            return;

        foreach (var character in ResolvePlayerCharacters(simulation, source, targets))
        {
            var before = PresentationEmitter.SnapshotHand(character);
            if (simulation.CurrentDiscardChannel == EDiscardChannel.ActiveSkill)
                DiscardViaActiveSkillChannel(simulation, character, count);
            else
                character.DiscardRandomUnmarked(count, simulation.DiscardRng);

            var characterIndex = IndexOfCharacter(simulation, character);
            PresentationEmitter.EmitDiscardsByDiff(simulation, characterIndex, before, simulation.CurrentDiscardChannel);
        }
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

    /// <summary>
    /// ActiveSkill 通道：均匀随机可含已标记；命中标记则取消、退 <c>paid</c>、回退未确认，再进弃牌堆。
    /// </summary>
    private static void DiscardViaActiveSkillChannel(
        CombatSimulation simulation,
        CharacterBattleInstance character,
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            var slot = character.PickRandomOccupiedSlot(simulation.DiscardRng);
            if (slot is null)
                return;

            var runtimeId = slot.RuntimeInstanceId!;
            if (slot.IsMarked)
            {
                var entry = simulation.CardQueue
                    .PeekAllOrdered()
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.RuntimeInstanceId, runtimeId, StringComparison.Ordinal));
                if (entry is not null)
                    CombatStateMachine.CancelMarkAndRefund(simulation, entry);
                character.SetHasActed(false);
            }

            character.MoveHandCardToGraveyard(runtimeId);
        }
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

        foreach (var character in ResolvePlayerCharacters(simulation, source, resolvedTargets))
            character.AddDrawModifier(amount);
    }

    /// <summary>
    /// 资源给到「当前可用能量」（规格 §3.2），或按 <c>resource: "SkillCounter"</c> 显式加技能计数器
    /// <c>S</c>（规格 §5.3 连发）。其余资源名 v1 无操作。
    /// </summary>
    private static void ApplyGainResource(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        IReadOnlyDictionary<string, object> parameters)
    {
        var amount = ReadInt(parameters, "amount", 0);
        if (amount <= 0)
            return;

        switch (ReadResourceName(parameters))
        {
            case "energy":
                foreach (var character in ResolvePlayerCharacters(simulation, source, targets))
                    character.GainAvailableEnergy(amount);
                break;
            case "skillcounter":
                foreach (var character in ResolvePlayerCharacters(simulation, source, targets))
                    character.GainSkillCounter(amount);
                break;
        }
    }

    private static IEnumerable<CharacterBattleInstance> ResolvePlayerCharacters(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets)
    {
        var indices = new SortedSet<int>();
        foreach (var target in targets)
        {
            if (target.Side == ECombatSide.Player && target.Index >= 0)
                indices.Add(target.Index);
        }

        if (indices.Count == 0 && source.Side == ECombatSide.Player && source.Index >= 0)
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

    private static int ReadInt(IReadOnlyDictionary<string, object> parameters, string key, int defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return defaultValue;

        return value switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetInt32(),
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue,
        };
    }
}