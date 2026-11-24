using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Marble;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Toast;
using KemoCard.Mod.Run.Ui.CombatUi.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 战斗界面（Run 规格 §14）：顶部道具横幅（预留）/ 左栏队伍总血 + 非操控角色 / 中间友方与敌方舞台 /
/// 右上暂停 + 充能球 / 底栏当前角色 · 卡组 · 5 手牌 · 墓地 · 确认。
/// 所有玩法指令只走 <see cref="CombatSimulation.TryApply"/>；表现事件经 <see cref="CombatPresentationDirector"/>
/// 播放，播完 <see cref="SyncFromState"/> 对账（战斗规格 §16）。
/// </summary>
public partial class CombatWin : BaseWin, ICombatStageView
{
    [Export] private Items.RunItemBannerCmp? _itemSlots;
    [Export] private HpBarCmp? _sharedHp;
    [Export] private Container? _bench;
    [Export] private Container? _allyStage;
    [Export] private Container? _enemyStage;
    [Export] private PackedScene? _enemyScene;
    [Export] private Button? _btnPause;
    [Export] private OrbQueueCmp? _orbs;
    [Export] private ActorInfoCmp? _actor;
    [Export] private CardPileCmp? _deck;
    [Export] private Container? _hand;
    [Export] private Control? _battlefield;
    [Export] private Control? _dropSurface;
    [Export] private CardPileCmp? _grave;
    [Export] private Button? _btnPlayConfirm;
    [Export] private Button? _btnConfirm;
    [Export] private Label? _lblHint;
    [Export] private Control? _fxLayer;
    [Export] private PackedScene? _damageNumberScene;
    [Export] private Label? _phaseBanner;
    [Export] private CombatAnimator? _animator;
    [Export] private MarblePanelCmp? _marble;
    [Export] private MarblePanelCmp? _marbleMain;
    [Export] private Control? _combatLayout;
    [Export] private Control? _marblePreview;
    [Export] private MarblePanelCmp? _marblePreviewPanel;

    private readonly CombatPresentationDirector _director = new();
    private readonly List<PartyMemberCmp> _members = [];
    private readonly List<AllyUnitCmp> _allies = [];
    private readonly List<HandSlotCmp> _hands = [];
    private readonly List<EnemyUnitCmp> _enemies = [];

    private CombatUiState? _ui;
    private bool _endHandled;

    public override string UIId => RunUiIds.Combat;
    public override string UIDir => "Src/mod/run/Ui/Combat";

    private DiscardPlan? _discardPlan;
    private IReadOnlyList<CombatTargetRef> _discardTargets = [];

    /// <summary>弹珠阶段固定步长累加器：每渲染帧把真实时间换算成 1/60s 物理步，暂停 / 播放表现时停表。</summary>
    private const int MarbleMaxStepsPerFrame = 8;
    private static readonly double MarbleStepSeconds = 1.0 / MarblePhysicsConfig.StepsPerSecond;
    private int _marbleSpeed = 1;
    private double _marbleAccumulator;
    private bool _marbleLayoutActive;

    private static CombatSimulation? Simulation => RunRuntime.Current?.Simulation;

    #region 生命周期

    protected override void OnReady()
    {
        if (_itemSlots is not null) _itemSlots.ConsumableRequested = OnConsumableRequested;
        CollectChildren(_bench, _members);
        CollectChildren(_allyStage, _allies);
        CollectChildren(_hand, _hands);
        foreach (var member in _members)
            member.ReleaseRequested = OnReleaseSkillRequested;
        if (_actor != null)
            _actor.ReleaseRequested = () => OnReleaseSkillRequested(_ui?.ControlledSlot ?? -1);
        if (_phaseBanner != null)
            _phaseBanner.Visible = false;
        if (_marbleMain != null)
        {
            _marbleMain.Visible = false;
            _marbleMain.SetCompact(false);
            _marbleMain.SetToggleVisible(false);
            _marbleMain.SetInteractive(false);
        }
    }

    private static void CollectChildren<T>(Node? parent, List<T> into) where T : Node
    {
        into.Clear();
        if (parent is null)
            return;

        foreach (var child in parent.GetChildren())
        {
            if (child is T typed)
                into.Add(typed);
        }
    }

    protected override void InitEvent()
    {
        if (_btnPause != null)
            OnClicks(_btnPause, () => _ = RunUiController.TogglePauseMenuAsync());
        if (_btnPlayConfirm != null)
            OnClicks(_btnPlayConfirm, OnPlayConfirm);
        if (_btnConfirm != null)
            OnClicks(_btnConfirm, OnEndTurn);

        // 组件回调是属性赋值：进树登记、离场清空（与 Binder 的信号解绑同步）。
        if (_orbs != null)
            _orbs.TriggerRequested = OnTriggerOrbs;
        if (_marble != null)
            _marble.ToggleRequested = () => SetMarblePreview(true);
        if (_marblePreviewPanel != null)
            _marblePreviewPanel.ToggleRequested = () => SetMarblePreview(false);
        if (_marbleMain?.Board is { } marbleBoard)
        {
            marbleBoard.LaunchRequested = OnMarbleLaunchRequested;
            marbleBoard.AimCancelled = OnMarbleAimCancelled;
        }
        foreach (var member in _members)
            member.Clicked = OnMemberClicked;
        foreach (var ally in _allies)
            ally.Clicked = OnAllyTargetClicked;
        foreach (var hand in _hands)
        {
            hand.Clicked = slot => OnHandClicked(hand.CharacterIndex, slot);
            if (hand.Card is { } face)
                face.DragRequested = card => BeginCardDrag(hand, hand.CharacterIndex, card);
        }
        if (_dropSurface is { } surface)
        {
            surface.SetDragForwarding(default, Callable.From<Vector2, Variant, bool>(PreviewDrop),
                Callable.From<Vector2, Variant>(DropCard));
            Bind(() => surface.Draw += DrawCardAim, () => surface.Draw -= DrawCardAim);
        }

        var run = RunRuntime.Current;
        if (run is not null)
        {
            var listener = run.State.OnRunPhaseChanged((_, _) => OnRunPhaseChanged(), this);
            Binder.Add(listener.Off);
        }
    }

    protected override void OnExitTree()
    {
        if (_itemSlots is not null) _itemSlots.ConsumableRequested = null;
        if (_orbs != null)
            _orbs.TriggerRequested = null;
        if (_marble != null)
            _marble.ToggleRequested = null;
        if (_marblePreviewPanel != null)
            _marblePreviewPanel.ToggleRequested = null;
        if (_marbleMain?.Board is { } marbleBoard)
        {
            marbleBoard.LaunchRequested = null;
            marbleBoard.AimCancelled = null;
        }
        foreach (var member in _members)
            member.Clicked = null;
        foreach (var ally in _allies)
            ally.Clicked = null;
        foreach (var hand in _hands)
        {
            hand.Clicked = null;
            if (hand.Card is { } face)
                face.DragRequested = null;
        }
        foreach (var enemy in _enemies)
            enemy.Clicked = null;
        _director.Clear();
    }

    protected override void OnOpen()
    {
        var run = RunRuntime.Current;
        var simulation = run?.Simulation;
        if (run is null || simulation is null)
        {
            Close();
            return;
        }

        _endHandled = false;
        _ui = CombatUiState.CreateLocal();
        _animator?.Bind(this);
        _marbleAccumulator = 0;
        _marbleLayoutActive = false;
        SetMarblePreview(false);
        ApplyMarbleLayout(false);
        SyncMarbleLayout(simulation);

        // 开战管线（BattleStart）在界面打开前已跑完：这些事件不再回放，直接对账到当前状态。
        simulation.Presentation.Clear();
        BindAllies(simulation);
        RebuildEnemies();
        SyncFromState();
    }

    protected override void UpdateView() => SyncFromState();

    protected override void OnClose()
    {
        CancelCardDrag();
        SetMarblePreview(false);
        _marbleLayoutActive = false;
        ApplyMarbleLayout(false);
        _director.Clear();
    }

    private void OnRunPhaseChanged()
    {
        var run = RunRuntime.Current;
        if (run is null || !run.State.Phase.IsCombatPhase())
        {
            Close();
            return;
        }

        SyncFromState();
    }

    /// <summary>ESC：弹珠阶段先取消瞄准；有待出牌先取消；否则开 / 关系统菜单。两者都吃掉事件，不穿透到下层 RunMainWin。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (RunRuntime.Current is null)
            return;

        var simulation = Simulation;
        if (simulation?.Phase == ECombatPhase.Marble && _marbleMain?.Board is { Interactive: true } marbleBoard)
        {
            // 右键在盘面外也要能取消瞄准；盘面内的右键由 MarbleBoardCmp 自己吃掉。
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && marbleBoard.HasAim)
            {
                GetViewport().SetInputAsHandled();
                marbleBoard.CancelAim();
                return;
            }

            if (@event.IsActionPressed("ui_cancel") && marbleBoard.HasAim)
            {
                GetViewport().SetInputAsHandled();
                marbleBoard.CancelAim();
                return;
            }
        }

        if (!@event.IsActionPressed("ui_cancel"))
            return;

        GetViewport().SetInputAsHandled();
        if (_marblePreview?.Visible == true)
        {
            SetMarblePreview(false);
            return;
        }
        if (_ui is { HasPending: true })
        {
            _ui.ClearPending();
            SyncFromState();
            return;
        }

        _ = RunUiController.TogglePauseMenuAsync();
    }

    #endregion

    #region 弹珠阶段（固定步长 / 布局 / 输入）

    /// <summary>
    /// 固定步长累加器：只在弹珠阶段推进 <see cref="CombatSimulation.StepMarble"/>（1/60s 物理步）。
    /// 暂停菜单打开、界面不可见或表现播放锁输入时停表。相位切走后自动推进自动相位并播放表现事件。
    /// </summary>
    public override void _Process(double delta)
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null)
            return;

        SyncMarbleLayout(simulation);
        // 待选目标 / 弃牌与战斗内模态（消耗品、确认框、调试面板等）打开期间停表：
        // 否则最后一颗球会在弹窗背后跑完普攻、敌方回合，相位推进后玩家仍在操作旧回合的弹窗。
        if (simulation.Phase != ECombatPhase.Marble || _ui.InputLocked || _ui.HasPending ||
            RunUiController.IsCombatCovered())
            return;
        if (!IsVisibleInTree() || RunUiController.IsPauseMenuOpen())
            return;

        var before = (simulation.Marble.Count, simulation.Marble.ActiveBalls.Count, simulation.MarbleTimeLimitReached);
        _marbleAccumulator += Math.Clamp(delta, 0d, 0.25d) * _marbleSpeed;
        var steps = 0;
        while (_marbleAccumulator >= MarbleStepSeconds && steps < MarbleMaxStepsPerFrame)
        {
            _marbleAccumulator -= MarbleStepSeconds;
            steps++;
            simulation.StepMarble();
            if (simulation.Phase != ECombatPhase.Marble)
                break;
        }

        if (steps >= MarbleMaxStepsPerFrame && _marbleAccumulator >= MarbleStepSeconds)
            _marbleAccumulator = 0;

        if (simulation.Phase != ECombatPhase.Marble)
        {
            // 队列与场上球清空（或提前结束）后由模拟器切到 CardExecution：
            // 把普攻 / 敌方等自动相位一次推完，再统一播放期间记录的表现事件。
            _marbleAccumulator = 0;
            SyncMarbleLayout(simulation);
            simulation.AdvanceAutomaticPhases();
            _ = PlayPendingAsync();
            return;
        }

        if (before != (simulation.Marble.Count, simulation.Marble.ActiveBalls.Count, simulation.MarbleTimeLimitReached))
        {
            // 球位移由盘面自行重绘；计数、回收或时限改变时更新伤害预览与操作。
            _marbleMain?.Bind(simulation.Marble);
            RefreshMarbleControls(simulation);
        }
    }

    /// <summary>按当前相位切换「出牌布局」与「弹珠主盘布局」；幂等，相位未变时零操作。</summary>
    private void SyncMarbleLayout(CombatSimulation simulation)
    {
        var marble = simulation.Phase == ECombatPhase.Marble;
        if (marble == _marbleLayoutActive)
            return;

        _marbleLayoutActive = marble;
        if (!marble)
            CancelMarblePendingActions();
        ApplyMarbleLayout(marble);
    }

    /// <summary>
    /// 弹珠阶段结束时清掉在该阶段发起的待选目标 / 弃牌与选牌计划：
    /// 相位自动推进后模拟器已进入下一阶段，残留的待选态没有合法提交时机
    /// （结束回合会被禁用，下一次点击角色会误放上一阶段的技能）。
    /// </summary>
    private void CancelMarblePendingActions()
    {
        _discardPlan = null;
        _discardTargets = [];
        _ui?.ClearPending();
    }

    private void ApplyMarbleLayout(bool marble)
    {
        if (marble)
        {
            _marbleSpeed = 1;
            SetMarblePreview(false);
            // 新阶段从"未瞄准"开始：沿用上一阶段的指针位置会让轨迹凭空出现。
            _marbleMain?.Board?.CancelAim();
        }

        // 弹珠阶段：主盘展开、手牌收起；敌人 / 队伍技能 / 道具 / 暂停仍在原位可用。
        if (_marbleMain != null)
        {
            _marbleMain.Visible = marble;
            _marbleMain.SetInputLocked(_ui?.InputLocked ?? false);
            _marbleMain.SetInteractive(marble);
        }
        if (_hand != null)
            _hand.Visible = !marble;
        if (_allyStage != null)
            _allyStage.Visible = !marble;
        if (_marble != null)
            _marble.Visible = !marble;
        _marbleAccumulator = 0;
    }

    /// <summary>弹珠阶段的操作提示与提前结束按钮（按钮可用性只读 <see cref="CombatSimulation.CanEndMarbleEarly"/>）。</summary>
    private void RefreshMarbleControls(CombatSimulation simulation)
    {
        if (_ui is null)
            return;

        var locked = _ui.InputLocked;
        // 先清掉上一轮分配的预计伤害：Count 变化后分配可能转移到别的敌人，
        // 未重置的旧目标会把过时的「当前 → 预计剩余」一直留在血条上。
        foreach (var enemy in _enemies)
            enemy.HpBar?.ClearMarbleForecast();
        var forecast = simulation.NormalAttacks.PreviewMarbles(simulation, simulation.Marble.Count, simulation.MarbleMarkedEnemyIndex);
        foreach (var allocation in forecast.Allocations)
            _enemies.ElementAtOrDefault(allocation.EnemyIndex)?.HpBar?.SetMarbleForecast(allocation.RemainingHealth);
        // 选主动技目标期间不能继续发射弹珠：否则弹珠会与待提交的技能目标状态互相干扰。
        _marbleMain?.SetInteractive(!_ui.HasPending);
        _marbleMain?.SetInputLocked(locked);
        if (_btnConfirm != null)
        {
            _btnConfirm.Text = Localization.Tr("UI_COMBAT_MARBLE_END");
            _btnConfirm.Disabled = locked || !simulation.CanEndMarbleEarly;
        }
        if (_btnPlayConfirm != null)
        {
            _btnPlayConfirm.Visible = true;
            _btnPlayConfirm.Disabled = locked;
            _btnPlayConfirm.Text = string.Format(Localization.Tr("UI_COMBAT_MARBLE_SPEED"), _marbleSpeed);
        }
        if (_lblHint != null)
        {
            var key = locked ? "UI_COMBAT_HINT_PLAYING"
                : _ui.PendingMode == ECombatPendingMode.PickActiveTarget ? "UI_COMBAT_HINT_PICK_ACTIVE_TARGET"
                : simulation.Marble.CanLaunch
                    ? "UI_COMBAT_MARBLE_HINT_AIM"
                    : "UI_COMBAT_MARBLE_HINT_FLYING";
            _lblHint.Visible = true;
            _lblHint.Text = Localization.Tr(key);
        }
    }

    private void OnMarbleLaunchRequested(float angleRadians)
    {
        // 盘面在待选目标期间已禁用交互；这里再兜一层，防止队列中的输入绕过 UI 态。
        if (Simulation?.Phase != ECombatPhase.Marble || _ui is { HasPending: true })
            return;

        ApplyCommand(new LaunchMarbleCommand(angleRadians));
    }

    private void OnMarbleAimCancelled()
    {
        var simulation = Simulation;
        if (simulation is not null)
            RefreshMarbleControls(simulation);
    }

    #endregion

    #region 指令与播放

    private bool ApplyCommand(ICombatCommand command)
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null || _ui.InputLocked)
            return false;

        var result = simulation.TryApply(command);
        if (!result.Success)
        {
            ToastService.Show("UI_COMBAT_COMMAND_FAILED", result.Error is null ? "" : Localization.Tr(result.Error));
        }
        else
        {
            // 自动推进普攻和敌方行动，再统一播放表现事件。
            simulation.AdvanceAutomaticPhases();
            // 相位可能刚切到 / 切出弹珠阶段：布局立即跟随，不等待动画播完。
            SyncMarbleLayout(simulation);
        }

        _ = PlayPendingAsync();
        return result.Success;
    }

    private async Task PlayPendingAsync()
    {
        var simulation = Simulation;
        if (simulation is null || _animator is null || _ui is null)
            return;

        _director.Enqueue(simulation.Presentation.Drain());
        if (_director.IsPlaying)
            return;

        // 只锁交互、不对账：血条 / 手牌等要等动画逐条推进到终值，提前对账会让动画没有落差可播。
        SetInputLocked(true);
        RefreshInteractivity();
        await _director.PlayAsync(_animator);
        // 播放期间界面可能已被关闭销毁（战斗结束 / 调试面板结束战斗）：对已释放节点对账会抛异常。
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;

        SetInputLocked(false);
        SyncFromState();
        CheckBattleEnd();
    }

    private void SetInputLocked(bool locked)
    {
        if (locked)
            CancelCardDrag();
        if (_ui != null)
            _ui.InputLocked = locked;
    }

    private void OnConsumableRequested(int slot)
    {
        var sim = Simulation;
        if (sim is null || _ui is null || _ui.InputLocked ||
            sim.Phase is not (ECombatPhase.Player or ECombatPhase.Marble))
            return;
        var character = _ui.ControlledSlot;
        var id = sim.Items.Consumables[slot];
        if (id is null) return;
        _ui.ClearPending();
        _ = RunUiController.OpenConsumableAsync(new Items.RunConsumableDlgPayload
        {
            Slot = slot, CharacterIndex = character, CanUse = true, Changed = SyncFromState,
            UseRequested = targets =>
            {
                if (GodotObject.IsInstanceValid(this) && IsInsideTree())
                    ApplyCommand(new UseConsumableCommand(slot, id, character, targets));
            },
        });
    }

    private void CheckBattleEnd()
    {
        var run = RunRuntime.Current;
        var simulation = run?.Simulation;
        if (run is null || simulation is null || _endHandled)
            return;

        if (simulation.Phase is not (ECombatPhase.Victory or ECombatPhase.Defeat))
            return;

        _endHandled = true;
        var won = simulation.Phase == ECombatPhase.Victory;
        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = won ? "UI_COMBAT_RESULT_VICTORY_TITLE" : "UI_COMBAT_RESULT_DEFEAT_TITLE",
            DescKey = won ? "UI_COMBAT_RESULT_VICTORY_DESC" : "UI_COMBAT_RESULT_DEFEAT_DESC",
            OkTextKey = "UI_ALERT_OK",
            Time = 0,
            OkCallback = () => EndBattle(won),
            // 结算弹窗必须收尾：_endHandled 已置位，不会再弹第二次，而取消（或任何其它关闭路径）不调
            // EndBattle 就会把战斗永久停在 Victory/Defeat——界面全部禁用、只能退出到桌面。
            CancelCallback = () => EndBattle(won),
            CallbackWhenClose = AlertClosePolicy.Ok,
        });
    }

    /// <summary>静态且不触碰节点：确认框生命周期可能长于本界面（同 <c>RunPauseDlg.QuitToDesktop</c> 的口径）。</summary>
    private static void EndBattle(bool won)
    {
        var run = RunRuntime.Current;
        if (run is null || !run.State.Phase.IsCombatPhase())
            return;

        try
        {
            run.EndBattle(won);
        }
        catch (InvalidOperationException)
        {
            // 阶段已被别处（调试面板）推进：忽略。
        }
    }

    #endregion

    #region 交互回调

    private void OnMemberClicked(int slotIndex)
    {
        if (_ui is null || !_ui.TrySelectSlot(slotIndex))
            return;

        SyncFromState();
    }

    private void OnHandClicked(int characterIndex, int handSlotIndex)
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null || _ui.InputLocked || simulation.Phase != ECombatPhase.Player)
            return;

        if (!CombatCommandValidator.TryGetCharacter(simulation, characterIndex, out var character, out _))
            return;
        if (handSlotIndex < 0 || handSlotIndex >= character.HandSlots.Count)
            return;

        var slot = character.HandSlots[handSlotIndex];
        if (slot.IsEmpty || slot.CardId is null || slot.RuntimeInstanceId is null)
            return;
        var choice = new HandCardChoice(characterIndex, slot.RuntimeInstanceId);
        if (!_ui.CanSelectHandCard(character.IsSealed, _discardPlan?.Owners.Contains(characterIndex) == true))
            return;

        if (_ui.PendingMode == ECombatPendingMode.PickDiscard)
        {
            _ui.ToggleDiscard(choice);
            SyncFromState();
            return;
        }

        if (!simulation.Definitions.Store.TryGetCard(slot.CardId, out var card))
            return;

        if (_ui.ControlledSlot != characterIndex)
            _ui.TrySelectSlot(characterIndex);
        if (CombatTargeting.RequiresExplicitTarget(card))
        {
            _ui.TogglePending(choice, ECombatPendingMode.PickTarget);
            SyncFromState();
            return;
        }

        _ui.ClearPending();
        ApplyCommand(new PlayCardCommand(characterIndex, handSlotIndex, [], slot.RuntimeInstanceId));
    }

    private void OnPlayConfirm()
    {
        var simulation = Simulation;
        if (simulation?.Phase == ECombatPhase.Marble && _ui is { InputLocked: false })
        {
            _marbleSpeed = _marbleSpeed == 1 ? 2 : 1;
            RefreshMarbleControls(simulation);
            return;
        }
        if (simulation is null || _ui is null || !CanConfirmDiscard())
            return;

        var command = new CastActiveSkillCommand(_ui.ControlledSlot, _discardTargets, DiscardCards: _ui.DiscardCards);
        _ui.ClearPending();
        ApplyCommand(command);
    }

    private void OnAllyTargetClicked(int slotIndex) =>
        OnTargetPicked(new CombatTargetRef(ECombatSide.Player, slotIndex));

    private bool CanConfirmDiscard() => _ui is { CanConfirmDiscard: true } &&
        _discardPlan?.TryAssign(_ui.DiscardCards, out _) == true;

    private void OnEnemyTargetClicked(int enemyIndex)
    {
        // 弹珠阶段点敌人 = 切换本轮 Count 分配标记；正在选主动技目标时仍走选目标。
        if (Simulation?.Phase == ECombatPhase.Marble && _ui?.PendingMode != ECombatPendingMode.PickActiveTarget)
        {
            ApplyCommand(new MarkMarbleTargetCommand(enemyIndex));
            return;
        }

        OnTargetPicked(new CombatTargetRef(ECombatSide.Enemy, enemyIndex));
    }

    private void OnTargetPicked(CombatTargetRef target)
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null)
            return;

        if (_ui.PendingMode == ECombatPendingMode.PickActiveTarget)
        {
            if (!PendingActiveTargetIsLegal(simulation, target))
            {
                ToastService.Show("UI_COMBAT_ILLEGAL_TARGET");
                return;
            }
            CommitActiveSkillTargets(simulation, _ui.ControlledSlot, [target]);
            return;
        }
        if (_ui.PendingMode != ECombatPendingMode.PickTarget)
            return;

        if (!TryGetPendingCard(simulation, out var card, out var handSlot))
        {
            _ui.ClearPending();
            SyncFromState();
            return;
        }

        var characterIndex = _ui.ControlledSlot;
        if (!CombatTargeting.IsLegalTarget(simulation, card, characterIndex, target))
        {
            ToastService.Show("UI_COMBAT_ILLEGAL_TARGET");
            return;
        }

        var runtimeId = _ui.PendingCard!.Value.RuntimeInstanceId;
        _ui.ClearPending();
        ApplyCommand(new PlayCardCommand(characterIndex, handSlot, [target], runtimeId));
    }

    private void OnEndTurn()
    {
        if (Simulation?.Phase == ECombatPhase.Marble)
        {
            // 提前结束：按钮可用性由 CanEndMarbleEarly 决定，这里只负责提交指令。
            // 主动结束会取消弹珠阶段里发起的待选目标（与 ESC 同语义，且此时未支付任何资源）。
            _ui?.ClearPending();
            ApplyCommand(new EndMarbleCommand());
            return;
        }

        _ui?.ClearPending();
        ApplyCommand(new EndTurnCommand());
    }

    private void OnTriggerOrbs()
    {
        _ui?.ClearPending();
        ApplyCommand(new TriggerOrbsCommand());
    }

    /// <summary>
    /// 缩略盘面 / 大屏预览切换。预览只读状态；打开时先取消进行中的拖拽，
    /// 避免拖放层与待选目标压在预览下方。
    /// </summary>
    private void SetMarblePreview(bool open)
    {
        if (_marblePreview is null || _marblePreview.Visible == open)
            return;

        if (open && (_ui?.InputLocked != false || Simulation?.Phase != ECombatPhase.Player))
            return;
        if (open)
            CancelCardDrag();
        if (_combatLayout != null)
            _combatLayout.FocusBehaviorRecursive = open ? FocusBehaviorRecursiveEnum.Disabled : FocusBehaviorRecursiveEnum.Inherited;
        _marblePreview.Visible = open;
        if (open)
            _marblePreviewPanel?.Bind(Simulation?.Marble);
        (open ? _marblePreviewPanel : _marble)?.FocusToggle();
    }

    private bool TryGetControlledCharacter(CombatSimulation simulation, out CharacterBattleInstance character)
    {
        var characters = simulation.PlayerTeam.Characters;
        var index = _ui?.ControlledSlot ?? -1;
        if (index < 0 || index >= characters.Count)
        {
            character = null!;
            return false;
        }

        character = characters[index];
        return true;
    }

    private bool TryGetPendingCard(CombatSimulation simulation, out CardDto card, out int handSlot)
    {
        card = null!;
        handSlot = -1;
        if (_ui?.PendingCard is not { } choice ||
            !CombatCommandValidator.TryGetCharacter(simulation, choice.CharacterIndex, out var character, out _))
            return false;

        var slot = character.HandSlots.FirstOrDefault(candidate => candidate.RuntimeInstanceId == choice.RuntimeInstanceId);
        if (slot?.CardId is null)
            return false;

        handSlot = slot.SlotIndex;
        return simulation.Definitions.Store.TryGetCard(slot.CardId, out card!);
    }

    #endregion

    #region 对账（SyncFromState）

    private void BindAllies(CombatSimulation simulation)
    {
        var store = simulation.Definitions.Store;
        var characters = simulation.PlayerTeam.Characters;
        for (var i = 0; i < _allies.Count; i++)
        {
            var ally = _allies[i];
            if (i >= characters.Count)
            {
                ally.Visible = false;
                continue;
            }

            ally.Visible = true;
            ally.Bind(i, characters[i], ResolveCharacter(store, characters[i].DefinitionId));
        }
    }

    /// <summary>全量按模拟器状态重绘；播放期间也会调用一次（锁输入后的禁用态）。</summary>
    private void SyncFromState()
    {
        var run = RunRuntime.Current;
        var simulation = run?.Simulation;
        if (run is null || simulation is null || _ui is null)
            return;

        var store = simulation.Definitions.Store;
        var characters = simulation.PlayerTeam.Characters;
        var inPlayerPhase = simulation.Phase == ECombatPhase.Player;
        var inMarblePhase = simulation.Phase == ECombatPhase.Marble;
        var inActionPhase = inPlayerPhase || inMarblePhase;
        SyncMarbleLayout(simulation);
        _itemSlots?.Bind(run.State.Items, store, inActionPhase && !_ui.InputLocked);
        var controlled = _ui.ControlledSlot;
        var pickingTarget = _ui.PendingMode == ECombatPendingMode.PickTarget;
        CardDto? pendingCard = pickingTarget && TryGetPendingCard(simulation, out var pc, out _) ? pc : null;

        _sharedHp?.SetValue(simulation.PlayerTeam.SharedHp, simulation.PlayerTeam.MaxHp);

        // 嘲讽标识（Run 规格 §14.2）：全队当前嘲讽值里最高的一组打 Crosshair（判定见 CombatTauntMarks）。
        var tauntMarks = CombatTauntMarks.Resolve(simulation);

        // 左栏：非当前操控的角色。
        var benchIndex = 0;
        for (var i = 0; i < characters.Count && benchIndex < _members.Count; i++)
        {
            if (i == controlled)
                continue;

            var member = _members[benchIndex];
            benchIndex++;

            // 释放按钮常驻：出牌 / 弹珠阶段、未锁输入、有权控制、未封印且有可释放档才可用。
            var canRelease = inActionPhase
                && !_ui.InputLocked
                && _ui.CanControl(i)
                && !characters[i].IsSealed
                && CanReleaseSkill(simulation, characters[i]);

            member.Visible = true;
            member.Bind(
                i,
                characters[i],
                ResolveCharacter(store, characters[i].DefinitionId),
                _ui.CanControl(i),
                !_ui.InputLocked,
                CombatActionMarks.Resolve(simulation, i));
            member.SetTauntMark(i < tauntMarks.Length && tauntMarks[i]);
            member.SetSkillRelease(canRelease);
        }

        for (; benchIndex < _members.Count; benchIndex++)
        {
            _members[benchIndex].Visible = false;
            _members[benchIndex].SetTauntMark(false);
        }

        // 友方舞台：状态文本 + 高亮 + 嘲讽标识。
        for (var i = 0; i < _allies.Count && i < characters.Count; i++)
        {
            _allies[i].Bind(i, characters[i], ResolveCharacter(store, characters[i].DefinitionId));
            var targetable = pendingCard is not null &&
                CombatTargeting.IsLegalTarget(simulation, pendingCard, controlled, new CombatTargetRef(ECombatSide.Player, i));
            targetable |= PendingActiveTargetIsLegal(simulation, new CombatTargetRef(ECombatSide.Player, i));
            _allies[i].SetHighlight(i == controlled, targetable);
            _allies[i].SetTauntMark(i < tauntMarks.Length && tauntMarks[i]);
        }

        // 敌方舞台。
        if (_enemies.Count != simulation.EnemyTeam.Enemies.Count)
            RebuildEnemies();
        IReadOnlyList<int> legalMarks = inMarblePhase ? simulation.LegalMarbleMarks() : [];
        for (var i = 0; i < _enemies.Count; i++)
        {
            _enemies[i].Refresh();
            var targetable = pendingCard is not null &&
                CombatTargeting.IsLegalTarget(simulation, pendingCard, controlled, new CombatTargetRef(ECombatSide.Enemy, i));
            targetable |= PendingActiveTargetIsLegal(simulation, new CombatTargetRef(ECombatSide.Enemy, i));
            // 弹珠阶段：只有符合嘲讽 / 免疫约束的敌人才可点击切换标记。
            if (inMarblePhase)
                targetable = legalMarks.Contains(i) || targetable;
            _enemies[i].SetTargetable(targetable && !_ui.InputLocked);
            _enemies[i].SetMarbleMarked(inMarblePhase && i == simulation.MarbleMarkedEnemyIndex);
        }

        // 右栏：充能球。
        _orbs?.SetInputLocked(_ui.InputLocked || !inPlayerPhase);
        _orbs?.Bind(
            simulation.Orbs.Queue,
            id => store.TryGetOrbType(id, out var orb) ? orb : null,
            characterIndex => ResolveCharacterName(simulation, store, characterIndex));

        // 右栏常驻缩略盘面 + 大盘预览 + 弹珠阶段主盘：盘面 / 队列只读模拟器状态。
        _marble?.Bind(simulation.Marble);
        _marblePreviewPanel?.Bind(simulation.Marble);
        _marbleMain?.Bind(simulation.Marble);

        var visibleHands = 0;
        for (var i = 0; i < characters.Count; i++)
        {
            var owner = characters[i];
            var ownerName = ResolveCharacterName(simulation, store, i);
            for (var j = 0; j < owner.HandSlots.Count; j++)
            {
                var slot = owner.HandSlots[j];
                if (slot.IsEmpty)
                    continue;
                var hand = _hands[visibleHands++];
                var card = slot.CardId is not null && store.TryGetCard(slot.CardId, out var definition) ? definition : null;
                var discarding = _ui.PendingMode == ECombatPendingMode.PickDiscard;
                var choice = new HandCardChoice(i, slot.RuntimeInstanceId ?? "");
                var pending = !slot.IsEmpty && (discarding ? _ui.DiscardCards.Contains(choice) : _ui.PendingCard == choice);
                var interactable = inPlayerPhase && _ui.CanSelectHandCard(owner.IsSealed, _discardPlan?.Owners.Contains(i) == true);
                hand.Bind(i, j, slot.RuntimeInstanceId, card, pending, interactable, ownerName);
            }
        }

        for (var i = visibleHands; i < _hands.Count; i++)
            _hands[i].Clear();

        // 底栏。
        _deck?.SetCount(simulation.PlayerTeam.CardPiles.DrawPile.Count);
        _grave?.SetCount(simulation.PlayerTeam.CardPiles.Graveyard.Count);
        if (TryGetControlledCharacter(simulation, out var actor))
        {
            var canAct = inActionPhase && !_ui.InputLocked && _ui.CanControl(controlled) && !actor.IsSealed;
            _actor?.Bind(actor, ResolveCharacter(store, actor.DefinitionId), CombatActionMarks.Resolve(simulation, controlled));
            _actor?.SetSkillRelease(canAct && CanReleaseSkill(simulation, actor));

            if (_btnPlayConfirm != null)
            {
                var discarding = _ui.PendingMode == ECombatPendingMode.PickDiscard;
                _btnPlayConfirm.Visible = discarding && !inMarblePhase;
                _btnPlayConfirm.Text = Localization.Tr("UI_COMBAT_DISCARD_CONFIRM");
                _btnPlayConfirm.Disabled = !canAct || !CanConfirmDiscard();
            }
        }

        if (_btnConfirm != null && !inMarblePhase)
        {
            _btnConfirm.Text = Localization.Tr("UI_COMBAT_END_TURN");
            _btnConfirm.Disabled = !inPlayerPhase || _ui.InputLocked || _ui.HasPending;
        }

        if (_lblHint != null && !inMarblePhase)
        {
            var hintKey = _ui.InputLocked ? "UI_COMBAT_HINT_PLAYING" : _ui.PendingMode switch
            {
                ECombatPendingMode.PickDiscard => "UI_COMBAT_HINT_PICK_DISCARD",
                ECombatPendingMode.PickTarget => "UI_COMBAT_HINT_PICK_TARGET",
                ECombatPendingMode.PickActiveTarget => "UI_COMBAT_HINT_PICK_ACTIVE_TARGET",
                _ => "",
            };
            _lblHint.Visible = hintKey.Length > 0;
            _lblHint.Text = _ui.PendingMode == ECombatPendingMode.PickDiscard && !_ui.InputLocked
                ? string.Format(Localization.Tr(hintKey), _ui.DiscardMinimum, _ui.DiscardMaximum, _ui.DiscardCards.Count)
                : hintKey.Length > 0 ? Localization.Tr(hintKey) : "";
        }

        if (inMarblePhase)
        {
            var pickingAlly = _ui.PendingMode == ECombatPendingMode.PickActiveTarget && Enumerable.Range(0, characters.Count).Any(i => PendingActiveTargetIsLegal(simulation, new CombatTargetRef(ECombatSide.Player, i)));
            if (_allyStage != null) _allyStage.Visible = pickingAlly;
            if (_marbleMain != null) _marbleMain.Visible = !pickingAlly;
            RefreshMarbleControls(simulation);
        }

        if (_btnPause != null)
            _btnPause.Disabled = false;
    }

    /// <summary>
    /// 只刷「能不能点」：锁输入时禁掉手牌 / 目标 / 确定 / 触发球并显示结算中提示，不重绑任何数据
    /// （数据由动画逐条推进，播完再 <see cref="SyncFromState"/>）。
    /// </summary>
    private void RefreshInteractivity()
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null)
            return;

        var locked = _ui.InputLocked;
        _itemSlots?.SetEnabled(!locked && simulation.Phase is ECombatPhase.Player or ECombatPhase.Marble);
        _marbleMain?.SetInputLocked(locked);
        foreach (var hand in _hands)
            hand.SetPending(false);
        foreach (var enemy in _enemies)
            enemy.SetTargetable(false);
        for (var i = 0; i < _allies.Count; i++)
            _allies[i].SetHighlight(i == _ui.ControlledSlot, targetable: false);
        foreach (var member in _members)
        {
            member.Modulate = member.Modulate with { A = locked ? 0.6f : 1f };
            if (locked)
                member.SetSkillRelease(false);
        }

        if (locked)
            _actor?.SetSkillRelease(false);
        _orbs?.SetInputLocked(locked);

        if (_btnConfirm != null)
            _btnConfirm.Disabled = locked;
        if (_btnPlayConfirm != null)
            _btnPlayConfirm.Visible = false;

        if (_lblHint != null)
        {
            _lblHint.Visible = locked;
            _lblHint.Text = locked ? Localization.Tr("UI_COMBAT_HINT_PLAYING") : "";
        }
    }

    private static CharacterDto? ResolveCharacter(GameDefinitionStore store, string definitionId) =>
        store.TryGetCharacter(definitionId, out var definition) ? definition : null;

    /// <summary>把已标记卡 id 列表翻译成卡定义（缺定义的 id 跳过——内容被剔除时不留空洞）。</summary>
    #endregion

    #region ICombatStageView

    int ICombatStageView.ControlledSlot => _ui?.ControlledSlot ?? 0;

    public AllyUnitCmp? GetAllyUnit(int slotIndex) =>
        slotIndex >= 0 && slotIndex < _allies.Count ? _allies[slotIndex] : null;

    public EnemyUnitCmp? GetEnemyUnit(int enemyIndex) =>
        enemyIndex >= 0 && enemyIndex < _enemies.Count ? _enemies[enemyIndex] : null;

    HpBarCmp? ICombatStageView.SharedHpBar => _sharedHp;

    public HandSlotCmp? GetHandSlot(int characterIndex, int slotIndex) =>
        _hands.Find(hand => hand.HasCard && hand.CharacterIndex == characterIndex && hand.SlotIndex == slotIndex);

    ActorInfoCmp? ICombatStageView.ActorInfo => _actor;

    OrbQueueCmp? ICombatStageView.OrbQueue => _orbs;

    public BuffListCmp? GetBuffList(BuffHolderRef holder)
    {
        if (holder.Target.Side == ECombatSide.Enemy)
            return GetEnemyUnit(holder.Target.Index)?.BuffList;

        if (_ui is null || holder.Target.Index != _ui.ControlledSlot)
            return null;

        return _actor?.BuffList;
    }

    Control ICombatStageView.FxLayer => _fxLayer ?? this;

    Vector2 ICombatStageView.GraveyardGlobalCenter =>
        _grave is null ? GlobalPosition + Size / 2f : _grave.GlobalPosition + _grave.Size / 2f;

    public DamageNumberCmp? SpawnDamageNumber()
    {
        if (_damageNumberScene?.Instantiate() is not DamageNumberCmp number)
            return null;

        (_fxLayer ?? this).AddChild(number);
        return number;
    }

    public async Task ShowPhaseBannerAsync(string text, float duration)
    {
        if (_phaseBanner is null)
            return;

        _phaseBanner.Text = text;
        _phaseBanner.Visible = true;
        _phaseBanner.Modulate = _phaseBanner.Modulate with { A = 0f };
        await UnitTweens.FadeAsync(_phaseBanner, 1f, duration * 0.25f);
        await UnitTweens.WaitAsync(this, duration * 0.5f);
        await UnitTweens.FadeAsync(_phaseBanner, 0f, duration * 0.25f);
        if (GodotObject.IsInstanceValid(_phaseBanner))
            _phaseBanner.Visible = false;
    }

    public void RebuildEnemies()
    {
        var simulation = Simulation;
        if (_enemyStage is null || _enemyScene is null || simulation is null)
            return;

        foreach (var enemy in _enemies)
        {
            enemy.Clicked = null;
            enemy.QueueFree();
        }

        _enemies.Clear();

        var store = simulation.Definitions.Store;
        var units = simulation.EnemyTeam.Enemies;
        for (var i = 0; i < units.Count; i++)
        {
            if (_enemyScene.Instantiate() is not EnemyUnitCmp cmp)
                continue;

            _enemyStage.AddChild(cmp);
            cmp.Bind(i, units[i], store.TryGetEnemy(units[i].DefinitionId, out var def) ? def : null);
            cmp.Clicked = OnEnemyTargetClicked;
            _enemies.Add(cmp);
        }
    }

    public void RefreshEnemy(int enemyIndex) => GetEnemyUnit(enemyIndex)?.Refresh();

    public void AddSummonedEnemy(int enemyIndex)
    {
        var simulation = Simulation;
        if (_enemyStage is null || _enemyScene is null || simulation is null)
            return;
        for (var index = _enemies.Count; index <= enemyIndex && index < simulation.EnemyTeam.Enemies.Count; index++)
        {
            if (_enemyScene.Instantiate() is not EnemyUnitCmp cmp)
                return;
            var unit = simulation.EnemyTeam.Enemies[index];
            _enemyStage.AddChild(cmp);
            cmp.Bind(index, unit, simulation.Definitions.Store.TryGetEnemy(unit.DefinitionId, out var def) ? def : null);
            cmp.Clicked = OnEnemyTargetClicked;
            _enemies.Add(cmp);
        }
    }

    public void RefreshOrbs()
    {
        var simulation = Simulation;
        if (simulation is null || _orbs is null)
            return;

        var store = simulation.Definitions.Store;
        _orbs.Bind(
            simulation.Orbs.Queue,
            id => store.TryGetOrbType(id, out var orb) ? orb : null,
            characterIndex => ResolveCharacterName(simulation, store, characterIndex));
    }

    /// <summary>按事件载荷单独上色一格（<see cref="OrbGainedEvent"/> 驱动）：不读队列终态。</summary>
    public void PaintOrb(int index, string orbTypeId, int queueCount, int producerIndex)
    {
        var simulation = Simulation;
        var store = simulation?.Definitions.Store;
        _orbs?.PaintOrb(
            index,
            orbTypeId,
            producerIndex,
            queueCount,
            id => store is not null && store.TryGetOrbType(id, out var orb) ? orb : null,
            producer => simulation is null || store is null
                ? ""
                : ResolveCharacterName(simulation, store, producer));
    }

    /// <summary>
    /// 角色显示名（槽位 → 显示名，缺显示名回落定义 id）：球位悬停的产球者与主动技确认弹窗共用。
    /// 无效槽位（&lt; 0 / 越界）返回空串，由调用方决定回落文案。
    /// </summary>
    private static string ResolveCharacterName(CombatSimulation simulation, GameDefinitionStore store, int characterIndex)
    {
        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return "";

        var character = characters[characterIndex];
        var definition = ResolveCharacter(store, character.DefinitionId);
        return definition is null || string.IsNullOrWhiteSpace(definition.DisplayNameId)
            ? character.DefinitionId
            : Localization.Tr(definition.DisplayNameId);
    }

    /// <summary>
    /// 「释放主动技」按钮（队友卡 / 操控角色面板）：二次确认后发 <see cref="CastActiveSkillCommand"/>。
    /// 确认文案带上角色名、将释放的档位技能名与技能效果（效果先剥掉关键词标记再进 Label）；
    /// 无主动链 / 无可释放档直接忽略。
    /// </summary>
    private static bool CanReleaseSkill(CombatSimulation simulation, CharacterBattleInstance character)
    {
        if (!CombatActiveSkillTips.CanRelease(character))
            return false;
        var skillId = character.ActiveSkillChain[character.ResolveCastableTier()].SkillId;
        return simulation.Definitions.Store.TryGetSkill(skillId, out var skill) &&
            (simulation.Phase == ECombatPhase.Player || simulation.Phase == ECombatPhase.Marble && !skill.CardPhaseOnly);
    }

    private void OnReleaseSkillRequested(int characterIndex)
    {
        var simulation = Simulation;
        if (simulation is null)
            return;

        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return;

        var character = characters[characterIndex];
        if (!CanReleaseSkill(simulation, character))
            return;

        var store = simulation.Definitions.Store;
        var tierIndex = character.ResolveCastableTier();
        var skillId = character.ActiveSkillChain[tierIndex].SkillId;
        var skillName = skillId;
        var skillEffect = "";
        if (store.TryGetSkill(skillId, out var skill))
        {
            if (!string.IsNullOrWhiteSpace(skill.DisplayNameId))
            {
                skillName = Localization.Tr(skill.DisplayNameId);
            }

            // 确认框是 Label（非 RichTextLabel）：关键词标记先剥掉，否则会露出 [url=kw:...] 原文。
            if (!string.IsNullOrWhiteSpace(skill.DescId))
            {
                skillEffect = CardSummaryBuilder.StripRichText(Localization.Tr(skill.DescId));
            }
        }

        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = "UI_COMBAT_ACTIVE_SKILL_CONFIRM_TITLE",
            DescKey = "UI_COMBAT_ACTIVE_SKILL_CONFIRM_DESC",
            // {2} = 技能效果（前置空行；效果为空时不留空行）。
            DescArgs =
            [
                ResolveCharacterName(simulation, store, characterIndex),
                skillName,
                string.IsNullOrWhiteSpace(skillEffect) ? "" : $"\n\n{skillEffect}",
            ],
            OkTextKey = "UI_ALERT_OK",
            CancelTextKey = "UI_ALERT_CANCEL",
            Time = 0,
            OkCallback = () => CastActiveSkill(characterIndex),
        });
    }

    /// <summary>
    /// 确认后按技能目标规格进入选牌/选目标，或直接释放可自动解析的技能。
    /// </summary>
    private void CastActiveSkill(int characterIndex)
    {
        // 确认框生命周期可能长于本界面（战斗结束 / 窗口销毁）：先校验节点有效性再走指令。
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;

        var simulation = Simulation;
        if (simulation is null || _ui is null || _ui.InputLocked || !_ui.CanControl(characterIndex))
            return;
        var character = simulation.PlayerTeam.Characters[characterIndex];
        var tier = character.ResolveCastableTier();
        if (tier < 0 || !simulation.Definitions.Store.TryGetSkill(character.ActiveSkillChain[tier].SkillId, out var skill))
            return;
        if (skill.TargetOverride is { Scope: ETargetScope.Single, Side: not ETargetSide.Self })
        {
            _ui.TrySelectSlot(characterIndex);
            _ui.BeginActiveTarget();
            SyncFromState();
            return;
        }
        CommitActiveSkillTargets(simulation, characterIndex, []);
    }

    private void CommitActiveSkillTargets(CombatSimulation simulation, int characterIndex, IReadOnlyList<CombatTargetRef> targets)
    {
        if (_ui is null)
            return;

        var character = simulation.PlayerTeam.Characters[characterIndex];
        var tier = character.ResolveCastableTier();
        if (tier < 0 || !simulation.Definitions.Store.TryGetSkill(character.ActiveSkillChain[tier].SkillId, out var skill))
            return;

        var resolvedTargets = CombatTargeting.ResolveActiveSkillTargets(simulation, skill, characterIndex, targets, out _);
        if (resolvedTargets is null)
        {
            ToastService.Show("UI_COMBAT_ILLEGAL_TARGET");
            return;
        }

        var plan = DiscardSelection.BuildPlan(simulation, skill, characterIndex, resolvedTargets);
        if (plan is { Limits.Maximum: > 0 })
        {
            _ui.TrySelectSlot(characterIndex);
            _discardPlan = plan;
            _discardTargets = resolvedTargets;
            _ui.BeginDiscard(plan.Limits.Minimum, plan.Limits.Maximum);
            SyncFromState();
            return;
        }

        _ui.ClearPending();
        ApplyCommand(new CastActiveSkillCommand(characterIndex, resolvedTargets));
    }

    private bool PendingActiveTargetIsLegal(CombatSimulation simulation, CombatTargetRef target)
    {
        if (_ui is null || _ui.PendingMode != ECombatPendingMode.PickActiveTarget)
            return false;
        var character = simulation.PlayerTeam.Characters[_ui.ControlledSlot];
        var tier = character.ResolveCastableTier();
        return tier >= 0 && simulation.Definitions.Store.TryGetSkill(character.ActiveSkillChain[tier].SkillId, out var skill) &&
            skill.TargetOverride is { } spec && CombatTargeting.CollectLegalTargetsForScope(simulation,
                spec.Side, spec.Scope, _ui.ControlledSlot).Contains(target);
    }

    public void RefreshBuffs(BuffHolderRef holder)
    {
        var simulation = Simulation;
        var list = GetBuffList(holder);
        if (simulation is null || list is null)
            return;

        if (holder.Target.Side == ECombatSide.Enemy)
        {
            var enemies = simulation.EnemyTeam.Enemies;
            if (holder.Target.Index >= 0 && holder.Target.Index < enemies.Count)
                list.Bind(enemies[holder.Target.Index].Buffs.Visible);
            return;
        }

        var characters = simulation.PlayerTeam.Characters;
        if (holder.Target.Index < 0 || holder.Target.Index >= characters.Count)
            return;

        var character = characters[holder.Target.Index];
        list.Bind(character.Buffs.Visible);
    }

    public void ShowDrawnCard(int characterIndex, DrawnCard drawn)
    {
        var simulation = Simulation;
        var hand = GetHandSlot(characterIndex, drawn.SlotIndex) ?? _hands.Find(candidate => !candidate.HasCard);
        if (simulation is null || hand is null)
            return;

        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return;

        simulation.Definitions.Store.TryGetCard(drawn.CardId, out var card);
        hand.Bind(characterIndex, drawn.SlotIndex, drawn.RuntimeInstanceId, card, pending: false, interactable: false,
            ResolveCharacterName(simulation, simulation.Definitions.Store, characterIndex));
    }

    #endregion
}