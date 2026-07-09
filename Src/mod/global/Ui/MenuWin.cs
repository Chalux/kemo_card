using Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui;

public partial class MenuWin : BaseWin
{
    [Export] public Button? StartBtn { get; set; }
    [Export] public Button? LoadBtn { get; set; }
    [Export] public Button? SettingsBtn { get; set; }
    [Export] public Button? CodexBtn { get; set; }
    [Export] public Button? QuitBtn { get; set; }

    public override string UIId => GlobalUiIds.Menu;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
        if (CodexBtn != null)
        {
            OnClicks(CodexBtn, () => _ = GlobalModController.OpenCodexAsync());
        }

        if (QuitBtn != null)
        {
            OnClicks(QuitBtn, () => GetTree().Quit());
        }
    }

    protected override void OnOpen()
    {
    }

    protected override void UpdateView()
    {
    }
}
