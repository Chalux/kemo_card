using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Combat;

public readonly record struct CharacterAttributes
{
    private static readonly IReadOnlyDictionary<string, float> EmptyValues =
        new Dictionary<string, float>(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, float>? _values;

    public CharacterAttributes(IReadOnlyDictionary<string, float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, float>(values, StringComparer.Ordinal);
    }

    public CharacterAttributes(
        int hpCap,
        int physicalAttack,
        int physicalDefense,
        int magicAttack,
        int magicDefense,
        int healPower,
        int physicalShield,
        int magicShield,
        int damageScale,
        int damageTakenScale,
        int taunt,
        int drawCount,
        int maxEnergy,
        int initialEnergy)
        : this(new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = hpCap,
            [AttributeIds.PhysicalAttack] = physicalAttack,
            [AttributeIds.PhysicalDefense] = physicalDefense,
            [AttributeIds.MagicAttack] = magicAttack,
            [AttributeIds.MagicDefense] = magicDefense,
            [AttributeIds.HealPower] = healPower,
            [AttributeIds.Damage] = damageScale,
            [AttributeIds.DamageTakenScale] = damageTakenScale,
            [AttributeIds.MaxEnergy] = maxEnergy,
            [AttributeIds.InitialEnergy] = initialEnergy,
        })
    {
        _ = physicalShield;
        _ = magicShield;
        _ = taunt;
        _ = drawCount;
    }

    public static CharacterAttributes Zero { get; } = new(EmptyValues);

    public IReadOnlyDictionary<string, float> Values => _values ?? EmptyValues;

    public int HpCap => ReadInt(AttributeIds.MaxHealth);
    public int PhysicalAttack => ReadInt(AttributeIds.PhysicalAttack);
    public int PhysicalDefense => ReadInt(AttributeIds.PhysicalDefense);
    public int MagicAttack => ReadInt(AttributeIds.MagicAttack);
    public int MagicDefense => ReadInt(AttributeIds.MagicDefense);
    public int HealPower => ReadInt(AttributeIds.HealPower);
    public int PhysicalShield => 0;
    public int MagicShield => 0;
    public int DamageScale => ReadInt(AttributeIds.Damage);
    public int DamageTakenScale => ReadInt(AttributeIds.DamageTakenScale);
    public int Taunt => 0;
    public int DrawCount => 0;
    public int MaxEnergy => ReadInt(AttributeIds.MaxEnergy);
    public int InitialEnergy => ReadInt(AttributeIds.InitialEnergy);

    public static CharacterAttributes FromMap(IReadOnlyDictionary<string, float> values) => new(values);

    public static CharacterAttributes FromCardStats(CardStatBlockDto stats) =>
        new(AttributeContributionMapper.MapCardStats(stats));

    public static CharacterAttributes operator +(CharacterAttributes left, CharacterAttributes right)
    {
        var merged = new Dictionary<string, float>(left.Values, StringComparer.Ordinal);
        foreach (var (attributeId, value) in right.Values)
        {
            merged.TryGetValue(attributeId, out var current);
            merged[attributeId] = current + value;
        }

        return new CharacterAttributes(merged);
    }

    private int ReadInt(string attributeId)
    {
        if (!Values.TryGetValue(attributeId, out var value))
            return 0;
        return (int)MathF.Round(value);
    }
}