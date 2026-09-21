using System;
using System.Collections.Generic;
using Godot;
using KemoCard.Frame.Audio;
using KemoCard.Frame.Display;
using KemoCard.Frame.Locale;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Def;
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

    [Export] public SettingDropdownRow? PotentialConsumeModeRow { get; set; }
    [Export] public SettingDropdownRow? PotentialProposalsRow { get; set; }
    [Export] public SettingToggleRow? PotentialUnlimitedRow { get; set; }

    private bool _suppressApply;

    private string _confirmedWindowMode = WindowModeIds.Windowed;
    private string _confirmedResolution = DisplaySettingKeys.DefaultResolutionId;
    private bool _displayConfirmBusy;
    private int _displayConfirmEpoch;
    private string _revertWindowMode = WindowModeIds.Windowed;
    private string _revertResolution = DisplaySettingKeys.DefaultResolutionId;
    private string _trialWindowMode = WindowModeIds.Windowed;
    private string _trialResolution = DisplaySettingKeys.DefaultResolutionId;

    private bool _pendingVSync = DisplaySettingKeys.DefaultVSync;
    private int _pendingMaxFps = DisplaySettingKeys.DefaultMaxFps;
    private int _pendingMasterVolume = AudioSettingKeys.DefaultVolumePercent;
    private int _pendingMusicVolume = AudioSettingKeys.DefaultVolumePercent;
    private int _pendingSfxVolume = AudioSettingKeys.DefaultVolumePercent;
    private int _pendingMuteFlag = AudioSettingKeys.DefaultMuteFlag;
    private string _pendingLanguage = LocaleSettingKeys.DefaultLanguage;
    private string _pendingPotentialConsumeMode = MultiplayerSettingKeys.PotentialConsumeModeDefault;
    private int _pendingPotentialProposals = MultiplayerSettingKeys.DefaultProposalsPerRing;
    private bool _pendingPotentialUnlimited;

    public override string UIId => GlobalUiIds.Setting;
    public override string UIDir => "Src/mod/global/Ui";

    #region 生命周期

    protected override void InitEvent()
    {
        if (WindowModeRow != null)
        {
            Action<string> handler = _ => OnDisplayRowChanged();
            Binder.Bind(() => WindowModeRow.ValueChanged += handler, () => WindowModeRow.ValueChanged -= handler);
        }

        if (ResolutionRow != null)
        {
            Action<string> handler = _ => OnDisplayRowChanged();
            Binder.Bind(() => ResolutionRow.ValueChanged += handler, () => ResolutionRow.ValueChanged -= handler);
        }

        if (VSyncRow != null)
        {
            Binder.Bind(() => VSyncRow.ValueChanged += OnVSyncChanged, () => VSyncRow.ValueChanged -= OnVSyncChanged);
        }

        if (MaxFpsRow != null)
        {
            Binder.Bind(() => MaxFpsRow.ValueChanged += OnMaxFpsChanged, () => MaxFpsRow.ValueChanged -= OnMaxFpsChanged);
        }

        if (MasterMuteRow != null)
        {
            Action<bool> handler = muted => OnMuteChanged(SoundBus.Master, muted);
            Binder.Bind(() => MasterMuteRow.ValueChanged += handler, () => MasterMuteRow.ValueChanged -= handler);
        }

        if (MasterVolumeRow != null)
        {
            Action<double> handler = value => OnVolumeChanged(SoundBus.Master, value);
            Binder.Bind(() => MasterVolumeRow.ValueChanged += handler, () => MasterVolumeRow.ValueChanged -= handler);
        }

        if (MusicMuteRow != null)
        {
            Action<bool> handler = muted => OnMuteChanged(SoundBus.Sound, muted);
            Binder.Bind(() => MusicMuteRow.ValueChanged += handler, () => MusicMuteRow.ValueChanged -= handler);
        }

        if (MusicVolumeRow != null)
        {
            Action<double> handler = value => OnVolumeChanged(SoundBus.Sound, value);
            Binder.Bind(() => MusicVolumeRow.ValueChanged += handler, () => MusicVolumeRow.ValueChanged -= handler);
        }

        if (SfxMuteRow != null)
        {
            Action<bool> handler = muted => OnMuteChanged(SoundBus.Sfx, muted);
            Binder.Bind(() => SfxMuteRow.ValueChanged += handler, () => SfxMuteRow.ValueChanged -= handler);
        }

        if (SfxVolumeRow != null)
        {
            Action<double> handler = value => OnVolumeChanged(SoundBus.Sfx, value);
            Binder.Bind(() => SfxVolumeRow.ValueChanged += handler, () => SfxVolumeRow.ValueChanged -= handler);
        }

        if (LanguageRow != null)
        {
            Binder.Bind(() => LanguageRow.ValueChanged += OnLanguageChanged, () => LanguageRow.ValueChanged -= OnLanguageChanged);
        }

        if (PotentialConsumeModeRow != null)
        {
            Binder.Bind(
                () => PotentialConsumeModeRow.ValueChanged += OnPotentialConsumeModeChanged,
                () => PotentialConsumeModeRow.ValueChanged -= OnPotentialConsumeModeChanged);
        }

        if (PotentialProposalsRow != null)
        {
            Binder.Bind(
                () => PotentialProposalsRow.ValueChanged += OnPotentialProposalsChanged,
                () => PotentialProposalsRow.ValueChanged -= OnPotentialProposalsChanged);
        }

        if (PotentialUnlimitedRow != null)
        {
            Binder.Bind(
                () => PotentialUnlimitedRow.ValueChanged += OnPotentialUnlimitedChanged,
                () => PotentialUnlimitedRow.ValueChanged -= OnPotentialUnlimitedChanged);
        }
    }

    protected override void OnOpen()
    {
        var settings = Facade<GlobalModController>().Snapshot.Settings;
        var display = DisplaySettingsParser.Parse(settings);
        var masterVolume = ReadIntSetting(settings, AudioSettingKeys.MasterVolume, AudioSettingKeys.DefaultVolumePercent);
        var musicVolume = ReadIntSetting(settings, AudioSettingKeys.SoundVolume, AudioSettingKeys.DefaultVolumePercent);
        var sfxVolume = ReadIntSetting(settings, AudioSettingKeys.SfxVolume, AudioSettingKeys.DefaultVolumePercent);
        var muteFlag = ReadIntSetting(settings, AudioSettingKeys.MuteFlag, AudioSettingKeys.DefaultMuteFlag);

        PopulateOptions();
        ApplyNameKeys();

        _suppressApply = true;
        WindowModeRow?.SetSelectedId(display.WindowMode, notify: false);
        ResolutionRow?.SetSelectedId(display.ResolutionId, notify: false);
        VSyncRow?.SetValue(display.VSync, animate: false, notify: false);
        MaxFpsRow?.SetSelectedId(display.MaxFps.ToString(), notify: false);
        MasterMuteRow?.SetValue(MuteFlagBits.IsMuted(muteFlag, SoundBus.Master), animate: false, notify: false);
        MasterVolumeRow?.SetValue(masterVolume, notify: false);
        MusicMuteRow?.SetValue(MuteFlagBits.IsMuted(muteFlag, SoundBus.Sound), animate: false, notify: false);
        MusicVolumeRow?.SetValue(musicVolume, notify: false);
        SfxMuteRow?.SetValue(MuteFlagBits.IsMuted(muteFlag, SoundBus.Sfx), animate: false, notify: false);
        SfxVolumeRow?.SetValue(sfxVolume, notify: false);
        LanguageRow?.SetSelectedId(display.LanguageCode, notify: false);
        PotentialConsumeModeRow?.SetSelectedId(_pendingPotentialConsumeMode, notify: false);
        PotentialProposalsRow?.SetSelectedId(_pendingPotentialProposals.ToString(), notify: false);
        PotentialUnlimitedRow?.SetValue(_pendingPotentialUnlimited, animate: false, notify: false);
        _suppressApply = false;

        _confirmedWindowMode = display.WindowMode;
        _confirmedResolution = display.ResolutionId;
        _pendingVSync = display.VSync;
        _pendingMaxFps = display.MaxFps;
        _pendingMasterVolume = masterVolume;
        _pendingMusicVolume = musicVolume;
        _pendingSfxVolume = sfxVolume;
        _pendingMuteFlag = muteFlag;
        _pendingLanguage = display.LanguageCode;
        _pendingPotentialConsumeMode = settings.TryGetValue(MultiplayerSettingKeys.PotentialConsumeMode, out var consumeMode) &&
            !string.IsNullOrWhiteSpace(consumeMode)
                ? consumeMode
                : MultiplayerSettingKeys.PotentialConsumeModeDefault;
        _pendingPotentialProposals = ReadIntSetting(
            settings,
            MultiplayerSettingKeys.PotentialProposalsPerRing,
            MultiplayerSettingKeys.DefaultProposalsPerRing);
        _pendingPotentialUnlimited = settings.TryGetValue(MultiplayerSettingKeys.PotentialProposalsUnlimited, out var unlimited) &&
            unlimited == "1";
        _displayConfirmBusy = false;
    }

    protected override void UpdateView()
    {
    }

    protected override void OnClose()
    {
        CancelDisplayConfirmIfNeeded();
        PersistNonConfirmSettings();
    }

    #endregion

    #region 打开灌值

    private void PopulateOptions()
    {
        WindowModeRow?.SetOptions(
        [
            (WindowModeIds.Windowed, "UI_SETTING_WINDOW_MODE_WINDOWED"),
            (WindowModeIds.BorderlessWindow, "UI_SETTING_WINDOW_MODE_BORDERLESS_WINDOW"),
            (WindowModeIds.BorderlessFullscreen, "UI_SETTING_WINDOW_MODE_BORDERLESS_FULLSCREEN"),
            (WindowModeIds.Fullscreen, "UI_SETTING_WINDOW_MODE_FULLSCREEN"),
        ]);

        var resolutions = new List<(string id, string label)>();
        foreach (var entry in ResolutionRegistry.All)
        {
            resolutions.Add((entry.Id, entry.DisplayLabel));
        }

        ResolutionRow?.SetOptions(resolutions);

        MaxFpsRow?.SetOptions(
        [
            ("30", "30"),
            ("60", "60"),
            ("120", "120"),
            ("144", "144"),
            ("165", "165"),
            ("244", "244"),
            ("0", "UI_SETTING_FPS_UNLIMITED"),
        ]);

        var locales = new List<(string id, string label)>();
        foreach (var entry in LocaleRegistry.All)
        {
            locales.Add((entry.Code, entry.DisplayNameKey));
        }

        LanguageRow?.SetOptions(locales);

        PotentialConsumeModeRow?.SetOptions(
        [
            (MultiplayerSettingKeys.PotentialConsumeModeFree, "UI_SETTING_MP_POTENTIAL_MODE_FREE"),
            (MultiplayerSettingKeys.PotentialConsumeModeVote, "UI_SETTING_MP_POTENTIAL_MODE_VOTE"),
        ]);

        var proposals = new List<(string id, string label)>();
        for (var count = 1; count <= 5; count++)
        {
            proposals.Add((count.ToString(), count.ToString()));
        }

        PotentialProposalsRow?.SetOptions(proposals);
    }

    private void ApplyNameKeys()
    {
        WindowModeRow?.SetNameKey("UI_SETTING_WINDOW_MODE");
        ResolutionRow?.SetNameKey("UI_SETTING_RESOLUTION");
        VSyncRow?.SetNameKey("UI_SETTING_VSYNC");
        MaxFpsRow?.SetNameKey("UI_SETTING_MAX_FPS");
        MasterMuteRow?.SetNameKey("UI_SETTING_MASTER_MUTE");
        MasterVolumeRow?.SetNameKey("UI_SETTING_MASTER_VOLUME");
        MusicMuteRow?.SetNameKey("UI_SETTING_MUSIC_MUTE");
        MusicVolumeRow?.SetNameKey("UI_SETTING_MUSIC_VOLUME");
        SfxMuteRow?.SetNameKey("UI_SETTING_SFX_MUTE");
        SfxVolumeRow?.SetNameKey("UI_SETTING_SFX_VOLUME");
        LanguageRow?.SetNameKey("UI_SETTING_LANGUAGE");
        PotentialConsumeModeRow?.SetNameKey("UI_SETTING_MP_POTENTIAL_MODE");
        PotentialProposalsRow?.SetNameKey("UI_SETTING_MP_PROPOSALS");
        PotentialUnlimitedRow?.SetNameKey("UI_SETTING_MP_PROPOSALS_UNLIMITED");
    }

    private static int ReadIntSetting(IReadOnlyDictionary<string, string> settings, string key, int defaultValue)
    {
        if (!settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw, out var value) ? value : defaultValue;
    }

    #endregion

    #region 立即应用（非确认项）

    private void OnVSyncChanged(bool enabled)
    {
        if (_suppressApply)
        {
            return;
        }

        _pendingVSync = enabled;
        DisplayServer.WindowSetVsyncMode(
            enabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    private void OnMaxFpsChanged(string id)
    {
        if (_suppressApply)
        {
            return;
        }

        if (!int.TryParse(id, out var fps) || fps < 0)
        {
            AppLog.Warning($"无效最高帧率选项: {id}", nameof(SettingDlg));
            return;
        }

        _pendingMaxFps = fps;
        Engine.MaxFps = fps;
    }

    private void OnVolumeChanged(int busIndex, double value)
    {
        if (_suppressApply)
        {
            return;
        }

        var percent = (int)Math.Round(value);
        switch (busIndex)
        {
            case SoundBus.Master:
                _pendingMasterVolume = percent;
                break;
            case SoundBus.Sound:
                _pendingMusicVolume = percent;
                break;
            case SoundBus.Sfx:
                _pendingSfxVolume = percent;
                break;
        }

        Sound.SetBusVolumePercent(busIndex, percent);
    }

    private void OnMuteChanged(int busIndex, bool muted)
    {
        if (_suppressApply)
        {
            return;
        }

        _pendingMuteFlag = MuteFlagBits.WithMuted(_pendingMuteFlag, busIndex, muted);
        Sound.SetMuteFlag(_pendingMuteFlag);
    }

    private void OnLanguageChanged(string code)
    {
        if (_suppressApply)
        {
            return;
        }

        _pendingLanguage = code;
        LocaleSettingsApplier.Apply(code);
    }

    #region 联机潜能设置

    private void OnPotentialConsumeModeChanged(string id)
    {
        if (_suppressApply)
        {
            return;
        }

        _pendingPotentialConsumeMode = id;
    }

    private void OnPotentialProposalsChanged(string id)
    {
        if (_suppressApply)
        {
            return;
        }

        if (!int.TryParse(id, out var count) || count <= 0)
        {
            return;
        }

        _pendingPotentialProposals = count;
    }

    private void OnPotentialUnlimitedChanged(bool unlimited)
    {
        if (_suppressApply)
        {
            return;
        }

        _pendingPotentialUnlimited = unlimited;
    }

    #endregion

    #endregion

    #region 显示确认流

    private void OnDisplayRowChanged()
    {
        if (_suppressApply)
        {
            return;
        }

        var intendedMode = WindowModeRow?.SelectedId ?? _confirmedWindowMode;
        var intendedResolution = ResolutionRow?.SelectedId ?? _confirmedResolution;

        if (_displayConfirmBusy)
        {
            AbortDisplayConfirm(restoreDropdowns: false);
        }

        BeginDisplayConfirm(intendedMode, intendedResolution);
    }

    private void BeginDisplayConfirm(string trialMode, string trialResolution)
    {
        _revertWindowMode = _confirmedWindowMode;
        _revertResolution = _confirmedResolution;
        _trialWindowMode = trialMode;
        _trialResolution = trialResolution;

        WindowModeRow?.SetSelectedId(trialMode, notify: false);
        ResolutionRow?.SetSelectedId(trialResolution, notify: false);

        var window = GetWindow();
        DisplaySettingsApplier.ApplyWindowMode(window, _trialWindowMode);
        DisplaySettingsApplier.ApplyResolution(window, _trialResolution);

        _displayConfirmBusy = true;
        var epoch = _displayConfirmEpoch;
        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = "UI_ALERT_DISPLAY_TITLE",
            DescKey = "UI_ALERT_DISPLAY_DESC",
            Time = 10,
            CallbackWhenClose = AlertClosePolicy.Cancel,
            OkCallback = () => OnDisplayConfirmOk(epoch),
            CancelCallback = () => OnDisplayConfirmCancel(epoch),
        });
    }

    private void OnDisplayConfirmOk(int epoch)
    {
        if (epoch != _displayConfirmEpoch || !_displayConfirmBusy)
        {
            return;
        }

        _displayConfirmBusy = false;
        _confirmedWindowMode = _trialWindowMode;
        _confirmedResolution = _trialResolution;

        var gc = Facade<GlobalModController>();
        var changes = new List<string>();
        SetSettingLogged(gc, DisplaySettingKeys.WindowMode, _confirmedWindowMode, changes);
        SetSettingLogged(gc, DisplaySettingKeys.Resolution, _confirmedResolution, changes);
        SaveSettingsLogged(gc, changes);
    }

    private void OnDisplayConfirmCancel(int epoch)
    {
        if (epoch != _displayConfirmEpoch || !_displayConfirmBusy)
        {
            return;
        }

        _displayConfirmBusy = false;
        RevertDisplayTrial(restoreDropdowns: true);
    }

    private void CancelDisplayConfirmIfNeeded()
    {
        AbortDisplayConfirm(restoreDropdowns: true);
    }

    private void AbortDisplayConfirm(bool restoreDropdowns)
    {
        if (!_displayConfirmBusy)
        {
            return;
        }

        _displayConfirmBusy = false;
        _displayConfirmEpoch++;
        RevertDisplayTrial(restoreDropdowns);
        UIManager.Instance?.Close(GlobalUiIds.Alert);
    }

    private void RevertDisplayTrial(bool restoreDropdowns)
    {
        var window = GetWindow();
        DisplaySettingsApplier.ApplyWindowMode(window, _revertWindowMode);
        DisplaySettingsApplier.ApplyResolution(window, _revertResolution);

        if (!restoreDropdowns)
        {
            return;
        }

        WindowModeRow?.SetSelectedId(_revertWindowMode, notify: false);
        ResolutionRow?.SetSelectedId(_revertResolution, notify: false);
    }

    #endregion

    #region 关窗持久化

    private void PersistNonConfirmSettings()
    {
        var gc = Facade<GlobalModController>();
        var changes = new List<string>();
        SetSettingLogged(gc, DisplaySettingKeys.VSync, _pendingVSync ? "1" : "0", changes);
        SetSettingLogged(gc, DisplaySettingKeys.MaxFps, _pendingMaxFps.ToString(), changes);
        SetSettingLogged(gc, AudioSettingKeys.MasterVolume, _pendingMasterVolume.ToString(), changes);
        SetSettingLogged(gc, AudioSettingKeys.SoundVolume, _pendingMusicVolume.ToString(), changes);
        SetSettingLogged(gc, AudioSettingKeys.SfxVolume, _pendingSfxVolume.ToString(), changes);
        SetSettingLogged(gc, AudioSettingKeys.MuteFlag, _pendingMuteFlag.ToString(), changes);
        SetSettingLogged(gc, LocaleSettingKeys.Language, _pendingLanguage, changes);
        SetSettingLogged(gc, MultiplayerSettingKeys.PotentialConsumeMode, _pendingPotentialConsumeMode, changes);
        SetSettingLogged(gc, MultiplayerSettingKeys.PotentialProposalsPerRing, _pendingPotentialProposals.ToString(), changes);
        SetSettingLogged(gc, MultiplayerSettingKeys.PotentialProposalsUnlimited, _pendingPotentialUnlimited ? "1" : "0", changes);
        SaveSettingsLogged(gc, changes);
    }

    private static void SetSettingLogged(
        GlobalModController gc,
        string key,
        string value,
        List<string> changes)
    {
        gc.Snapshot.Settings.TryGetValue(key, out var previous);
        previous ??= string.Empty;
        if (!string.Equals(previous, value, StringComparison.Ordinal))
        {
            changes.Add($"{key}: '{previous}' -> '{value}'");
        }

        gc.SetSetting(key, value);
    }

    private static void SaveSettingsLogged(GlobalModController gc, List<string> changes)
    {
        if (changes.Count > 0)
        {
            AppLog.Info($"设置写盘变更 ({changes.Count}): {string.Join("; ", changes)}", nameof(SettingDlg));
        }
        else
        {
            AppLog.Info("设置写盘（无键值变更）", nameof(SettingDlg));
        }

        gc.SaveToDisk();
    }

    #endregion
}