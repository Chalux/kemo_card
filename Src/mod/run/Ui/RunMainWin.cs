using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Toast;

namespace KemoCard.Mod.Run.Ui;

public partial class RunMainWin : BaseWin
{
    [Export] private Label? _lblStory;
    [Export] private Label? _lblPhase;
    [Export] private Label? _lblRing;
    [Export] private Label? _lblSeed;
    [Export] private Label? _lblGold;
    [Export] private Button? _btnSave;
    [Export] private Button? _btnSaveExit;
    [Export] private Button? _btnQuickLoad;
    [Export] private Button? _btnAbandon;
    [Export] private Button? _btnDebug;
    [Export] private Button? _btnTeam;

    public override string UIId => RunUiIds.RunMain;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        if (_btnSave != null)
        {
            OnClicks(_btnSave, OnSave);
        }

        if (_btnSaveExit != null)
        {
            OnClicks(_btnSaveExit, OnSaveExit);
        }

        if (_btnQuickLoad != null)
        {
            OnClicks(_btnQuickLoad, OnQuickLoad);
        }

        if (_btnAbandon != null)
        {
            OnClicks(_btnAbandon, OnAbandon);
        }

        if (_btnDebug != null)
        {
            // 调试面板只在 debug 构建开放：release 里直接隐藏，避免“点了没反应”的死按钮。
            _btnDebug.Visible = OS.IsDebugBuild();
            if (_btnDebug.Visible)
            {
                OnClicks(_btnDebug, OnDebug);
            }
        }

        if (_btnTeam != null)
        {
            OnClicks(_btnTeam, OnTeamEdit);
        }

        BindPhaseChanges();
    }

    /// <summary>
    /// 订阅 Run 阶段变化，让本界面在开战 / 结算 / 进入下一环后重新取数，并在进入战斗时打开战斗界面。
    /// </summary>
    /// <remarks>
    /// 本界面是常驻 Win（<c>CacheTime = 0</c>，随 Run 会话存亡），此前只在 <c>OnOpen</c> 刷过一次视图：
    /// 从调试面板开战后相位标签仍是旧阶段，「进了战斗但界面毫无变化」看上去就像进不去战斗。
    /// 订阅经 <see cref="BaseUI.Binder"/> 登记，离场（或框架重新 <c>InitEvent</c>）时统一解绑。
    /// </remarks>
    private void BindPhaseChanges()
    {
        var run = RunRuntime.Current;
        if (run is null)
        {
            return;
        }

        var listener = run.State.OnRunPhaseChanged((_, _) =>
        {
            UpdateView();
            OpenCombatIfInBattle();
        }, this);
        Binder.Add(listener.Off);
    }

    protected override void OnOpen()
    {
        UpdateView();
        OpenCombatIfInBattle();
    }

    /// <summary>
    /// Run 规格 §14.1：阶段处于战斗且模拟器存在 → 打开 <c>CombatWin</c>（已打开则不重复）。
    /// 挂在阶段变化与 <c>OnOpen</c> 两处：调试面板开战、读档进战斗场景都能补开。
    /// </summary>
    private static void OpenCombatIfInBattle()
    {
        var run = RunRuntime.Current;
        if (run is null || run.Simulation is null || !run.State.Phase.IsCombatPhase())
        {
            return;
        }

        _ = RunUiController.OpenCombatAsync();
    }

    /// <summary>
    /// ESC：随时打开 / 关闭系统菜单（已打开则关闭）。
    /// </summary>
    /// <remarks>
    /// 走 <c>_UnhandledInput</c> 而不是 <c>_Input</c>：被弹出的下拉框 / 弹窗消费掉的 ESC
    /// （例如关掉 OptionButton 的弹出列表）不该同时把系统菜单也开起来。Run 会话期间 RunMain 常驻，
    /// 因此菜单在任何界面之上都能响应；"是否开着"的唯一判据是
    /// <see cref="RunUiController.IsPauseMenuOpen"/>，不在这里另存一份状态。
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (RunRuntime.Current is null || !@event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        _ = RunUiController.TogglePauseMenuAsync();
    }

    protected override void UpdateView()
    {
        var run = RunRuntime.Current;
        if (run == null)
        {
            Close();
            return;
        }

        var state = run.State;
        var storyName = state.StoryId;
        if (AppRoot.Services.ContentModPipeline.Registry.Store.TryGetStory(state.StoryId, out var story)
            && !string.IsNullOrWhiteSpace(story.DisplayNameId))
        {
            storyName = Localization.Tr(story.DisplayNameId);
        }

        if (_lblStory != null)
        {
            _lblStory.Text = storyName;
        }

        if (_lblPhase != null)
        {
            _lblPhase.Text = Localization.Tr($"UI_RUN_PHASE_{state.Phase.ToString().ToUpperInvariant()}");
        }

        if (_lblRing != null)
        {
            _lblRing.Text = $"{state.CurrentRing} / {state.MaxRing}";
        }

        if (_lblSeed != null)
        {
            _lblSeed.Text = state.RunSeed.ToString();
        }

        if (_lblGold != null)
        {
            _lblGold.Text = run.GetGold().ToString();
        }

        var inCombat = state.Phase.IsCombatPhase();
        if (_btnSave != null)
        {
            _btnSave.Disabled = inCombat;
        }

        if (_btnSaveExit != null)
        {
            _btnSaveExit.Disabled = inCombat;
        }

        if (_btnQuickLoad != null)
        {
            _btnQuickLoad.Disabled = inCombat;
        }
    }

    protected override void OnClose()
    {
        var run = RunRuntime.Current;
        if (run == null)
        {
            return;
        }

        if (run.State.Phase is ERunPhase.Battle or ERunPhase.BattleEnd or ERunPhase.Finished)
        {
            return;
        }

        RunRuntime.SaveCurrent();
    }

    #region 保存与快速读取

    private void OnSave()
    {
        RunRuntime.SaveCurrent();
        ToastService.Show("UI_RUN_SAVED");
    }

    private void OnSaveExit()
    {
        RunRuntime.SaveCurrent();
        Close();
        _ = GlobalModController.OpenMenuAsync();
    }

    private void OnQuickLoad()
    {
        if (RunRuntime.TryLoadLatest())
        {
            UpdateView();
        }
        else
        {
            ToastService.Show("UI_RUN_LOAD_FAILED");
        }
    }

    #endregion

    #region 放弃

    private void OnAbandon()
    {
        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = "UI_RUN_CONFIRM_ABANDON_TITLE",
            DescKey = "UI_RUN_CONFIRM_ABANDON_DESC",
            OkTextKey = "UI_ALERT_OK",
            CancelTextKey = "UI_ALERT_CANCEL",
            Time = 0,
            OkCallback = AbandonAndExit,
        });
    }

    private void AbandonAndExit()
    {
        RunRuntime.Abandon();
        Close();
        _ = GlobalModController.OpenMenuAsync();
    }

    #endregion

    #region 队伍编辑

    /// <summary>打开队伍编辑（战斗中由服务层拒绝编辑，入口保持可用以便查看当前队伍）。</summary>
    private void OnTeamEdit() => _ = RunUiController.OpenTeamEditAsync();

    #endregion

    #region 调试

    /// <summary>
    /// 打开调试面板（开发期工具）。战斗中也允许打开：调试面板本身可以判定战斗胜负退出战斗阶段。
    /// </summary>
    private void OnDebug() => _ = RunUiController.OpenRunDebugAsync();

    #endregion
}