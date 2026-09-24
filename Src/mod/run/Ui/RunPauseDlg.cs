using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Run.Ui;

/// <summary>
/// ESC 系统菜单：Run 进行中随时按 ESC 打开 / 关闭（切换逻辑见 <see cref="RunUiController.TogglePauseMenuAsync"/>，
/// 输入捕获挂在 <see cref="RunMainWin._UnhandledInput"/>）。
/// </summary>
/// <remarks>
/// <para>入口：设置 / 图鉴 / 词典（三者都叠在本菜单之上，关掉后回到这里）、保存并退出到主菜单、
/// 退出到桌面；战斗中额外显示「退出战斗」（回滚到战前快照并返回 Run 界面）。
/// 「继续游戏」= 关闭本菜单。</para>
/// <para>「保存并退出」在战斗阶段禁用并给出原因：战斗态不在存档模型里，中途落盘得到的是读不回来的档
/// （与 <c>RunMainWin</c> 的保存按钮同一规则，判定见 <see cref="ERunPhaseExtensions.IsCombatPhase"/>）。</para>
/// <para>「退出到桌面」先弹确认框：退出会丢掉最近一次自动保存之后的进度，不静默退出。</para>
/// </remarks>
public partial class RunPauseDlg : BaseDlg
{
    [Export] private Label? _lblTitle;
    [Export] private Label? _lblHint;
    [Export] private Button? _btnResume;
    [Export] private Button? _btnSettings;
    [Export] private Button? _btnCodex;
    [Export] private Button? _btnGlossary;
    [Export] private Button? _btnExitBattle;
    [Export] private Button? _btnSaveExit;
    [Export] private Label? _lblSaveExitBlocked;
    [Export] private Button? _btnQuitDesktop;

    public override string UIId => RunUiIds.PauseMenu;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        BindButton(_btnResume, Close);
        BindButton(_btnSettings, () => _ = GlobalModController.OpenSettingAsync());
        BindButton(_btnCodex, () => _ = GlobalModController.OpenCodexAsync());
        BindButton(_btnGlossary, () => _ = GlobalModController.OpenGlossaryAsync());
        BindButton(_btnExitBattle, OnExitBattle);
        BindButton(_btnSaveExit, () => _ = RunUiController.SaveAndExitToMenuAsync());
        BindButton(_btnQuitDesktop, OnQuitToDesktop);
    }

    protected override void OnOpen()
    {
        SetLabel(_lblTitle, "UI_PAUSE_TITLE");
        SetLabel(_lblHint, "UI_PAUSE_HINT");
        SetButton(_btnResume, "UI_PAUSE_RESUME");
        SetButton(_btnSettings, "UI_PAUSE_SETTINGS");
        SetButton(_btnCodex, "UI_PAUSE_CODEX");
        SetButton(_btnGlossary, "UI_PAUSE_GLOSSARY");
        SetButton(_btnExitBattle, "UI_PAUSE_EXIT_BATTLE");
        SetButton(_btnSaveExit, "UI_PAUSE_SAVE_EXIT");
        SetButton(_btnQuitDesktop, "UI_PAUSE_QUIT_DESKTOP");

        RefreshAvailability();
    }

    protected override void UpdateView() => RefreshAvailability();

    /// <summary>Run 不存在时（理论上进不到这里）只剩「退出到桌面」可用，并说明原因。</summary>
    private void RefreshAvailability()
    {
        var run = RunRuntime.Current;
        var inCombat = run?.State.Phase.IsCombatPhase() ?? false;

        // 「退出战斗」只在战斗阶段出现；非战斗阶段没有可退出的战斗。
        if (_btnExitBattle != null)
        {
            _btnExitBattle.Visible = inCombat;
        }

        if (_btnSaveExit != null)
        {
            _btnSaveExit.Disabled = run is null || inCombat;
        }

        if (_lblSaveExitBlocked != null)
        {
            var reasonKey = run is null ? "UI_PAUSE_NO_RUN" : inCombat ? "UI_PAUSE_SAVE_EXIT_BLOCKED" : "";
            _lblSaveExitBlocked.Visible = reasonKey.Length > 0;
            _lblSaveExitBlocked.Text = reasonKey.Length > 0 ? Localization.Tr(reasonKey) : "";
        }
    }

    /// <summary>
    /// 退出战斗：先弹二次确认（放弃当前战斗、回到战前状态），确认后回滚快照并离开战斗阶段
    /// （<c>CombatWin</c> 随阶段变化自关闭），再关掉本菜单回到 Run 界面。
    /// </summary>
    private void OnExitBattle()
    {
        var run = RunRuntime.Current;
        if (run is null || !run.State.Phase.IsCombatPhase())
        {
            return;
        }

        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = "UI_PAUSE_EXIT_BATTLE_TITLE",
            DescKey = "UI_PAUSE_EXIT_BATTLE_DESC",
            OkTextKey = "UI_ALERT_OK",
            CancelTextKey = "UI_ALERT_CANCEL",
            Time = 0,
            OkCallback = ExitBattleConfirmed,
        });
    }

    /// <summary>
    /// 确认后的实际退出。确认框生命周期可能长于本对话框（<c>CacheTime = 0</c>，关闭即销毁），
    /// 先校验节点有效性再 <see cref="Close"/>，避免访问已释放节点。
    /// </summary>
    private void ExitBattleConfirmed()
    {
        var run = RunRuntime.Current;
        if (run is null || !run.State.Phase.IsCombatPhase())
        {
            return;
        }

        run.EndBattle(won: false);
        if (GodotObject.IsInstanceValid(this))
        {
            Close();
        }
    }

    private void OnQuitToDesktop()
    {
        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = "UI_PAUSE_QUIT_TITLE",
            DescKey = "UI_PAUSE_QUIT_DESC",
            OkTextKey = "UI_ALERT_OK",
            CancelTextKey = "UI_ALERT_CANCEL",
            Time = 0,
            OkCallback = QuitToDesktop,
        });
    }

    /// <summary>
    /// 退出进程。<b>刻意是静态方法且不触碰本节点</b>：确认框属于 global 功能、生命周期可能长于本对话框
    /// （本界面 <c>CacheTime = 0</c>，关闭即销毁），实例方法一旦在宿主销毁后被回调就会访问已释放节点
    /// （<c>GetTree()</c> 抛 <c>ObjectDisposedException</c>），表现为"点了确定却没退出"。
    /// </summary>
    private static void QuitToDesktop() => (Engine.GetMainLoop() as SceneTree)?.Quit();

    private void BindButton(Button? button, Action action)
    {
        if (button != null)
        {
            OnClicks(button, action);
        }
    }

    private static void SetButton(Button? button, string key)
    {
        if (button != null)
        {
            button.Text = Localization.Tr(key);
        }
    }

    private static void SetLabel(Label? label, string key)
    {
        if (label != null)
        {
            label.Text = Localization.Tr(key);
        }
    }
}