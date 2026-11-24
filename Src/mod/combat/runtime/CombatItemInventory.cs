using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>整局共享道具账本，战斗使用同一实例，不含 Run 或节点依赖。</summary>
public sealed class CombatItemInventory
{
    public const int ConsumableCapacity = 5;
    private readonly string?[] _consumables = new string?[ConsumableCapacity];
    private readonly List<string> _relics = [];
    public IReadOnlyList<string?> Consumables => Array.AsReadOnly(_consumables);
    public IReadOnlyList<string> Relics => _relics.AsReadOnly();

    #region 变更与恢复

    public bool CanAdd(ItemDto item, int? replaceSlot = null) => item.Kind == EItemKind.Relic
        ? !_relics.Contains(item.Id, StringComparer.Ordinal)
        : replaceSlot.HasValue ? replaceSlot is >= 0 and < ConsumableCapacity : Array.IndexOf(_consumables, null) >= 0;

    public bool TryAdd(ItemDto item, int? replaceSlot = null)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || !CanAdd(item, replaceSlot))
            return false;
        if (item.Kind == EItemKind.Relic)
            _relics.Add(item.Id);
        else
            _consumables[replaceSlot ?? Array.IndexOf(_consumables, null)] = item.Id;
        return true;
    }

    public bool RemoveConsumable(int slot, string? expectedId = null)
    {
        if (slot < 0 || slot >= ConsumableCapacity || _consumables[slot] is null ||
            expectedId is not null && _consumables[slot] != expectedId)
            return false;
        _consumables[slot] = null;
        return true;
    }

    public void Restore(IEnumerable<string>? relics, IEnumerable<string?>? consumables)
    {
        _relics.Clear();
        _relics.AddRange((relics ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal));
        Array.Clear(_consumables);
        var values = (consumables ?? []).Take(ConsumableCapacity).ToArray();
        for (var i = 0; i < values.Length; i++)
            _consumables[i] = string.IsNullOrWhiteSpace(values[i]) ? null : values[i];
    }

    #endregion
}