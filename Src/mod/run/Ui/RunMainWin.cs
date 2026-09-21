using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Orbs;
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
    [Export] private Control? _orbPanel;
    [Export] private Label? _lblOrbs;
    [Export] private Label? _lblOrbHint;
    [Export] private Button? _btnTriggerOrbs;

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

        if (_btnTriggerOrbs != null)
        {
            OnClicks(_btnTriggerOrbs, OnTriggerOrbs);
        }

        if (_btnTeam != null)
        {
            OnClicks(_btnTeam, OnTeamEdit);
        }
    }

    protected override void OnOpen()
    {
        UpdateView();
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

        var inCombat = state.Phase is ERunPhase.Battle or ERunPhase.BattleEnd;
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

        RefreshOrbPanel(run);
    }

    /// <summary>
    /// 充能球指示器（右上角，正式战斗界面落地前的过渡挂点）：显示队列里的球数与种类；
    /// 球数达到门槛时可点击触发（走正式 <see cref="TriggerOrbsCommand"/> 管线）。
    /// </summary>
    private void RefreshOrbPanel(RunController run)
    {
        var simulation = run.Simulation;
        var inBattle = simulation is not null && run.State.Phase is ERunPhase.Battle or ERunPhase.BattleEnd;
        if (_orbPanel != null)
        {
            _orbPanel.Visible = inBattle;
        }

        if (!inBattle || simulation is null)
        {
            return;
        }

        var queue = simulation.Orbs.Queue;
        if (_lblOrbs != null)
        {
            _lblOrbs.Text = queue.IsEmpty
                ? Localization.Tr("UI_ORB_EMPTY")
                : string.Join(
                    "\n",
                    queue.Orbs
                        .GroupBy(orb => orb.OrbTypeId, StringComparer.Ordinal)
                        .Select(group => $"{OrbLabel(group.Key)} ×{group.Count()}"));
        }

        if (_lblOrbHint != null)
        {
            _lblOrbHint.Text = string.Format(
                Localization.Tr("UI_ORB_HINT"),
                OrbQueue.Capacity,
                OrbQueue.ManualTriggerThreshold);
        }

        if (_btnTriggerOrbs != null)
        {
            _btnTriggerOrbs.Disabled = !queue.CanTriggerManually;
        }
    }

    private string OrbLabel(string orbTypeId)
    {
        var store = AppRoot.Services.ContentModPipeline.Registry.Store;
        return store.TryGetOrbType(orbTypeId, out var orbType) && !string.IsNullOrWhiteSpace(orbType.DisplayNameId)
            ? Localization.Tr(orbType.DisplayNameId)
            : orbTypeId;
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

    #region 充能球

    private void OnTriggerOrbs()
    {
        var run = RunRuntime.Current;
        var simulation = run?.Simulation;
        if (simulation is null)
        {
            return;
        }

        var result = simulation.TryApply(new TriggerOrbsCommand());
        if (!result.Success)
        {
            ToastService.Show("UI_ORB_TRIGGER_FAILED");
        }

        UpdateView();
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