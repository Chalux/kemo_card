using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Run.Ui.Items;

public partial class RunItemBannerCmp : BaseCmp
{
    [Export] private Container? _consumables;
    public Action<int>? ConsumableRequested { get; set; }

    public void Bind(CombatItemInventory inventory, GameDefinitionStore store, bool enabled)
    {
        if (_consumables is null) return;
        var slots = _consumables.GetChildren().OfType<RunItemSlotCmp>().ToArray();
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = i;
            var id = inventory.Consumables[i];
            slots[i].Bind(id, id is not null && store.TryGetItem(id, out var item) ? item : null, enabled);
            slots[i].Clicked = () => ConsumableRequested?.Invoke(slot);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_consumables is null) return;
        foreach (var slot in _consumables.GetChildren().OfType<RunItemSlotCmp>()) slot.SetEnabled(enabled);
    }
}