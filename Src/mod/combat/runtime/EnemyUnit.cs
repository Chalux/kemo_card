using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class EnemyUnit
{
    public string RuntimeId { get; }
    public string DefinitionId { get; }
    public AbilitySystemComponent Asc { get; }
    public int CurrentHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.Health));
    public int MaxHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.MaxHealth));
    public string? IntentSkillId { get; set; }

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(baseAttributes);
        if (!baseAttributes.TryGetValue(AttributeIds.MaxHealth, out var maxHealth) || maxHealth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(baseAttributes), "MaxHealth must be greater than 0.");
        RuntimeId = runtimeId;
        DefinitionId = definitionId;
        Asc = new CombatAscFactory().BuildAsc(
            new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
            new Dictionary<string, float>(baseAttributes, StringComparer.Ordinal));
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, maxHealth);
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