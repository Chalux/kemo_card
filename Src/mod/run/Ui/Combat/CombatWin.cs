using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
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
    [Export] private Control? _itemSlots;
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
    [Export] private CardPileCmp? _grave;
    [Export] private Button? _btnPlayConfirm;
    [Export] private Button? _btnConfirm;
    [Export] private Label? _lblHint;
    [Export] private Control? _fxLayer;
    [Export] private Control? _stripLayer;
    [Export] private PackedScene? _damageNumberScene;
    [Export] private Label? _phaseBanner;
    [Export] private CombatAnimator? _animator;

    private readonly CombatPresentationDirector _director = new();
    private readonly List<PartyMemberCmp> _members = [];
    private readonly List<AllyUnitCmp> _allies = [];
    private readonly List<HandSlotCmp> _hands = [];
    private readonly List<EnemyUnitCmp> _enemies = [];
    private readonly List<MarkedCardStripCmp> _strips = [];

    private CombatUiState? _ui;
    private bool _endHandled;

    public override string UIId => RunUiIds.Combat;
    public override string UIDir => "Src/mod/run/Ui/Combat";

    private static CombatSimulation? Simulation => RunRuntime.Current?.Simulation;

    #region 生命周期

    protected override void OnReady()
    {
        CollectChildren(_bench, _members);
        CollectChildren(_allyStage, _allies);
        CollectChildren(_hand, _hands);
        CollectChildren(_stripLayer, _strips);
        foreach (var member in _members)
            member.ReleaseRequested = OnReleaseSkillRequested;
        if (_actor != null)
            _actor.ReleaseRequested = () => OnReleaseSkillRequested(_ui?.ControlledSlot ?? -1);
        if (_phaseBanner != null)
            _phaseBanner.Visible = false;
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
            OnClicks(_btnConfirm, OnConfirmToggle);

        // 组件回调是属性赋值：进树登记、离场清空（与 Binder 的信号解绑同步）。
        if (_orbs != null)
            _orbs.TriggerRequested = OnTriggerOrbs;
        foreach (var member in _members)
            member.Clicked = OnMemberClicked;
        foreach (var ally in _allies)
            ally.Clicked = OnAllyTargetClicked;
        foreach (var hand in _hands)
            hand.Clicked = OnHandClicked;

        var run = RunRuntime.Current;
        if (run is not null)
        {
            var listener = run.State.OnRunPhaseChanged((_, _) => OnRunPhaseChanged(), this);
            Binder.Add(listener.Off);
        }
    }

    protected override void OnExitTree()
    {
        if (_orbs != null)
            _orbs.TriggerRequested = null;
        foreach (var member in _members)
            member.Clicked = null;
        foreach (var ally in _allies)
            ally.Clicked = null;
        foreach (var hand in _hands)
            hand.Clicked = null;
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
        _ui = CombatUiState.FromRun(run.State);
        _animator?.Bind(this);

        // 开战管线（BattleStart）在界面打开前已跑完：这些事件不再回放，直接对账到当前状态。
        simulation.Presentation.Clear();
        BindAllies(simulation);
        RebuildEnemies();
        SyncFromState();
    }

    protected override void UpdateView() => SyncFromState();

    protected override void OnClose() => _director.Clear();

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

    /// <summary>ESC：有待出牌先取消；否则开 / 关系统菜单。两者都吃掉事件，不穿透到下层 RunMainWin。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (RunRuntime.Current is null || !@event.IsActionPressed("ui_cancel"))
            return;

        GetViewport().SetInputAsHandled();
        if (_ui is { HasPending: true })
        {
            _ui.ClearPending();
            SyncFromState();
            return;
        }

        _ = RunUiController.TogglePauseMenuAsync();
    }

    #endregion

    #region 指令与播放

    private void ApplyCommand(ICombatCommand command)
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null || _ui.InputLocked)
            return;

        var result = simulation.TryApply(command);
        if (!result.Success)
        {
            ToastService.Show("UI_COMBAT_COMMAND_FAILED", result.Error);
        }
        else
        {
            // 四人确认齐后状态机只切到 CardExecution，执行 / 敌方相位要由宿主推进（同步跑完，事件事后播放）。
            simulation.AdvanceAutomaticPhases();
        }

        _ = PlayPendingAsync();
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
        if (_ui != null)
            _ui.InputLocked = locked;
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

    private void OnHandClicked(int handSlotIndex)
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null || _ui.InputLocked || simulation.Phase != ECombatPhase.Player)
            return;

        var characterIndex = _ui.ControlledSlot;
        if (!TryGetControlledCharacter(simulation, out var character))
            return;
        if (handSlotIndex < 0 || handSlotIndex >= character.HandSlots.Count)
            return;

        var slot = character.HandSlots[handSlotIndex];
        if (slot.IsEmpty || slot.CardId is null)
            return;

        if (_ui.PendingMode == ECombatPendingMode.PickDiscard)
        {
            _ui.ToggleDiscard(handSlotIndex);
            SyncFromState();
            return;
        }

        if (character.HasActed)
        {
            ToastService.Show("UI_COMBAT_ALREADY_CONFIRMED");
            return;
        }

        if (slot.IsMarked)
        {
            _ui.ClearPending();
            ApplyCommand(new CancelQueuedCardCommand(characterIndex, CardRuntimeInstanceId: slot.RuntimeInstanceId));
            return;
        }

        if (!simulation.Definitions.Store.TryGetCard(slot.CardId, out var card))
            return;

        var mode = CombatTargeting.RequiresExplicitTarget(card)
            ? ECombatPendingMode.PickTarget
            : ECombatPendingMode.ConfirmPlay;
        _ui.TogglePending(handSlotIndex, mode);
        SyncFromState();
    }

    private void OnPlayConfirm()
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null)
            return;
        if (_ui.PendingMode == ECombatPendingMode.PickDiscard)
        {
            if (!_ui.CanConfirmDiscard)
                return;
            var command = new CastActiveSkillCommand(_ui.ControlledSlot, [], _ui.DiscardSlots);
            _ui.ClearPending();
            ApplyCommand(command);
            return;
        }
        if (_ui.PendingMode != ECombatPendingMode.ConfirmPlay)
            return;

        if (!TryGetPendingCard(simulation, out var card))
        {
            _ui.ClearPending();
            SyncFromState();
            return;
        }

        var characterIndex = _ui.ControlledSlot;
        var handSlot = _ui.PendingHandSlot;
        if (CombatTargeting.RequiresExplicitTarget(card))
            return;

        _ui.ClearPending();
        ApplyCommand(new PlayCardCommand(characterIndex, handSlot, []));
    }

    private void OnAllyTargetClicked(int slotIndex) =>
        OnTargetPicked(new CombatTargetRef(ECombatSide.Player, slotIndex));

    private void OnEnemyTargetClicked(int enemyIndex) =>
        OnTargetPicked(new CombatTargetRef(ECombatSide.Enemy, enemyIndex));

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
            var caster = _ui.ControlledSlot;
            _ui.ClearPending();
            ApplyCommand(new CastActiveSkillCommand(caster, [target]));
            return;
        }
        if (_ui.PendingMode != ECombatPendingMode.PickTarget)
            return;

        if (!TryGetPendingCard(simulation, out var card))
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

        var handSlot = _ui.PendingHandSlot;
        _ui.ClearPending();
        ApplyCommand(new PlayCardCommand(characterIndex, handSlot, [target]));
    }

    private void OnConfirmToggle()
    {
        var simulation = Simulation;
        if (simulation is null || _ui is null || !TryGetControlledCharacter(simulation, out var character))
            return;

        _ui.ClearPending();
        ApplyCommand(character.HasActed
            ? new UnconfirmCharacterCommand(_ui.ControlledSlot)
            : new ConfirmCharacterCommand(_ui.ControlledSlot));
    }

    private void OnTriggerOrbs()
    {
        _ui?.ClearPending();
        ApplyCommand(new TriggerOrbsCommand());
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

    private bool TryGetPendingCard(CombatSimulation simulation, out CardDto card)
    {
        card = null!;
        if (_ui is null || !_ui.HasPending || !TryGetControlledCharacter(simulation, out var character))
            return false;

        var handSlot = _ui.PendingHandSlot;
        if (handSlot < 0 || handSlot >= character.HandSlots.Count)
            return false;

        var slot = character.HandSlots[handSlot];
        if (slot.IsEmpty || slot.CardId is null || slot.IsMarked)
            return false;

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
        var controlled = _ui.ControlledSlot;
        var pickingTarget = _ui.PendingMode == ECombatPendingMode.PickTarget;
        CardDto? pendingCard = pickingTarget && TryGetPendingCard(simulation, out var pc) ? pc : null;

        _sharedHp?.SetValue(simulation.PlayerTeam.SharedHp, simulation.PlayerTeam.MaxHp);

        // 嘲讽标识（Run 规格 §14.2）：全队当前嘲讽值里最高的一组打 Crosshair（判定见 CombatTauntMarks）。
        var tauntMarks = CombatTauntMarks.Resolve(simulation);

        // 已标记卡牌条（Run 规格 §14.2）：按角色给出本回合已标记的卡（结算顺序）。
        // 条挂在 FitScale 的 StripLayer（晚于 Root → 拾取优先于战场单位）并跟随队友卡定位；
        // 当前操控角色正在选目标时整条隐藏——选目标态下战场是唯一交互焦点，列表会挡住点击。
        var markedCards = CombatMarkedCards.Resolve(simulation);
        var hideMarkedCards = pickingTarget || _ui.PendingMode == ECombatPendingMode.PickActiveTarget;

        // 左栏：非当前操控的角色。
        var benchIndex = 0;
        for (var i = 0; i < characters.Count && benchIndex < _members.Count; i++)
        {
            if (i == controlled)
                continue;

            var member = _members[benchIndex];
            var strip = benchIndex < _strips.Count ? _strips[benchIndex] : null;
            benchIndex++;

            // 释放按钮（2026-09-27）：玩家阶段、未锁输入、有权控制、未封印且有可释放档才显示。
            var canRelease = inPlayerPhase
                && !_ui.InputLocked
                && _ui.CanControl(i)
                && !characters[i].IsSealed
                && CombatActiveSkillTips.CanRelease(characters[i]);

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
            if (strip != null)
            {
                strip.Follow(member);
                strip.Bind(
                    hideMarkedCards ? [] : ResolveCards(store, i < markedCards.Count ? markedCards[i] : []));
            }
        }

        var boundMembers = benchIndex;
        for (; benchIndex < _members.Count; benchIndex++)
        {
            _members[benchIndex].Visible = false;
            _members[benchIndex].SetTauntMark(false);
        }

        for (var i = boundMembers; i < _strips.Count; i++)
        {
            _strips[i].Follow(null);
            _strips[i].Visible = false;
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
        for (var i = 0; i < _enemies.Count; i++)
        {
            _enemies[i].Refresh();
            var targetable = pendingCard is not null &&
                CombatTargeting.IsLegalTarget(simulation, pendingCard, controlled, new CombatTargetRef(ECombatSide.Enemy, i));
            targetable |= PendingActiveTargetIsLegal(simulation, new CombatTargetRef(ECombatSide.Enemy, i));
            _enemies[i].SetTargetable(targetable && !_ui.InputLocked);
        }

        // 右栏：充能球。
        _orbs?.SetInputLocked(_ui.InputLocked || !inPlayerPhase);
        _orbs?.Bind(
            simulation.Orbs.Queue,
            id => store.TryGetOrbType(id, out var orb) ? orb : null,
            characterIndex => ResolveCharacterName(simulation, store, characterIndex));

        // 底栏。
        if (TryGetControlledCharacter(simulation, out var actor))
        {
            var canAct = inPlayerPhase && !_ui.InputLocked && _ui.CanControl(controlled) && !actor.IsSealed;
            _actor?.Bind(actor, ResolveCharacter(store, actor.DefinitionId), CombatActionMarks.Resolve(simulation, controlled));
            _actor?.SetSkillRelease(canAct && CombatActiveSkillTips.CanRelease(actor));
            _deck?.SetCount(actor.DrawPile.Count);
            _grave?.SetCount(actor.Graveyard.Count);

            for (var i = 0; i < _hands.Count && i < actor.HandSlots.Count; i++)
            {
                var slot = actor.HandSlots[i];
                var card = slot.CardId is not null && store.TryGetCard(slot.CardId, out var def) ? def : null;
                _hands[i].Bind(i, slot, card, (_ui.PendingMode == ECombatPendingMode.PickDiscard ? _ui.DiscardSlots.Contains(i) : _ui.HasPending && _ui.PendingHandSlot == i), canAct);
            }

            if (_btnConfirm != null)
            {
                _btnConfirm.Text = Localization.Tr(actor.HasActed ? "UI_COMBAT_UNCONFIRM" : "UI_COMBAT_CONFIRM");
                _btnConfirm.Disabled = !canAct || _ui.PendingMode is ECombatPendingMode.PickDiscard or ECombatPendingMode.PickActiveTarget;
            }

            if (_btnPlayConfirm != null)
            {
                var discarding = _ui.PendingMode == ECombatPendingMode.PickDiscard;
                _btnPlayConfirm.Visible = discarding || _ui.PendingMode == ECombatPendingMode.ConfirmPlay;
                _btnPlayConfirm.Text = Localization.Tr(discarding ? "UI_COMBAT_DISCARD_CONFIRM" : "UI_COMBAT_PLAY_CONFIRM");
                _btnPlayConfirm.Disabled = !canAct || discarding && !_ui.CanConfirmDiscard;
            }
        }

        if (_lblHint != null)
        {
            var hintKey = _ui.InputLocked
                ? "UI_COMBAT_HINT_PLAYING"
                : _ui.PendingMode switch
                {
                    ECombatPendingMode.PickDiscard => "UI_COMBAT_HINT_PICK_DISCARD",
                    ECombatPendingMode.PickTarget => "UI_COMBAT_HINT_PICK_TARGET",
                    ECombatPendingMode.PickActiveTarget => "UI_COMBAT_HINT_PICK_ACTIVE_TARGET",
                    ECombatPendingMode.ConfirmPlay => "UI_COMBAT_HINT_CONFIRM_PLAY",
                    _ => "",
                };
            _lblHint.Visible = hintKey.Length > 0;
            _lblHint.Text = _ui.PendingMode == ECombatPendingMode.PickDiscard && !_ui.InputLocked
                ? string.Format(Localization.Tr(hintKey), _ui.DiscardMinimum, _ui.DiscardMaximum, _ui.DiscardSlots.Count)
                : hintKey.Length > 0 ? Localization.Tr(hintKey) : "";
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
    private static IReadOnlyList<CardDto> ResolveCards(GameDefinitionStore store, IReadOnlyList<string> cardIds)
    {
        var cards = new List<CardDto>(cardIds.Count);
        foreach (var cardId in cardIds)
        {
            if (store.TryGetCard(cardId, out var card))
            {
                cards.Add(card);
            }
        }

        return cards;
    }

    #endregion

    #region ICombatStageView

    int ICombatStageView.ControlledSlot => _ui?.ControlledSlot ?? 0;

    public AllyUnitCmp? GetAllyUnit(int slotIndex) =>
        slotIndex >= 0 && slotIndex < _allies.Count ? _allies[slotIndex] : null;

    public EnemyUnitCmp? GetEnemyUnit(int enemyIndex) =>
        enemyIndex >= 0 && enemyIndex < _enemies.Count ? _enemies[enemyIndex] : null;

    HpBarCmp? ICombatStageView.SharedHpBar => _sharedHp;

    public HandSlotCmp? GetHandSlot(int characterIndex, int slotIndex)
    {
        if (_ui is null || characterIndex != _ui.ControlledSlot)
            return null;

        return slotIndex >= 0 && slotIndex < _hands.Count ? _hands[slotIndex] : null;
    }

    ActorInfoCmp? ICombatStageView.ActorInfo => _actor;

    OrbQueueCmp? ICombatStageView.OrbQueue => _orbs;

    public BuffListCmp? GetBuffList(BuffHolderRef holder)
    {
        if (holder.Target.Side == ECombatSide.Enemy)
            return GetEnemyUnit(holder.Target.Index)?.BuffList;

        if (_ui is null || holder.Target.Index != _ui.ControlledSlot)
            return null;

        return holder.HandSlotIndex is { } slot ? GetHandSlot(holder.Target.Index, slot)?.SlotBuffs : _actor?.BuffList;
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
    private void OnReleaseSkillRequested(int characterIndex)
    {
        var simulation = Simulation;
        if (simulation is null)
            return;

        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return;

        var character = characters[characterIndex];
        if (!CombatActiveSkillTips.CanRelease(character))
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
        var selection = DiscardSelection.Resolve(simulation, skill, characterIndex);
        if (selection is { Maximum: > 0 })
        {
            _ui.TrySelectSlot(characterIndex);
            _ui.BeginDiscard(selection.Minimum, selection.Maximum);
            SyncFromState();
            return;
        }
        ApplyCommand(new CastActiveSkillCommand(characterIndex, [], []));
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
        if (holder.HandSlotIndex is { } slot && slot >= 0 && slot < character.HandSlots.Count)
            list.Bind(character.HandSlots[slot].Buffs.Visible);
        else
            list.Bind(character.Buffs.Visible);
    }

    public void RefreshHandSlot(int characterIndex, int slotIndex)
    {
        var simulation = Simulation;
        var hand = GetHandSlot(characterIndex, slotIndex);
        if (simulation is null || hand is null || _ui is null)
            return;

        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return;

        var character = characters[characterIndex];
        if (slotIndex < 0 || slotIndex >= character.HandSlots.Count)
            return;

        var slot = character.HandSlots[slotIndex];
        var card = slot.CardId is not null && simulation.Definitions.Store.TryGetCard(slot.CardId, out var def) ? def : null;
        hand.Bind(slotIndex, slot, card, pending: false, interactable: false);
    }

    #endregion
}