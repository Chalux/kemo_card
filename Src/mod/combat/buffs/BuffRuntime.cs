using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Buffs;

public sealed record BuffApplyResult(bool Success, BuffInstance? Instance = null, string? Error = null);

/// <summary>
/// Buff 运行时编排：投放（叠层/互斥/范围展开）、钩子节点分发、时长 tick、驱散。
/// 挂点三类：角色容器、敌人容器、手牌槽位容器；修正走 ASC 聚合器句柄，休眠=撤销句柄。
/// 钩子效果的目标由效果参数决定：<c>hookTargets</c>（self/randomEnemy/allEnemies）与
/// <c>targetFilter</c>（self/elementAny/raceAny 筛选玩家角色）。
/// </summary>
public sealed class BuffRuntime
{
    private readonly GameDefinitionRegistry _registry;
    private readonly CombatEffectExecutor _executor;

    public BuffRuntime(GameDefinitionRegistry registry, CombatEffectExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(executor);
        _registry = registry;
        _executor = executor;
    }

    #region 投放

    /// <summary>
    /// 对目标挂 buff。<c>applyScope: AllAllies</c> 的团队型 buff 会展开到全部玩家角色
    /// （各自的容器独立叠层；条件 buff 靠休眠机制决定生效与否）。
    /// </summary>
    public BuffApplyResult Apply(
        CombatSimulation simulation,
        CombatTargetRef holder,
        string buffId,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentException.ThrowIfNullOrWhiteSpace(buffId);

        if (!_registry.Store.TryGetBuff(buffId, out var def))
            return new BuffApplyResult(false, Error: $"Unknown buffId '{buffId}'.");

        if (def.ApplyScope == EBuffApplyScope.AllAllies && holder.Side == ECombatSide.Player)
        {
            BuffInstance? last = null;
            for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
            {
                var result = ApplyToHolder(simulation, new CombatTargetRef(ECombatSide.Player, i), def, parameters);
                if (result.Instance is not null)
                    last = result.Instance;
            }

            return new BuffApplyResult(true, last);
        }

        return ApplyToHolder(simulation, holder, def, parameters);
    }

    /// <summary>对角色手牌槽位挂 buff（充能 / 槽位伤害等）；槽位容器无 ASC，只承载钩子与 tag。</summary>
    public BuffApplyResult ApplyToSlot(
        CombatSimulation simulation,
        int characterIndex,
        int slotIndex,
        string buffId,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentException.ThrowIfNullOrWhiteSpace(buffId);

        if (!TryGetCharacter(simulation, characterIndex, out var character, out var error) ||
            slotIndex < 0 || slotIndex >= character.HandSlots.Count)
            return new BuffApplyResult(false, Error: error ?? "槽位索引无效。");

        if (!_registry.Store.TryGetBuff(buffId, out var def))
            return new BuffApplyResult(false, Error: $"Unknown buffId '{buffId}'.");

        var container = character.HandSlots[slotIndex].Buffs;
        var instance = StackOrAdd(simulation, container, new CombatTargetRef(ECombatSide.Player, characterIndex), def, parameters);
        return new BuffApplyResult(true, instance);
    }

    private BuffApplyResult ApplyToHolder(
        CombatSimulation simulation,
        CombatTargetRef holder,
        BuffDto def,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var container = TryResolveContainer(simulation, holder);
        if (container is null)
            return new BuffApplyResult(false, Error: "目标没有 buff 容器（队伍账本不是合法挂点）。");

        var instance = StackOrAdd(simulation, container, holder, def, parameters);
        return new BuffApplyResult(true, instance);
    }

    /// <summary>叠层/互斥/新建的统一入口；onApply / onRemove / onStackChanged 钩子在此补发。</summary>
    private BuffInstance StackOrAdd(
        CombatSimulation simulation,
        BuffContainer container,
        CombatTargetRef holder,
        BuffDto def,
        IReadOnlyDictionary<string, object>? parameters)
    {
        // 充能互斥优先于一切：同一槽位只允许 1 个充能，新充能无条件覆盖旧的并重置进度。
        RemoveExistingCharges(simulation, container, holder, def);

        if (!string.IsNullOrWhiteSpace(def.ExclusiveGroup))
        {
            foreach (var removed in container.RemoveExclusiveGroup(def.ExclusiveGroup))
                FireHook(simulation, holder, removed, removed.Def.Hooks.OnRemove);
        }

        var existing = container.Find(def.Id);
        if (existing is not null)
        {
            switch (def.StackRule)
            {
                case EBuffStackRule.Add:
                    var stacksBefore = existing.Stacks;
                    existing.AddStack(def.MaxStacks);
                    if (existing.Stacks != stacksBefore)
                    {
                        existing.RegisterModifiers(GetAsc(simulation, holder));
                        FireHook(simulation, holder, existing, def.Hooks.OnStackChanged);
                    }
                    return existing;
                case EBuffStackRule.Refresh:
                    existing.RefreshDuration();
                    return existing;
                case EBuffStackRule.Replace:
                    FireHook(simulation, holder, existing, def.Hooks.OnRemove);
                    container.Remove(existing);
                    break;
            }
        }

        var instance = container.Add(def, FilterInstanceParams(parameters));
        FireHook(simulation, holder, instance, def.Hooks.OnApply);
        return instance;
    }

    /// <summary>
    /// 充能互斥（充能规格 §2）：同一槽位<b>只允许存在 1 个</b> <see cref="BuiltinBuffTags.SlotCharge"/> buff。
    /// 新的充能<b>无条件覆盖</b>旧的——即使 new 与 old 完全同 id、也即使 <c>stackRule</c> 写的是
    /// Refresh/Add——并且<b>进度重置</b>（充能计数回到新 buff 声明的值）。
    /// 覆盖时对旧实例补发 onRemove，与其它移除路径口径一致。
    /// </summary>
    private void RemoveExistingCharges(
        CombatSimulation simulation,
        BuffContainer container,
        CombatTargetRef holder,
        BuffDto def)
    {
        if (!def.EffectiveTags.Contains(BuiltinBuffTags.SlotCharge, StringComparer.Ordinal))
            return;

        var existing = container.All
            .Where(instance => instance.Def.EffectiveTags.Contains(BuiltinBuffTags.SlotCharge, StringComparer.Ordinal))
            .ToList();
        foreach (var instance in existing)
        {
            FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
            container.Remove(instance);
        }
    }

    /// <summary>驱散：按 buffId 或 tag 集匹配；带 <see cref="BuiltinBuffTags.Undispellable"/> 的一律跳过。</summary>
    public int Dispel(
        CombatSimulation simulation,
        CombatTargetRef holder,
        string? buffId = null,
        IReadOnlyList<string>? withTags = null)
    {
        var container = TryResolveContainer(simulation, holder);
        if (container is null)
            return 0;

        var toRemove = container.All.Where(instance => MatchesDispel(instance, buffId, withTags)).ToList();
        foreach (var instance in toRemove)
        {
            FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
            container.Remove(instance);
        }

        return toRemove.Count;
    }

    private static bool MatchesDispel(BuffInstance instance, string? buffId, IReadOnlyList<string>? withTags)
    {
        if (instance.Def.EffectiveTags.Contains(BuiltinBuffTags.Undispellable))
            return false;

        if (!string.IsNullOrWhiteSpace(buffId) &&
            string.Equals(instance.Def.Id, buffId, StringComparison.Ordinal))
            return true;

        if (withTags is { Count: > 0 } &&
            withTags.Any(tag => instance.Def.EffectiveTags.Contains(tag, StringComparer.Ordinal)))
            return true;

        return false;
    }

    #endregion

    #region 钩子节点

    /// <summary>回合开始：重估休眠 + 触发 onTurnStart（支持 per-effect <c>turnInterval</c> 按波内回合计数分档触发）。</summary>
    public void FireTurnStart(CombatSimulation simulation)
    {
        foreach (var (holder, container) in EnumerateContainers(simulation))
        {
            container.EvaluateDormancy();
            // 钩子可能对自己容器挂/删 buff，必须快照枚举（活列表枚举中修改会抛异常）。
            foreach (var instance in container.All.ToArray())
            {
                if (instance.IsDormant)
                    continue;
                instance.ResetTurnFlags();
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnTurnStart, gateTurnInterval: true);
            }
        }
    }

    /// <summary>本回合全部卡牌结算结束（普攻之前）：触发所有角色的 onCardExecutionEnd。</summary>
    public void FireCardExecutionEnd(CombatSimulation simulation)
    {
        FireForAllPlayerCharacters(simulation, instance => instance.Def.Hooks.OnCardExecutionEnd);
    }

    /// <summary>
    /// 充能球触发结算后：对<b>参与本次产球</b>的角色各触发一次 onOrbTriggered
    /// （素材按产球者归属，去重；配合 <c>oncePerTurn</c> 即"每回合仅 1 次"）。
    /// </summary>
    public void FireOrbTriggered(CombatSimulation simulation, IReadOnlyCollection<int> producerIndexes)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(producerIndexes);

        foreach (var index in producerIndexes.Distinct().OrderBy(value => value))
        {
            if (!TryGetCharacter(simulation, index, out var character, out _))
                continue;

            var holder = new CombatTargetRef(ECombatSide.Player, index);
            foreach (var instance in character.Buffs.All.ToArray())
            {
                if (instance.IsDormant)
                    continue;
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnOrbTriggered);
            }
        }
    }

    private void FireForAllPlayerCharacters(
        CombatSimulation simulation,
        Func<BuffInstance, IReadOnlyList<EffectRefDto>> selectHooks)
    {
        for (var index = 0; index < simulation.PlayerTeam.Characters.Count; index++)
        {
            if (!TryGetCharacter(simulation, index, out var character, out _))
                continue;

            var holder = new CombatTargetRef(ECombatSide.Player, index);
            foreach (var instance in character.Buffs.All.ToArray())
            {
                if (instance.IsDormant)
                    continue;
                FireHook(simulation, holder, instance, selectHooks(instance));
            }
        }
    }

    /// <summary>回合结束：先触发 onTurnEnd，再递减时长，到期者触发 onRemove 并移除。</summary>
    public void FireTurnEnd(CombatSimulation simulation)
    {
        foreach (var (holder, container) in EnumerateContainers(simulation))
        {
            // 以触发前的快照为本回合基准：钩子期间新增的 buff 本回合不 tick（刚挂上不应立刻扣时长）。
            var instances = container.All.ToArray();

            foreach (var instance in instances)
            {
                if (instance.IsDormant)
                    continue;
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnTurnEnd);
            }

            foreach (var instance in instances)
                instance.TickTurnEnd();

            var expired = instances.Where(instance => instance.IsExpired).ToList();
            foreach (var instance in expired)
            {
                // 钩子期间可能已被驱散（不在容器里了）：只对仍持有的实例补发 onRemove。
                if (!container.All.Contains(instance))
                    continue;

                FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
                container.Remove(instance);
            }
        }
    }

    /// <summary>波次（阶层）开始：触发 onWaveStart。第一波在 RunBattleStart 由状态机补发。</summary>
    public void FireWaveStart(CombatSimulation simulation)
    {
        foreach (var (holder, container) in EnumerateContainers(simulation))
        {
            foreach (var instance in container.All.ToArray())
            {
                if (instance.IsDormant)
                    continue;
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnWaveStart);
            }
        }
    }

    /// <summary>持有者释放主动技后：触发其 onActiveSkillCast。</summary>
    public void FireActiveSkillCast(CombatSimulation simulation, int characterIndex)
    {
        if (!TryGetCharacter(simulation, characterIndex, out var character, out _))
            return;

        var holder = new CombatTargetRef(ECombatSide.Player, characterIndex);
        foreach (var instance in character.Buffs.All.ToArray())
        {
            if (instance.IsDormant)
                continue;
            FireHook(simulation, holder, instance, instance.Def.Hooks.OnActiveSkillCast);
        }
    }

    /// <summary>
    /// 持有者打出的卡牌<b>结算完成后</b>触发其 onCardSettled（逐张，本回合内累计；2026-09-21 新增）。
    /// 与 onSlotCardPlayed 的分工：后者是槽位 buff 的"打牌瞬间（结算前）"，本钩子是角色级"结算后"。
    /// 莱因哈特被动2「本回合打出 2 张以上黄属性卡」据此接线：判定读
    /// <c>ICombatCondContext.CountCardsPlayedThisTurn</c>（该卡此时已登记进本回合出牌表）。
    /// </summary>
    public void FireCardSettled(CombatSimulation simulation, int characterIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!TryGetCharacter(simulation, characterIndex, out var character, out _))
            return;

        var holder = new CombatTargetRef(ECombatSide.Player, characterIndex);
        foreach (var instance in character.Buffs.All.ToArray())
        {
            if (instance.IsDormant)
                continue;

            FireHook(simulation, holder, instance, instance.Def.Hooks.OnCardSettled);
        }
    }

    /// <summary>
    /// 该槽打出卡牌（结算前）：触发槽位 buff 的 onSlotCardPlayed。
    /// 槽位伤害（slot.damage）目标为打出者（走共享血量账本），打出者持有免疫特征 tag 时跳过；
    /// 充能（slot.charge）计数递减，归零触发载荷并重置计数。
    /// </summary>
    public void FireSlotCardPlayed(CombatSimulation simulation, int characterIndex, HandSlot slot)
    {
        if (!TryGetCharacter(simulation, characterIndex, out var character, out _))
            return;

        var source = new CombatTargetRef(ECombatSide.Player, characterIndex);
        foreach (var instance in slot.Buffs.All.ToArray())
        {
            if (instance.IsDormant)
                continue;

            var hooks = instance.Def.Hooks.OnSlotCardPlayed;
            if (hooks.Count == 0)
                continue;

            var tags = instance.Def.EffectiveTags;
            if (tags.Contains(BuiltinBuffTags.SlotDamage))
            {
                if (character.Buffs.HasTag(BuiltinBuffTags.TraitImmuneSlotDamage))
                    continue;

                FireHookList(simulation, source, instance, hooks, [source]);
                continue;
            }

            if (tags.Contains(BuiltinBuffTags.SlotCharge))
            {
                if (!instance.TryConsumeCharge())
                    continue;

                // 充能载荷按效果自带的目标参数解析（如 hookTargets: randomEnemy）。
                FireHook(simulation, source, instance, hooks);
                instance.ResetCharge();
                continue;
            }

            FireHookList(simulation, source, instance, hooks, [source]);
        }
    }

    #endregion

    #region 目标与容器解析

    private BuffContainer? TryResolveContainer(CombatSimulation simulation, CombatTargetRef holder) =>
        holder.Side switch
        {
            ECombatSide.Player when holder.Index >= 0 &&
                holder.Index < simulation.PlayerTeam.Characters.Count =>
                simulation.PlayerTeam.Characters[holder.Index].Buffs,
            ECombatSide.Enemy when holder.Index >= 0 &&
                holder.Index < simulation.EnemyTeam.Enemies.Count =>
                simulation.EnemyTeam.Enemies[holder.Index].Buffs,
            _ => null,
        };

    private static AbilitySystemComponent? GetAsc(CombatSimulation simulation, CombatTargetRef holder)
    {
        if (holder.Side == ECombatSide.Player &&
            holder.Index >= 0 && holder.Index < simulation.PlayerTeam.Characters.Count)
            return simulation.PlayerTeam.Characters[holder.Index].Asc;
        if (holder.Side == ECombatSide.Enemy &&
            holder.Index >= 0 && holder.Index < simulation.EnemyTeam.Enemies.Count)
            return simulation.EnemyTeam.Enemies[holder.Index].Asc;
        return null;
    }

    private IEnumerable<(CombatTargetRef Holder, BuffContainer Container)> EnumerateContainers(
        CombatSimulation simulation)
    {
        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
        {
            var character = simulation.PlayerTeam.Characters[i];
            yield return (new CombatTargetRef(ECombatSide.Player, i), character.Buffs);
            foreach (var slot in character.HandSlots)
                yield return (new CombatTargetRef(ECombatSide.Player, i), slot.Buffs);
        }

        for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
            yield return (new CombatTargetRef(ECombatSide.Enemy, i), simulation.EnemyTeam.Enemies[i].Buffs);
    }

    private static bool TryGetCharacter(
        CombatSimulation simulation,
        int characterIndex,
        out CharacterBattleInstance character,
        out string? error)
    {
        if (characterIndex < 0 || characterIndex >= simulation.PlayerTeam.Characters.Count)
        {
            character = null!;
            error = "角色索引无效。";
            return false;
        }

        character = simulation.PlayerTeam.Characters[characterIndex];
        error = null;
        return true;
    }

    /// <summary>
    /// 钩子效果目标解析（委托 <see cref="CombatTargetSelector"/>）：按单个效果引用的合并参数
    /// （效果参数 + 实例挂载参数）解析 <c>hookTargets</c> / <c>targetFilter</c>，缺省为来源自身。
    /// </summary>
    private static IReadOnlyList<CombatTargetRef> ResolveHookTargets(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object>? mergedParams) =>
        CombatTargetSelector.Resolve(simulation, source, mergedParams);

    #endregion

    #region 钩子执行

    private void FireHook(
        CombatSimulation simulation,
        CombatTargetRef holder,
        BuffInstance instance,
        IReadOnlyList<EffectRefDto> hooks,
        bool gateTurnInterval = false)
    {
        if (hooks.Count == 0)
            return;

        foreach (var effectRef in hooks)
        {
            var merged = MergeEffectParams(effectRef, instance.Params);
            if (gateTurnInterval && !PassesTurnInterval(simulation, merged))
                continue;

            // oncePerTurn：同一 buff 实例的同一效果每回合只触发一次（"每回合仅 1 次"类被动）。
            if (IsOncePerTurn(merged.Params) && !instance.TryMarkHookFiredThisTurn(effectRef.EffectId))
                continue;

            _executor.ExecuteEffectRef(merged, simulation, holder, ResolveHookTargets(simulation, holder, merged.Params));
        }
    }

    /// <summary>钩子参数 <c>oncePerTurn: true</c> 判定（记账键用 effectId，同 buff 的不同效果互不影响）。</summary>
    private static bool IsOncePerTurn(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue("oncePerTurn", out var value) || value is null)
            return false;

        return value is bool flag
            ? flag
            : string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private void FireHookList(
        CombatSimulation simulation,
        CombatTargetRef holder,
        BuffInstance instance,
        IReadOnlyList<EffectRefDto> hooks,
        IReadOnlyList<CombatTargetRef> targets,
        bool gateTurnInterval = false)
    {
        foreach (var effectRef in hooks)
        {
            var merged = MergeEffectParams(effectRef, instance.Params);
            if (gateTurnInterval && !PassesTurnInterval(simulation, merged))
                continue;

            _executor.ExecuteEffectRef(merged, simulation, holder, targets);
        }
    }

    /// <summary>波内每 N 回合触发：效果参数 <c>turnInterval</c>（N ≥ 1）；未配置则每回合触发。</summary>
    private static bool PassesTurnInterval(
        CombatSimulation simulation,
        EffectRefDto effectRef)
    {
        if (effectRef.Params is null ||
            !effectRef.Params.TryGetValue("turnInterval", out var value) ||
            value is null)
            return true;

        return int.TryParse(value.ToString(), out var interval) &&
            interval > 0 &&
            simulation.TurnsIntoWave > 0 &&
            simulation.TurnsIntoWave % interval == 0;
    }

    /// <summary>挂载参数覆盖效果引用参数（载荷可配）。</summary>
    private static EffectRefDto MergeEffectParams(EffectRefDto effectRef, IReadOnlyDictionary<string, object>? overrides)
    {
        if (overrides is null || overrides.Count == 0 || effectRef.Params is null || effectRef.Params.Count == 0)
        {
            return overrides is null || overrides.Count == 0
                ? effectRef
                : new EffectRefDto
                {
                    EffectId = effectRef.EffectId,
                    Params = new Dictionary<string, object>(overrides, StringComparer.Ordinal),
                };
        }

        var merged = new Dictionary<string, object>(effectRef.Params, StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
            merged[key] = value;
        return new EffectRefDto { EffectId = effectRef.EffectId, Params = merged };
    }

    /// <summary>投放参数里的控制键（目标解析/分档）不进入实例参数。</summary>
    private static IReadOnlyDictionary<string, object>? FilterInstanceParams(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return null;

        var filtered = parameters
            .Where(pair => pair.Key is not ("hookTargets" or "targetFilter" or "turnInterval" or "oncePerTurn"))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return filtered.Count > 0 ? filtered : null;
    }

    #endregion
}
