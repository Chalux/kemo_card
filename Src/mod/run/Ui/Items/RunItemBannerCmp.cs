using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Run.Ui.Items;

public partial class RunItemBannerCmp : BaseCmp
{
    [Export] private Container? _potions;
    [Export] private Container? _relics;
    [Export] private PackedScene? _slotScene;
    public Action<int>? PotionRequested { get; set; }
    private string _relicKey = "";

    public void Bind(CombatItemInventory inventory, GameDefinitionStore store, bool enabled)
    {
        if (_potions is null || _relics is null || _slotScene is null) return;
        var slots = _potions.GetChildren().OfType<RunItemSlotCmp>().ToArray();
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = i;
            var id = inventory.Potions[i];
            slots[i].Bind(id, id is not null && store.TryGetItem(id, out var item) ? item : null, enabled);
            slots[i].Clicked = () => PotionRequested?.Invoke(slot);
        }
        var key = string.Join("\n", inventory.Relics);
        if (_relicKey == key) return;
        _relicKey = key;
        foreach (var child in _relics.GetChildren()) { _relics.RemoveChild(child); child.QueueFree(); }
        foreach (var id in inventory.Relics)
        {
            var slot = _slotScene.Instantiate<RunItemSlotCmp>();
            _relics.AddChild(slot);
            slot.Bind(id, store.TryGetItem(id, out var item) ? item : null, true);
            slot.MouseFilter = MouseFilterEnum.Ignore;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_potions is null) return;
        foreach (var slot in _potions.GetChildren().OfType<RunItemSlotCmp>()) slot.SetEnabled(enabled);
    }
}