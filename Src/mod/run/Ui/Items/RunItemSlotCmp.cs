using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.Items;

public partial class RunItemSlotCmp : BaseCmp
{
    [Export] private Button? _button;
    [Export] private TextureRect? _icon;
    [Export] private Label? _name;
    public Action? Clicked { get; set; }
    protected override void InitEvent()
    {
        if (_button is not null) Binder.OnPressed(_button, () => Clicked?.Invoke());
    }
    public void Bind(string? id, ItemDto? item, bool enabled)
    {
        if (_button is null) return;
        _button.Visible = id is not null;
        _button.Disabled = !enabled;
        if (id is null) return;
        var name = Localization.Tr(item?.DisplayNameId ?? id);
        var texture = ItemArtLoader.TryLoad(item);
        if (_icon is not null) { _icon.Texture = texture; _icon.Visible = texture is not null; }
        if (_name is not null) { _name.Text = name; _name.Visible = texture is null; }
        _button.TooltipText = name + "\n" + Localization.Tr(item?.DescId ?? "UI_ITEM_MISSING");
    }
    public void SetEnabled(bool enabled)
    {
        if (_button is not null) _button.Disabled = !enabled;
    }
}