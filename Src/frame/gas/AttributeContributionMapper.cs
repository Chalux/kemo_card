using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas;

public static class AttributeContributionMapper
{
    private static readonly IReadOnlyDictionary<string, string> LegacyToAttributeId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hpCap"] = AttributeIds.MaxHealth,
            ["maxHp"] = AttributeIds.MaxHealth,
            ["physicalAttack"] = AttributeIds.PhysicalAttack,
            ["physicalDefense"] = AttributeIds.PhysicalDefense,
            ["magicAttack"] = AttributeIds.MagicAttack,
            ["magicDefense"] = AttributeIds.MagicDefense,
            ["healPower"] = AttributeIds.HealPower,
            ["maxEnergy"] = AttributeIds.MaxEnergy,
            ["initialEnergy"] = AttributeIds.InitialEnergy,
        };

    public static Dictionary<string, float> MapCardStats(CardStatBlockDto? stats)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        if (stats is null)
            return result;

        foreach (var (key, value) in stats.Attributes)
            AddContribution(result, key, value);

        MergeLegacyIfMissing(result, "hpCap", stats.HpCap);
        MergeLegacyIfMissing(result, "physicalAttack", stats.PhysicalAttack);
        MergeLegacyIfMissing(result, "physicalDefense", stats.PhysicalDefense);
        MergeLegacyIfMissing(result, "magicAttack", stats.MagicAttack);
        MergeLegacyIfMissing(result, "magicDefense", stats.MagicDefense);
        MergeLegacyIfMissing(result, "healPower", stats.HealPower);
        MergeLegacyIfMissing(result, "maxEnergy", stats.MaxEnergy);
        MergeLegacyIfMissing(result, "initialEnergy", stats.InitialEnergy);

        return result;
    }

    public static Dictionary<string, float> BuildEnemyBaseAttributes(EnemyDto enemy)
    {
        ArgumentNullException.ThrowIfNull(enemy);

        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (key, value) in enemy.BaseAttributes)
            AddContribution(result, key, value);

        if (!result.ContainsKey(AttributeIds.MaxHealth))
            result[AttributeIds.MaxHealth] = enemy.MaxHp;

        return result;
    }

    private static void MergeLegacyIfMissing(Dictionary<string, float> result, string legacyKey, int legacyValue)
    {
        if (legacyValue == 0)
            return;

        var normalized = NormalizeAttributeKey(legacyKey);
        if (result.ContainsKey(normalized))
            return;

        result[normalized] = legacyValue;
    }

    private static void AddContribution(Dictionary<string, float> target, string rawKey, float value)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || Math.Abs(value) <= float.Epsilon)
            return;

        var key = NormalizeAttributeKey(rawKey);
        target.TryGetValue(key, out var current);
        target[key] = current + value;
    }

    private static string NormalizeAttributeKey(string key)
    {
        if (LegacyToAttributeId.TryGetValue(key, out var mapped))
            return mapped;

        return key;
    }
}