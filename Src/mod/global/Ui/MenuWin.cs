using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Global.Ui;

public partial class MenuWin : BaseWin<EmptyPayload>
{
    [Export] public Button? StartBtn { get; set; }
    [Export] public Button? LoadBtn { get; set; }
    [Export] public Button? SettingsBtn { get; set; }
    [Export] public Button? QuitBtn { get; set; }

    public override string UIId => GlobalUiIds.Menu;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
        // if (StartBtn != null)
        // {
        //     OnClicks(StartBtn, Close);
        // }
    }

    protected override void OnOpen()
    {
    }

    protected override void UpdateView()
    {
    }
}