using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Ui;

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
        if (StartBtn != null)
        {
            OnClicks(StartBtn, () => _ = RunUiController.OpenStorySelectAsync());
        }

        if (LoadBtn != null)
        {
            OnClicks(LoadBtn, OnContinue);
        }

        if (SettingsBtn != null)
        {
            OnClicks(SettingsBtn, () => _ = GlobalModController.OpenSettingAsync());
        }

        if (CodexBtn != null)
        {
            OnClicks(CodexBtn, () => _ = GlobalModController.OpenCodexAsync());
        }

        if (QuitBtn != null)
        {
            OnClicks(QuitBtn, () => GetTree().Quit());
        }
    }

    private void OnContinue()
    {
        if (RunRuntime.TryLoadLatest())
        {
            _ = RunUiController.OpenRunMainAsync();
        }
    }

    protected override void OnOpen()
    {
        if (LoadBtn != null)
        {
            LoadBtn.Disabled = !RunRuntime.HasSave;
        }
    }

    protected override void UpdateView()
    {
    }
}