using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>整局共享道具账本，战斗使用同一实例，不含 Run 或节点依赖。</summary>
public sealed class CombatItemInventory
{
    public const int PotionCapacity = 3;
    private readonly string?[] _potions = new string?[PotionCapacity];
    private readonly List<string> _relics = [];
    public IReadOnlyList<string?> Potions => Array.AsReadOnly(_potions);
    public IReadOnlyList<string> Relics => _relics.AsReadOnly();

    #region 变更与恢复

    public bool CanAdd(ItemDto item, int? replaceSlot = null) => item.Kind == EItemKind.Relic
        ? !_relics.Contains(item.Id, StringComparer.Ordinal)
        : replaceSlot.HasValue ? replaceSlot is >= 0 and < PotionCapacity : Array.IndexOf(_potions, null) >= 0;

    public bool TryAdd(ItemDto item, int? replaceSlot = null)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || !CanAdd(item, replaceSlot))
            return false;
        if (item.Kind == EItemKind.Relic)
            _relics.Add(item.Id);
        else
            _potions[replaceSlot ?? Array.IndexOf(_potions, null)] = item.Id;
        return true;
    }

    public bool RemovePotion(int slot, string? expectedId = null)
    {
        if (slot < 0 || slot >= PotionCapacity || _potions[slot] is null ||
            expectedId is not null && _potions[slot] != expectedId)
            return false;
        _potions[slot] = null;
        return true;
    }

    public void Restore(IEnumerable<string>? relics, IEnumerable<string?>? potions)
    {
        _relics.Clear();
        _relics.AddRange((relics ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal));
        Array.Clear(_potions);
        var values = (potions ?? []).Take(PotionCapacity).ToArray();
        for (var i = 0; i < values.Length; i++)
            _potions[i] = string.IsNullOrWhiteSpace(values[i]) ? null : values[i];
    }

    #endregion
}