using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class EnemyUnit
{
    public string RuntimeId { get; private set; }
    internal void SetSummonRuntimeId(string runtimeId) => RuntimeId = runtimeId;
    public string DefinitionId { get; }
    public AbilitySystemComponent Asc { get; }

    /// <summary>挂在该敌人身上的 buff 容器（减益等）。</summary>
    public BuffContainer Buffs { get; }

    public int CurrentHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.Health));
    public int MaxHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.MaxHealth));
    public string? IntentSkillId { get; set; }
    internal string? PlannedSkillId { get; set; }
    public EnemyIntentChangedEvent? Intent { get; internal set; }

    public IReadOnlyList<EnemyLifeBarDto> LifeBars { get; private set; } = [];
    public int LifeBarIndex { get; private set; }
    public int LifeBarCount => Math.Max(1, LifeBars.Count);
    public bool HasNextLifeBar => LifeBarIndex + 1 < LifeBars.Count;
    public bool IsAwaitingLifeBar { get; internal set; }
    public int ActionSequenceIndex { get; private set; }
    private double _hpScale = 1.0;
    private EnemyActionDto? _priorityAction;
    private readonly Dictionary<string, int> _lifeBarSummons = new(StringComparer.Ordinal);
    internal int LifeBarEnteredTurn { get; set; } = 1;
    internal int FirstEnemyPhase { get; set; }
    internal int SummonsThisLifeBar(string enemyId) => _lifeBarSummons.GetValueOrDefault(enemyId);
    internal void RecordSummon(string enemyId) => _lifeBarSummons[enemyId] = SummonsThisLifeBar(enemyId) + 1;
    public EnemyActionDto? NextAction => _priorityAction ?? (LifeBars.Count > LifeBarIndex &&
        LifeBars[LifeBarIndex].ActionSequence.Count > 0
        ? LifeBars[LifeBarIndex].ActionSequence[ActionSequenceIndex] : null);

    /// <summary>敌人属性（内容侧 <c>EnemyDto.element</c>）；供 buff 持有者条件求值。</summary>
    public EElement Element { get; }

    /// <summary>敌人种族（内容侧 <c>EnemyDto.race</c>，2026-09-23 新增）；供 buff 持有者条件求值。</summary>
    public ERace Race { get; }

    /// <summary>
    /// 行动计数（2026-09-23 新增）：每个敌方阶段，计数 &gt; 1 时该敌人<b>只递减计数、不行动</b>；
    /// 计数 ≤ 1 时正常行动。因此把它设为 2 = 把它的行动<b>推迟到下个回合</b>
    /// （冯·诺依曼被动6的控制手段）。默认 1 = 每回合行动。
    /// </summary>
    public int ActionCount
    {
        get => _actionCount;
        set => _actionCount = Math.Max(1, value);
    }

    private int _actionCount = 1;

    /// <summary>未取整血量，供结算保留 GAS 公式算出的小数（分槽/减伤可能产出小数伤害）。</summary>
    internal float CurrentHpExact => Asc.GetCurrentValue(AttributeIds.Health);

    /// <summary>
    /// 存活判定必须用未取整血量：<see cref="CurrentHp"/> 走 <c>MathF.Round</c>（银行家舍入），
    /// 剩 0.5 血时会被舍入为 0，敌人会被提前判定为已阵亡。
    /// </summary>
    public bool IsAlive => CurrentHpExact > 0f || IsAwaitingLifeBar;

    public EnemyUnit(string runtimeId, string definitionId, int maxHp)
        : this(
            runtimeId,
            definitionId,
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = maxHp,
            })
    {
    }

    public EnemyUnit(string runtimeId, string definitionId, IReadOnlyDictionary<string, float> baseAttributes)
        : this(runtimeId, definitionId, baseAttributes, EElement.None, ERace.None)
    {
    }

    public EnemyUnit(
        string runtimeId,
        string definitionId,
        IReadOnlyDictionary<string, float> baseAttributes,
        EElement element,
        ERace race)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(baseAttributes);
        if (!baseAttributes.TryGetValue(AttributeIds.MaxHealth, out var maxHealth) || maxHealth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(baseAttributes), "MaxHealth must be greater than 0.");
        RuntimeId = runtimeId;
        DefinitionId = definitionId;
        Element = element;
        Race = race;
        Asc = new CombatAscFactory().BuildAsc(
            new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
            new Dictionary<string, float>(baseAttributes, StringComparer.Ordinal));
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, maxHealth);
        // 属性/种族 provider 是 buff 持有者条件（elementAny / raceAny）的求值来源：
        // 没有它，挂在敌人身上的条件 buff 恒被判为"不满足"而休眠。
        Buffs = new BuffContainer(Asc, () => Element, () => Race);
    }

    public void ApplyDamage(int amount)
    {
        if (amount <= 0 || !IsAlive)
            return;
        var updated = Math.Max(0, CurrentHp - amount);
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, updated);
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || !IsAlive || IsAwaitingLifeBar)
            return;
        var updated = Math.Min(MaxHp, CurrentHp + amount);
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, updated);
    }

    #region 生命条与行动循环

    internal void ConfigureLifeBars(IReadOnlyList<EnemyLifeBarDto> lifeBars, double hpScale, int initialActionIndex = 0)
    {
        LifeBars = lifeBars;
        ActionSequenceIndex = lifeBars.Count > 0 ? initialActionIndex : 0;
        _hpScale = hpScale;
        ActionCount = NextAction?.ActionCount ?? 1;
    }

    internal void AdvanceLifeBar()
    {
        LifeBarIndex++;
        _lifeBarSummons.Clear();
        _priorityAction = null;
        IsAwaitingLifeBar = false;
        ActionSequenceIndex = 0;
        IntentSkillId = null;
        PlannedSkillId = null;
        ActionCount = NextAction?.ActionCount ?? 1;
        var maxHp = Math.Max(1f, LifeBars[LifeBarIndex].MaxHp * (float)_hpScale);
        Asc.SetBaseValue(AttributeIds.MaxHealth, maxHp);
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, Asc.GetCurrentValue(AttributeIds.MaxHealth));
    }

    internal void CompleteSequenceAction(int lifeBarBefore, int sequenceBefore)
    {
        if (LifeBarIndex != lifeBarBefore || ActionSequenceIndex != sequenceBefore || IsAwaitingLifeBar)
            return;
        if (_priorityAction is not null)
        {
            ActionCount = 1;
            return;
        }
        if (NextAction is null || LifeBarIndex != lifeBarBefore || ActionSequenceIndex != sequenceBefore || IsAwaitingLifeBar)
            return;
        ActionSequenceIndex = (ActionSequenceIndex + 1) % LifeBars[LifeBarIndex].ActionSequence.Count;
        ActionCount = NextAction!.ActionCount;
    }

    internal void RefreshPriorityAction(CombatSimulation simulation)
    {
        if (LifeBars.Count == 0)
            return;
        var bar = LifeBars[LifeBarIndex];
        var active = bar.PrioritySkillId is not null && (bar.PrioritySummonCondition is { } summon
            ? simulation.EnemySummons.CanSummon(this, summon.EnemyId, summon.MaxAlive, summon.MaxSummons)
            : simulation.EnemyTeam.Enemies.Any(candidate => candidate.IsAlive &&
                simulation.Definitions.Store.TryGetEnemy(candidate.DefinitionId, out var definition) &&
                definition.Tags.Contains(bar.RequiredAliveEnemyTag!)));
        if (active == (_priorityAction is not null))
            return;
        _priorityAction = active ? new EnemyActionDto { SkillId = bar.PrioritySkillId! } : null;
        if (bar.PrioritySummonCondition is null)
            ActionSequenceIndex = 0;
        ActionCount = NextAction!.ActionCount;
    }

    #endregion
}