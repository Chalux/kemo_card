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
    public bool IsAlive => CurrentHp > 0;

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