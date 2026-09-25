using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// 单个持有者（角色 / 敌人 / 手牌槽位）的 buff 容器。
/// 纯状态管理：应用、叠层、休眠评估、到期递减；钩子触发与投放范围由 <see cref="BuffRuntime"/> 编排。
/// 槽位容器没有 ASC 与持有者属性，condition / modifiers 均不适用（只承载钩子与 tag）。
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
    public BuffInstance Add(BuffDto def, IReadOnlyDictionary<string, object>? parameters)
    {
        var instance = new BuffInstance(def, parameters, _magnitudeResolver);
        EvaluateDormancy(instance);
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
    public void EvaluateDormancy()
    {
        foreach (var instance in _instances)
            EvaluateDormancy(instance);
    }

    private void EvaluateDormancy(BuffInstance instance)
    {
        var shouldDormant = instance.Def.Condition is not null && !MatchesCondition(instance.Def.Condition);
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

    private bool MatchesCondition(BuffConditionDto condition)
    {
        var element = _elementProvider?.Invoke() ?? EElement.None;
        var race = _raceProvider?.Invoke() ?? ERace.None;

        // 只把**已配置**的维度纳入判定：matchAll 是"跨配置维度取且"，未配置的维度不参与
        // （旧实现用固定两个布尔，只配一项时 matchAll 恒不满足）。
        var dimensions = new List<bool>(3);
        if (condition.ElementAny is { Count: > 0 })
        {
            dimensions.Add(condition.ElementAny.Any(flag => flag != EElement.None && (element & flag) != 0));
        }

        if (condition.RaceAny is { Count: > 0 })
        {
            dimensions.Add(condition.RaceAny.Any(flag => flag != ERace.None && (race & flag) != 0));
        }

        // raceAll（2026-09-25）：列表内取"且"——描述显式写「人类且学术」这类"同时具备多种族"时才用它。
        if (condition.RaceAll is { Count: > 0 })
        {
            dimensions.Add(condition.RaceAll.All(flag => flag != ERace.None && (race & flag) != 0));
        }

        // 持有者维度：默认"或"（任一维度命中即满足），matchAll: true 时取"且"。
        var holderMatched = dimensions.Count > 0 &&
            (condition.MatchAll ? dimensions.All(matched => matched) : dimensions.Any(matched => matched));

        // 队伍人数门闩（2026-09-21）：与持有者维度取"且"；只配人数时完全由人数决定。
        if (condition.PartyMinCount > 0)
        {
            var partyMatched = CountPartyMatches(condition) >= condition.PartyMinCount;
            return partyMatched && (dimensions.Count == 0 || holderMatched);
        }

        return holderMatched;
    }

    private int CountPartyMatches(BuffConditionDto condition)
    {
        if (_partyCountQuery is null)
            return 0;

        var elementFlags = 0;
        foreach (var flag in condition.PartyElementAny ?? [])
            elementFlags |= (int)flag;

        var raceFlags = 0;
        foreach (var flag in condition.PartyRaceAny ?? [])
            raceFlags |= (int)flag;

        // 描述约定（2026-09-25）：`·` = 或——队伍人数筛选与持有者维度共用同一个 matchAll 开关。
        return _partyCountQuery(elementFlags, raceFlags, condition.MatchAll);
    }
}