using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class EnemyUnit
{
    public string RuntimeId { get; }
    public string DefinitionId { get; }
    public AbilitySystemComponent Asc { get; }

    /// <summary>挂在该敌人身上的 buff 容器（减益等）。</summary>
    public BuffContainer Buffs { get; }

    public int CurrentHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.Health));
    public int MaxHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.MaxHealth));
    public string? IntentSkillId { get; set; }

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
    public bool IsAlive => CurrentHpExact > 0f;

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
        if (amount <= 0 || !IsAlive)
            return;
        var updated = Math.Min(MaxHp, CurrentHp + amount);
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, updated);
    }
}