using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.Items;

public partial class RunOptionCmp : BaseCmp
{
    [Export] private Button? _button;
    [Export] private Label? _details;
    private Action? _action;
    protected override void InitEvent()
    {
        if (_button is not null) Binder.OnPressed(_button, () => _action?.Invoke());
    }
    public void Bind(string text, string details, bool enabled, Action action)
    {
        _action = action;
        if (_button is not null) { _button.Text = text; _button.Disabled = !enabled; }
        if (_details is not null) { _details.Text = details; _details.Visible = details.Length > 0; }
    }
}