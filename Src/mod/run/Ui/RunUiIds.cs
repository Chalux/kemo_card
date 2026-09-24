namespace KemoCard.Mod.Run.Ui;

public static class RunUiIds
{
    public const string StorySelect = "StorySelectDlg";
    public const string RunMain = "RunMainWin";

    /// <summary>Run 调试面板（开发期工具，注册在 <c>EUILayer.Debug</c> 顶层）。</summary>
    public const string RunDebug = "RunDebugDlg";

    /// <summary>队伍编辑（槽位 / 角色池 / 详情预览）。</summary>
    public const string TeamEdit = "RunTeamEditDlg";

    /// <summary>队伍编辑的二级界面：单个角色的卡组与上阵。</summary>
    public const string CharacterDeck = "RunCharacterDeckDlg";

    /// <summary>ESC 系统菜单（设置 / 图鉴 / 词典 / 保存并退出 / 退出到桌面）。</summary>
    public const string PauseMenu = "RunPauseDlg";

    /// <summary>战斗界面（Run 规格 §14）：阶段进入 Battle 时打开，随一场战斗存亡。场景在 <c>Src/mod/run/Ui/Combat</c>。</summary>
    public const string Combat = "CombatWin";
}