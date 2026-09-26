using KemoCard.Frame.Condition;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Condition;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// 单个持有者（角色 / 敌人 / 手牌槽位）的 buff 容器。
/// 纯状态管理：应用、叠层、休眠评估、到期递减；钩子触发与投放范围由 <see cref="BuffRuntime"/> 编排。
/// 槽位容器没有 ASC 与持有者属性，conditions / modifiers 均不适用（只承载钩子与 tag）。
/// </summary>
public sealed class BuffContainer
{
    private readonly List<BuffInstance> _instances = [];
    private readonly AbilitySystemComponent? _asc;
    private readonly Func<EElement>? _elementProvider;
    private readonly Func<ERace>? _raceProvider;
    private readonly Func<MagnitudeDefDto, float>? _magnitudeResolver;
    private readonly Func<int, int, bool, int>? _partyCountQuery;

    public BuffContainer(
        AbilitySystemComponent? asc = null,
        Func<EElement>? elementProvider = null,
        Func<ERace>? raceProvider = null,
        Func<MagnitudeDefDto, float>? magnitudeResolver = null,
        Func<int, int, bool, int>? partyCountQuery = null)
    {
        _asc = asc;
        _elementProvider = elementProvider;
        _raceProvider = raceProvider;
        _magnitudeResolver = magnitudeResolver;
        _partyCountQuery = partyCountQuery;
    }

    public IReadOnlyList<BuffInstance> All => _instances;

    /// <summary>
    /// UI 展示集：非休眠且未标记 hidden 的实例（休眠 buff 不显示图标）。
    /// 预留给后置的正式战斗界面（2026-09-19 规格后置项 §7）；当前消费方只有 RunDebugDlg 的检查输出。
    /// </summary>
    public IReadOnlyList<BuffInstance> Visible => _instances.Where(instance => !instance.IsDormant && !instance.Def.Hidden).ToArray();

    public BuffInstance? Find(string buffId) =>
        _instances.FirstOrDefault(instance => string.Equals(instance.Def.Id, buffId, StringComparison.Ordinal));

    public bool HasTag(string tag) => _instances.Any(instance =>
        !instance.IsDormant && instance.Def.EffectiveTags.Contains(tag, StringComparer.Ordinal));

    /// <summary>按机制 tag 找第一个非休眠实例（如手牌槽充能 <c>slot.charge</c>）；找不到返回 null。</summary>
    public BuffInstance? FindByTag(string tag) => _instances.FirstOrDefault(instance =>
        !instance.IsDormant && instance.Def.EffectiveTags.Contains(tag, StringComparer.Ordinal));

    /// <summary>新建实例并入容器（不做叠层/互斥判定——那属于 <see cref="BuffRuntime"/> 的编排职责）。</summary>
    /// <param name="context">条件上下文（2026-09-26）：身份类条件的主体 = 持有者；未传时用持有者自身
    /// 的属性/种族/队伍查询构造（回合与出牌统计为 0/-1）。</param>
    public BuffInstance Add(
        BuffDto def,
        IReadOnlyDictionary<string, object>? parameters,
        ICombatCondContext? context = null)
    {
        var instance = new BuffInstance(def, parameters, _magnitudeResolver);
        EvaluateDormancy(instance, context);
        _instances.Add(instance);
        instance.RegisterModifiers(_asc);
        return instance;
    }

    public bool Remove(BuffInstance instance)
    {
        if (!_instances.Remove(instance))
            return false;

        instance.UnregisterModifiers(_asc);
        // RemoveModifiersForHandle 只刷新修饰符清单不重算当前值，与 ASC 移除路径对齐需要显式重算。
        _asc?.Aggregator.RecalculateAll();
        return true;
    }

    /// <summary>互斥组：移除同组已有实例，返回被移除的实例（供运行时补发 onRemove 钩子）。</summary>
    public List<BuffInstance> RemoveExclusiveGroup(string exclusiveGroup)
    {
        var removed = _instances
            .Where(instance => string.Equals(instance.Def.ExclusiveGroup, exclusiveGroup, StringComparison.Ordinal))
            .ToList();
        foreach (var instance in removed)
            Remove(instance);
        return removed;
    }

    /// <summary>
    /// 重新评估全部实例的休眠状态并同步修正注册。
    /// 每回合开始时调用一次；持有者属性/种族战斗中变化后下一回合自动生效。
    /// </summary>
    public void EvaluateDormancy(ICombatCondContext? context = null)
    {
        foreach (var instance in _instances)
            EvaluateDormancy(instance, context);
    }

    private void EvaluateDormancy(BuffInstance instance, ICombatCondContext? context)
    {
        var shouldDormant = instance.Def.Conditions.Count > 0 && !ConditionsPass(instance.Def, context);
        if (shouldDormant == instance.IsDormant)
            return;

        instance.SetDormant(shouldDormant);
        if (shouldDormant)
        {
            instance.UnregisterModifiers(_asc);
            _asc?.Aggregator.RecalculateAll();
        }
        else
        {
            instance.RegisterModifiers(_asc);
            _asc?.Aggregator.RecalculateAll();
        }
    }

    /// <summary>
    /// 持有者条件（2026-09-26 统一走战斗条件域）：主体 = 持有者，身份/人数维度与其它条件共用
    /// <c>IdentityMatch</c> 等条件类型。角色 / 敌人容器用调用方传入的持有者上下文；
    /// 槽位容器没有 ASC 与持有者身份，一律用容器自身 provider（槽位为 null → 身份条件不满足而休眠），
    /// 避免槽位 buff 意外按"所属角色的身份"求值（与类注释口径一致）。
    /// </summary>
    private bool ConditionsPass(BuffDto def, ICombatCondContext? context)
    {
        var effective = _asc is null
            ? new HolderCondContext(_elementProvider, _raceProvider, _partyCountQuery)
            : context ?? new HolderCondContext(_elementProvider, _raceProvider, _partyCountQuery);
        return CombatConditionEvaluator.Pass(def.Conditions, effective, $"buff:{def.Id}:conditions");
    }

    /// <summary>持有者上下文：只提供身份主体与队伍人数查询，其余查询（回合 / 出牌 / 连携）为 0/-1。</summary>
    private sealed class HolderCondContext : ICombatCondContext
    {
        private readonly Func<EElement>? _elementProvider;
        private readonly Func<ERace>? _raceProvider;
        private readonly Func<int, int, bool, int>? _partyCountQuery;

        public HolderCondContext(
            Func<EElement>? elementProvider,
            Func<ERace>? raceProvider,
            Func<int, int, bool, int>? partyCountQuery)
        {
            _elementProvider = elementProvider;
            _raceProvider = raceProvider;
            _partyCountQuery = partyCountQuery;
        }

        public int TurnNumber => 0;

        public int TurnsIntoWave => 0;

        public int SourceCharacterIndex => -1;

        public int SubjectElementFlags => (int)(_elementProvider?.Invoke() ?? EElement.None);

        public int SubjectRaceFlags => (int)(_raceProvider?.Invoke() ?? ERace.None);

        public int CountCardsPlayedThisTurn(int characterIndex, int elementFlags) => 0;

        public int CountChainParticipants(int elementFlags) => 0;

        public int CountPartyIdentityMatches(int elementFlags, int raceFlags, bool matchAll) =>
            _partyCountQuery?.Invoke(elementFlags, raceFlags, matchAll) ?? 0;
    }
}