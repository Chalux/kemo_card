using Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod.Global.Events;
using KemoCard.Mod.Global.Save;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Global;

public sealed class GlobalModController(GlobalMod model, GlobalSaveService saveService) : BaseController<GlobalMod>(model)
{
    private readonly GlobalSaveService _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));

    public GlobalSaveDto Snapshot => Model.Current;

    public void LoadFromDisk()
    {
        Model.Current = _saveService.LoadOrDefault().Normalize();
        Model.NotifyGlobalSaveChanged(new GlobalSaveChangedPayload { Snapshot = Model.Current });
    }

    public void SaveToDisk()
    {
        _saveService.Save(Model.Current);
        Model.NotifyGlobalSaveChanged(new GlobalSaveChangedPayload { Snapshot = Model.Current });
    }

    /// <summary>
    /// 写入内容管道重建后的内容版本指纹（内容 Mod 规格 §5：<c>ContentVersionHash</c> 与
    /// <c>GameDefinitionRegistry.DefinitionVersion</c> 联动）。指纹与旧值不同说明启用集 / 定义集已变化，
    /// Run 存档据此判断内容是否被改动；值未变则不写盘、不发事件。
    /// </summary>
    public void UpdateContentVersionHash(string contentVersionHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersionHash);
        if (string.Equals(Model.Current.ContentVersionHash, contentVersionHash, StringComparison.Ordinal))
        {
            return;
        }

        Model.Current = Model.Current with { ContentVersionHash = contentVersionHash };
        SaveToDisk();
    }

    public bool TryGetSetting(string key, out string value)
    {
        return Model.Current.Settings.TryGetValue(key, out value!);
    }

    public void SetSetting(string key, string value)
    {
        Model.Current.Settings[key] = value;
    }

    public bool IsAchievementUnlocked(string achievementId)
    {
        return Model.Current.Achievements.TryGetValue(achievementId, out var unlocked) && unlocked;
    }

    public void UnlockAchievement(string achievementId)
    {
        Model.Current.Achievements[achievementId] = true;
    }

    public bool IsContentUnlocked(string unlockId)
    {
        return Model.Current.Unlocks.TryGetValue(unlockId, out var unlocked) && unlocked;
    }

    public void UnlockContent(string unlockId)
    {
        Model.Current.Unlocks[unlockId] = true;
    }

    public bool IsCodexUnlocked(string entryId)
    {
        return Model.Current.CodexEntries.TryGetValue(entryId, out var unlocked) && unlocked;
    }

    public void UnlockCodexEntry(string entryId)
    {
        Model.Current.CodexEntries[entryId] = true;
    }

    public static async Task<UIVo?> OpenMenuAsync()
    {
        return await (UIManager.Instance?.OpenAsync<MenuWin>(new UiId<MenuWin>(GlobalUiIds.Menu), default, null) ?? Task.FromResult<UIVo?>(null));
    }

    public static async Task<UIVo?> OpenCodexAsync()
    {
        return await (UIManager.Instance?.OpenAsync<CodexDlg>(new UiId<CodexDlg>(GlobalUiIds.Codex), default, null) ?? Task.FromResult<UIVo?>(null));
    }

    public static async Task<UIVo?> OpenSettingAsync()
    {
        return await (UIManager.Instance?.OpenAsync<SettingDlg>(new UiId<SettingDlg>(GlobalUiIds.Setting), default, null) ?? Task.FromResult<UIVo?>(null));
    }

    public static async Task<UIVo?> OpenAlertAsync(AlertDlgPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return await (UIManager.Instance?.OpenAsync(new UiId<AlertDlgPayload>(GlobalUiIds.Alert), payload) ?? Task.FromResult<UIVo?>(null));
    }
}