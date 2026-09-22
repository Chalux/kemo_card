using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Ui;

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

    #region ESC 系统菜单

    /// <summary>系统菜单是否已打开（<see cref="UIVo.IsOpen"/> 覆盖"正在创建 → 已打开"的窗口期）。</summary>
    public static bool IsPauseMenuOpen() =>
        UIManager.Instance?.GetUIVo(RunUiIds.PauseMenu) is { IsOpen: true };

    /// <summary>
    /// ESC 的切换语义：已打开则关闭，未打开则打开。<see cref="RunMainWin"/> 的输入处理只负责转调这里，
    /// 因此"开/关"的唯一判据就是 <see cref="IsPauseMenuOpen"/>，不会出现两处各记一份状态而不同步。
    /// </summary>
    /// <remarks>
    /// <b>只有系统菜单是栈顶时 ESC 才关闭它</b>：确认框 / 设置 / 图鉴叠在它之上时，ESC 属于上层
    /// （或应当被忽略），不能把下层宿主拆掉——宿主是 <c>CacheTime = 0</c>，被关掉即销毁，
    /// 而挂在它上面的确认框回调还活着，会造成"确认框仍在、宿主已没了"的悬空状态。
    /// </remarks>
    public static async Task TogglePauseMenuAsync()
    {
        var manager = UIManager.Instance;
        if (manager is null)
        {
            return;
        }

        if (IsPauseMenuOpen())
        {
            if (manager.IsUITop(RunUiIds.PauseMenu))
            {
                manager.Close(RunUiIds.PauseMenu);
            }

            return;
        }

        await manager.OpenAsync<RunPauseDlg>(new UiId<RunPauseDlg>(RunUiIds.PauseMenu), default, null);
    }

    /// <summary>
    /// 保存并退出到主菜单：落盘 → 关闭本功能全部界面（含系统菜单与 RunMain）→ 回主菜单。
    /// </summary>
    /// <remarks>
    /// 战斗阶段不可调用（调用点已禁用按钮）：战斗态不在存档模型里，中途落盘会得到读不回来的档。
    /// </remarks>
    public static async Task SaveAndExitToMenuAsync()
    {
        RunRuntime.SaveCurrent();
        UIManager.Instance?.CloseByOwner(RunMod.FeatureId, destroy: true);
        await GlobalModController.OpenMenuAsync();
    }

    #endregion
}