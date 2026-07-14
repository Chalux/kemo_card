using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Ui.Comp;

namespace KemoCard.Mod.Global.Ui;

public partial class SettingDlg : BaseDlg
{
    [Export] public SettingDropdownRow? WindowModeRow { get; set; }
    [Export] public SettingDropdownRow? ResolutionRow { get; set; }
    [Export] public SettingToggleRow? VSyncRow { get; set; }
    [Export] public SettingDropdownRow? MaxFpsRow { get; set; }
    [Export] public SettingToggleRow? MasterMuteRow { get; set; }
    [Export] public SettingSliderRow? MasterVolumeRow { get; set; }
    [Export] public SettingToggleRow? MusicMuteRow { get; set; }
    [Export] public SettingSliderRow? MusicVolumeRow { get; set; }
    [Export] public SettingToggleRow? SfxMuteRow { get; set; }
    [Export] public SettingSliderRow? SfxVolumeRow { get; set; }
    [Export] public SettingDropdownRow? LanguageRow { get; set; }

    public override string UIId => GlobalUiIds.Setting;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent() { }
    protected override void OnOpen() { }
    protected override void UpdateView() { }
}
