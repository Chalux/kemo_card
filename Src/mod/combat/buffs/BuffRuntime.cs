using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Buffs;

public sealed record BuffApplyResult(bool Success, BuffInstance? Instance = null, string? Error = null);

/// <summary>
/// Buff 运行时编排：投放（叠层/互斥/范围展开）、钩子节点分发、时长 tick、驱散。
/// 挂点三类：角色容器、敌人容器、手牌槽位容器；修正走 ASC 聚合器句柄，休眠=撤销句柄。
/// 钩子效果的目标由效果参数决定：<c>hookTargets</c>（self/team/allies/randomEnemy/allEnemies）与
/// <c>targetFilter</c>（<c>self</c> / <c>excludeSelf</c> + <c>condition</c>，条件主体 = 候选）。
/// </summary>
public sealed class BuffRuntime
{
    private readonly GameDefinitionRegistry _registry;
    private readonly CombatEffectExecutor _executor;
    private bool _refreshingEnemyPresence;

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

        using var scope = simulation.EffectBudget.TryEnter();
        if (scope is null)
            return new BuffApplyResult(false, Error: "Synchronous effect budget exceeded.");

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

    private BuffApplyResult ApplyToHolder(
        CombatSimulation simulation,
        CombatTargetRef holder,
        BuffDto def,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var container = TryResolveContainer(simulation, holder);
        if (container is null)
            return new BuffApplyResult(false, Error: "目标没有 buff 容器（队伍账本不是合法挂点）。");

        if (def.EffectiveTags.Contains(CombatConstants.VirusTag) && container.HasTag(BuiltinBuffTags.TraitImmuneVirus))
            return new BuffApplyResult(true);

        var instance = StackOrAdd(simulation, container, holder, def, parameters);
        RefreshEnemyPresenceConditions(simulation);
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
        var holderRef = new BuffHolderRef(holder);

        // 先发布完整替换结果，再分发移除钩子；重入投放以最后一次提交为准。
        var conflicts = container.All.Where(instance => !instance.IsDomainOwned && (
            (!string.IsNullOrWhiteSpace(def.ExclusiveGroup) && instance.Def.ExclusiveGroup == def.ExclusiveGroup) ||
            (def.StackRule == EBuffStackRule.Replace && instance.Def.Id == def.Id))).ToArray();
        var removedInstances = new List<BuffInstance>(conflicts.Length);
        foreach (var conflict in conflicts)
        {
            if (!container.Remove(conflict))
                continue;
            removedInstances.Add(conflict);
            simulation.Presentation.Emit(new BuffRemovedEvent(holderRef, conflict.Def.Id));
        }

        var existing = container.All.FirstOrDefault(instance => !instance.IsDomainOwned && instance.Def.Id == def.Id);
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
                        simulation.Presentation.Emit(new BuffStacksChangedEvent(holderRef, def.Id, existing.Stacks));
                        FireHook(simulation, holder, existing, def.Hooks.OnStackChanged);
                    }
                    return existing;
                case EBuffStackRule.Refresh:
                    existing.RefreshDuration();
                    return existing;
                case EBuffStackRule.Independent:
                    if (container.All.Count(item => !item.IsDomainOwned && item.Def.Id == def.Id) >= Math.Max(1, def.MaxStacks))
                        return existing;
                    break;
            }
        }

        var instance = container.Add(def, FilterInstanceParams(parameters), BuildConditionContext(simulation, holder));
        simulation.Presentation.Emit(new BuffAppliedEvent(holderRef, def.Id, instance.Stacks));
        foreach (var removed in removedInstances)
            FireHook(simulation, holder, removed, removed.Def.Hooks.OnRemove);
        if (container.All.Contains(instance))
            FireHook(simulation, holder, instance, def.Hooks.OnApply);
        return instance;
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
        var removedCount = 0;
        foreach (var instance in toRemove)
        {
            if (!container.Remove(instance))
                continue;
            removedCount++;
            simulation.Presentation.Emit(new BuffRemovedEvent(new BuffHolderRef(holder), instance.Def.Id));
            FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
        }

        RefreshEnemyPresenceConditions(simulation);
        return removedCount;
    }

    /// <summary>开始时同时快照角色 Buff / GE；移除钩子新建的效果不进入本次驱散。</summary>
    public int DispelDebuffs(CombatSimulation simulation, CombatTargetRef holder)
    {
        var container = TryResolveContainer(simulation, holder);
        var asc = GetAsc(simulation, holder);
        if (container is null || asc is null)
            return 0;
        var buffs = container.All.Where(instance => !instance.IsDomainOwned &&
            !instance.Def.EffectiveTags.Contains(BuiltinBuffTags.Undispellable) &&
            instance.Def.EffectiveTags.Any(BuiltinBuffTags.IsDebuffTag)).ToArray();
        var effects = asc.ActiveEffects.Where(effect =>
            !effect.Def.GrantedTags.Contains(BuiltinBuffTags.Undispellable) &&
            effect.Def.GrantedTags.Any(BuiltinBuffTags.IsDebuffTag)).Select(effect => effect.Handle).ToArray();
        var removed = 0;
        foreach (var instance in buffs)
        {
            if (!container.Remove(instance))
                continue;
            removed++;
            simulation.Presentation.Emit(new BuffRemovedEvent(new BuffHolderRef(holder), instance.Def.Id));
            FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
        }
        foreach (var handle in effects)
            if (asc.RemoveActiveEffect(handle)) removed++;
        RefreshEnemyPresenceConditions(simulation);
        return removed;
    }

    private static bool MatchesDispel(BuffInstance instance, string? buffId, IReadOnlyList<string>? withTags)
    {
        if (instance.IsDomainOwned || instance.Def.EffectiveTags.Contains(BuiltinBuffTags.Undispellable))
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

    /// <summary>换条为状态重置而非驱散：包括不可驱散状态，不触发会重建状态的移除钩子。</summary>
    internal void ResetEnemyLifeBar(CombatSimulation simulation, int enemyIndex)
    {
        var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
        var holder = new BuffHolderRef(new(ECombatSide.Enemy, enemyIndex));
        foreach (var instance in enemy.Buffs.All.ToArray())
            if (!instance.Def.PersistAcrossLifeBars && enemy.Buffs.Remove(instance))
                simulation.Presentation.Emit(new BuffRemovedEvent(holder, instance.Def.Id));
        foreach (var effect in enemy.Asc.ActiveEffects.ToArray())
            if (!effect.Def.PersistAcrossLifeBars)
                enemy.Asc.RemoveActiveEffect(effect.Handle, invokeRemoveHooks: false);
        enemy.Asc.SetBaseValue(AttributeIds.Shield, 0f);
        RefreshEnemyPresenceConditions(simulation);
    }

    /// <summary>每个入场敌人只作为本次钩子的默认目标，不重放阶层开始钩子。</summary>
    internal void FireEnemyEntered(CombatSimulation simulation, int enemyIndex)
    {
        if (enemyIndex < 0 || enemyIndex >= simulation.EnemyTeam.Enemies.Count || !simulation.EnemyTeam.Enemies[enemyIndex].IsAlive)
            return;
        var target = new CombatTargetRef(ECombatSide.Enemy, enemyIndex);
        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
        {
            var character = simulation.PlayerTeam.Characters[i];
            foreach (var instance in character.Buffs.All.ToArray())
                if (!instance.IsDormant && character.Buffs.All.Contains(instance))
                    FireHook(simulation, new(ECombatSide.Player, i), instance, instance.Def.Hooks.OnEnemyEntered, defaultTargets: [target]);
        }
    }

    /// <summary>领域持有独立 Buff 实例，不与普通同名投放互相覆盖。</summary>
    internal BuffInstance? AddDomainBuff(CombatSimulation simulation, CombatTargetRef holder, BuffRefDto reference)
    {
        using var scope = simulation.EffectBudget.TryEnter();
        if (scope is null)
            return null;
        var container = TryResolveContainer(simulation, holder);
        if (container is null || !_registry.Store.TryGetBuff(reference.BuffId, out var def))
            return null;
        var instance = container.Add(def, reference.Params, BuildConditionContext(simulation, holder));
        instance.IsDomainOwned = true;
        simulation.Presentation.Emit(new BuffAppliedEvent(new BuffHolderRef(holder), def.Id, instance.Stacks));
        FireHook(simulation, holder, instance, def.Hooks.OnApply);
        return instance;
    }

    internal void RemoveDomainBuff(CombatSimulation simulation, CombatTargetRef holder, BuffContainer container, BuffInstance instance)
    {
        if (!container.Remove(instance))
            return;
        simulation.Presentation.Emit(new BuffRemovedEvent(new BuffHolderRef(holder), instance.Def.Id));
        FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
    }

    /// <summary>敌方标签/存活状态改变后立即同步存在条件，不提前重估出牌或回合条件。</summary>
    internal void RefreshEnemyPresenceConditions(CombatSimulation simulation)
    {
        if (_refreshingEnemyPresence)
            return;
        _refreshingEnemyPresence = true;
        try
        {
            foreach (var (holder, container, _) in SnapshotContainers(simulation))
                container.EvaluateDormancy(BuildConditionContext(simulation, holder), BuiltinCombatConditions.EnemyHasBuffTag);
        }
        finally { _refreshingEnemyPresence = false; }
        if (simulation.Phase == KemoCard.Mod.Combat.StateMachine.ECombatPhase.Player)
            simulation.EnemyIntents.RefreshAll();
    }

    /// <summary>回合开始：重估休眠 + 触发 onTurnStart（支持 per-effect <c>turnInterval</c> 按波内回合计数分档触发）。</summary>
    public void FireTurnStart(CombatSimulation simulation, bool expireBoundary = true)
    {
        if (expireBoundary)
            ExpireTurnStartBuffs(simulation);
        foreach (var (holder, container, instances) in SnapshotContainers(simulation))
        {
            container.EvaluateDormancy(BuildConditionContext(simulation, holder));
            // 钩子可能对自己容器挂/删 buff，必须快照枚举（活列表枚举中修改会抛异常）。
            foreach (var instance in instances)
            {
                if (instance.IsDormant || !container.All.Contains(instance))
                    continue;
                instance.ResetTurnFlags();
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnTurnStart, gateTurnInterval: true);
            }
        }
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
                if (instance.IsDormant || !character.Buffs.All.Contains(instance))
                    continue;
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnOrbTriggered);
            }
        }
    }

    public void FireOrbAutoTriggered(CombatSimulation simulation) =>
        FireForAllPlayerCharacters(simulation, instance => instance.Def.Hooks.OnOrbAutoTriggered);

    internal void FireShieldDepleted(CombatSimulation simulation, int index)
    {
        var character = simulation.PlayerTeam.Characters[index];
        foreach (var instance in character.Buffs.All.ToArray())
            if (!instance.IsDormant && character.Buffs.All.Contains(instance))
                FireHook(simulation, new(ECombatSide.Player, index), instance, instance.Def.Hooks.OnShieldDepleted);
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
                if (instance.IsDormant || !character.Buffs.All.Contains(instance))
                    continue;
                FireHook(simulation, holder, instance, selectHooks(instance));
            }
        }
    }

    /// <summary>回合结束：先触发 onTurnEnd，再递减时长，到期者触发 onRemove 并移除。</summary>
    public void FireTurnEnd(CombatSimulation simulation)
    {
        foreach (var (holder, container, instances) in SnapshotContainers(simulation))
        {
            var holderRef = new BuffHolderRef(holder);
            // 以触发前的快照为本回合基准：钩子期间新增的 buff 本回合不 tick（刚挂上不应立刻扣时长）。

            foreach (var instance in instances)
            {
                if (instance.IsDormant || !container.All.Contains(instance))
                    continue;
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnTurnEnd);
            }

            foreach (var instance in instances)
            {
                if (!container.All.Contains(instance))
                    continue;
                // 独立计时的层会随时间逐层脱落（2026-09-24）：层数变了必须重算属性修正，
                // 否则聚合器里还留着旧层数的幅度（例：两层 +6 掉成一层后仍按 +12 结算）。
                var stacksBefore = instance.Stacks;
                instance.TickTurnEnd();
                if (instance.Stacks != stacksBefore)
                {
                    instance.RegisterModifiers(GetAsc(simulation, holder));
                    if (!instance.IsExpired)
                        simulation.Presentation.Emit(new BuffStacksChangedEvent(holderRef, instance.Def.Id, instance.Stacks));
                }
            }

            var expired = instances.Where(instance => instance.IsExpired).ToList();
            foreach (var instance in expired)
            {
                // 钩子期间可能已被驱散（不在容器里了）：只对仍持有的实例补发 onRemove。
                if (!container.All.Contains(instance))
                    continue;

                if (!container.Remove(instance))
                    continue;
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
                simulation.Presentation.Emit(new BuffRemovedEvent(holderRef, instance.Def.Id));
            }
        }
        RefreshEnemyPresenceConditions(simulation);
    }

    /// <summary>波次（阶层）开始：触发 onWaveStart。第一波在 RunBattleStart 由状态机补发。</summary>
    public void FireWaveStart(CombatSimulation simulation)
    {
        ExpireDuration(simulation, EBuffDurationType.Wave);
        RefreshEnemyPresenceConditions(simulation);
        foreach (var (holder, container, instances) in SnapshotContainers(simulation))
        {
            foreach (var instance in instances)
            {
                if (instance.IsDormant || !container.All.Contains(instance))
                    continue;
                // 阶层记账在触发前清空：本阶层的"仅 1 次"从这一刻重新计数。
                instance.ResetWaveFlags();
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnWaveStart);
            }
        }
    }

    private (CombatTargetRef Holder, BuffContainer Container, BuffInstance[] Instances)[]
        SnapshotContainers(CombatSimulation simulation) => EnumerateContainers(simulation)
            .Select(pair => (pair.Holder, pair.Container, pair.Container.All.ToArray())).ToArray();

    /// <summary>持有者释放主动技后：触发其 onActiveSkillCast。</summary>
    public void FireActiveSkillCast(CombatSimulation simulation, int characterIndex)
    {
        if (!TryGetCharacter(simulation, characterIndex, out var character, out _))
            return;

        var holder = new CombatTargetRef(ECombatSide.Player, characterIndex);
        foreach (var instance in character.Buffs.All.ToArray())
        {
            if (instance.IsDormant || !character.Buffs.All.Contains(instance))
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
            if (instance.IsDormant || !character.Buffs.All.Contains(instance))
                continue;

            FireHook(simulation, holder, instance, instance.Def.Hooks.OnCardSettled);
        }
    }

    /// <summary>
    /// 持有者本批次被命中 <paramref name="hits"/> 次后逐次触发其 onDamaged
    /// （2026-09-25 新增；批次与记账见 <see cref="CombatSimulation.FlushOnDamagedHits"/>）。
    /// 每次触发独立走 <c>oncePerTurn</c> 门闩——"每次受击回复"应保持默认（不设门闩）。
    /// </summary>
    public void FireOnDamagedHits(CombatSimulation simulation, int characterIndex, int hits, CombatTargetRef? attacker = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (hits <= 0 || !TryGetCharacter(simulation, characterIndex, out var character, out _))
            return;

        var holder = new CombatTargetRef(ECombatSide.Player, characterIndex);
        foreach (var instance in character.Buffs.All.ToArray())
        {
            if (instance.IsDormant || !character.Buffs.All.Contains(instance) || instance.Def.Hooks.OnDamaged.Count == 0)
                continue;

            // 逐次触发：`oncePerTurn` 之类的门闩由 FireHook 按实例自行处理。
            for (var hit = 0; hit < hits && character.Buffs.All.Contains(instance); hit++)
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnDamaged, attacker: attacker);
        }
    }

    /// <summary>致命伤害可由钩子授予免疫抵消；次数门闩按战斗实例保存。</summary>
    internal void FireBeforeFatalDamage(CombatSimulation simulation, CombatTargetRef holder)
    {
        var container = TryResolveContainer(simulation, holder);
        if (container is null)
            return;
        foreach (var instance in container.All.ToArray())
        {
            if (!instance.IsDormant && container.All.Contains(instance))
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnBeforeFatalDamage);
        }
    }

    internal void ExpireTurnStartBuffs(CombatSimulation simulation) =>
        ExpireDuration(simulation, EBuffDurationType.UntilNextTurnStart);

    private void ExpireDuration(CombatSimulation simulation, EBuffDurationType durationType)
    {
        foreach (var (holder, container, instances) in SnapshotContainers(simulation))
        {
            foreach (var instance in instances)
            {
                if (instance.Def.DurationType != durationType || !container.Remove(instance))
                    continue;
                simulation.Presentation.Emit(new BuffRemovedEvent(new BuffHolderRef(holder), instance.Def.Id));
                FireHook(simulation, holder, instance, instance.Def.Hooks.OnRemove);
            }
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

    /// <summary>
    /// 持有者的条件上下文（2026-09-26）：身份类条件的主体 = 持有者；回合/出牌统计走仿真。
    /// </summary>
    private static CombatCondContext BuildConditionContext(CombatSimulation simulation, CombatTargetRef holder)
    {
        var (elementFlags, raceFlags) = CombatIdentity.Resolve(simulation, holder);
        return new CombatCondContext(simulation, holder, elementFlags, raceFlags);
    }

    /// <summary>枚举角色和敌人的 buff 容器。</summary>
    private IEnumerable<(CombatTargetRef Holder, BuffContainer Container)> EnumerateContainers(
        CombatSimulation simulation)
    {
        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
        {
            var character = simulation.PlayerTeam.Characters[i];
            yield return (new CombatTargetRef(ECombatSide.Player, i), character.Buffs);
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
        bool gateTurnInterval = false,
        CombatTargetRef? attacker = null,
        IReadOnlyList<CombatTargetRef>? defaultTargets = null)
    {
        if (hooks.Count == 0)
            return;

        foreach (var effectRef in hooks)
        {
            var merged = MergeEffectParams(effectRef, instance.Params);
            if (gateTurnInterval && !PassesTurnInterval(simulation, merged))
                continue;

            var oncePerTurn = IsOncePerTurn(merged.Params);
            var oncePerWave = IsOncePerWave(merged.Params);
            var oncePerCombat = IsTrueFlag(merged.Params, "oncePerCombat");
            if (!instance.CanFireHook(effectRef.EffectId, oncePerTurn, oncePerWave, oncePerCombat))
                continue;
            var targets = merged.Params?.GetValueOrDefault("hookTargets")?.ToString() == "attacker"
                ? attacker is { Side: ECombatSide.Enemy } target && target.Index >= 0 &&
                    target.Index < simulation.EnemyTeam.Enemies.Count && simulation.EnemyTeam.Enemies[target.Index].IsAlive
                    ? new[] { target } : []
                : defaultTargets is not null && (merged.Params is null || !BuffActionParams.HasTargetSelector(merged.Params))
                    ? defaultTargets : ResolveHookTargets(simulation, holder, merged.Params);
            if (oncePerTurn || oncePerWave || oncePerCombat)
            {
                _executor.ExecuteEffectRefWhen(merged, simulation, holder, targets,
                    () => instance.TryMarkHookFired(effectRef.EffectId, oncePerTurn, oncePerWave, oncePerCombat));
            }
            else
                _executor.ExecuteEffectRef(merged, simulation, holder, targets);
        }
    }

    /// <summary>钩子参数 <c>oncePerTurn: true</c> 判定（记账键用 effectId，同 buff 的不同效果互不影响）。</summary>
    private static bool IsOncePerTurn(IReadOnlyDictionary<string, object>? parameters) =>
        IsTrueFlag(parameters, "oncePerTurn");

    /// <summary>钩子参数 <c>oncePerWave: true</c> 判定（记账周期 = 整个阶层）。</summary>
    private static bool IsOncePerWave(IReadOnlyDictionary<string, object>? parameters) =>
        IsTrueFlag(parameters, "oncePerWave");

    private static bool IsTrueFlag(IReadOnlyDictionary<string, object>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value) || value is null)
            return false;

        return value is bool flag
            ? flag
            : string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);
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
            .Where(pair => pair.Key is not ("hookTargets" or "targetFilter" or "turnInterval" or "oncePerTurn" or "oncePerWave" or "oncePerCombat"))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return filtered.Count > 0 ? filtered : null;
    }

    #endregion
}