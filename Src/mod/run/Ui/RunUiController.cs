using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Mod.Run.Ui;

public static class RunUiController
{
    public static async Task<UIVo?> OpenStorySelectAsync()
    {
        return await (UIManager.Instance?.OpenAsync<StorySelectDlg>(new UiId<StorySelectDlg>(RunUiIds.StorySelect), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }

    public static async Task<UIVo?> OpenRunMainAsync()
    {
        return await (UIManager.Instance?.OpenAsync<RunMainWin>(new UiId<RunMainWin>(RunUiIds.RunMain), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }

    /// <summary>打开 Run 调试面板（层级由注册项指定为 <see cref="EUILayer.Debug"/>，无需在此传参）。</summary>
    public static async Task<UIVo?> OpenRunDebugAsync()
    {
        if (!OS.IsDebugBuild()) return null;
        return await (UIManager.Instance?.OpenAsync<RunDebugDlg>(new UiId<RunDebugDlg>(RunUiIds.RunDebug), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }

    /// <summary>打开队伍编辑（槽位 / 角色池 / 详情预览）。</summary>
    public static async Task<UIVo?> OpenTeamEditAsync()
    {
        return await (UIManager.Instance?.OpenAsync<RunTeamEditDlg>(new UiId<RunTeamEditDlg>(RunUiIds.TeamEdit), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }

    /// <summary>打开队伍编辑的二级界面：该角色的卡组编辑 + 上阵到指定槽位。</summary>
    public static async Task<UIVo?> OpenCharacterDeckAsync(string instanceId, int slotIndex)
    {
        return await (UIManager.Instance?.OpenAsync(
                new UiId<RunCharacterDeckDlgPayload>(RunUiIds.CharacterDeck),
                new RunCharacterDeckDlgPayload { InstanceId = instanceId, SlotIndex = slotIndex })
            ?? Task.FromResult<UIVo?>(null));
    }
}