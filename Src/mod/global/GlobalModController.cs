using KemoCard.Frame.Mvc;
using KemoCard.Frame.Ui;
using KemoCard.Mod.Global.Events;
using KemoCard.Mod.Global.Save;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Global;

public sealed class GlobalModController(GlobalMod model, GlobalSaveService saveService, IUiManager uiManager) : BaseController<GlobalMod>(model)
{
    private readonly GlobalSaveService _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
    private readonly IUiManager _uiManager = uiManager ?? throw new ArgumentNullException(nameof(uiManager));

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

    public Task OpenMenuAsync(CancellationToken cancellationToken = default)
    {
        return _uiManager.OpenDlgAsync(
            GlobalUiIds.Menu,
            new MenuDlgPayload(() => _uiManager.CloseDlg()),
            cancellationToken);
    }

    public Task OpenCodexAsync(CancellationToken cancellationToken = default)
    {
        return _uiManager.OpenDlgAsync(
            GlobalUiIds.Codex,
            new CodexDlgPayload(() => _uiManager.CloseDlg()),
            cancellationToken);
    }
}
