using Godot;

namespace KemoCard.Mod.Global.Ui.Tip;

public partial class KeywordTipPanel : PanelContainer
{
    [Export] private Label? _txtTitle;
    [Export] private Label? _txtDesc;

    public void SetContent(string title, string description)
    {
        if (_txtTitle != null)
        {
            _txtTitle.Text = title ?? "";
        }

        if (_txtDesc != null)
        {
            _txtDesc.Text = description ?? "";
        }
    }
}
