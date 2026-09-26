using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Run.Debug;

namespace KemoCard.Mod.Run.Ui;

/// <summary>
/// Run 调试面板：取指定卡牌 / 角色、打指定战斗、触发指定事件等开发期操作。
/// </summary>
/// <remarks>
/// <para>本类只做「取值 + 显示 + 收集参数」，全部语义在 <see cref="RunDebugService"/>（可单测，
/// 不依赖 Godot）。所有订阅经 <see cref="BaseUI.Binder"/> 登记，离场由框架统一解绑。</para>
/// <para>注册在 <c>EUILayer.Debug</c> 顶层，因此不会被 Run 界面遮挡；<c>CacheTime = 0</c>
/// 保证每次打开都重新读取当前 Run 状态。</para>
/// </remarks>
public partial class RunDebugDlg : BaseDlg
{
    private static readonly Color OwnedColor = new(0.55f, 0.85f, 0.55f);
    private static readonly Color ErrorColor = new(1f, 0.45f, 0.45f, 1f);
    private static readonly Color OkColor = new(0.75f, 0.85f, 0.95f);

    [Export] private TabContainer? _tabs;
    [Export] private Button? _btnClose;
    [Export] private OptionButton? _slotOption;

    [Export] private LineEdit? _cardFilter;
    [Export] private ItemList? _cardList;
    [Export] private Button? _btnGrantCard;
    [Export] private Button? _btnCardToDeck;
    [Export] private Button? _btnRevokeCard;

    [Export] private LineEdit? _charFilter;
    [Export] private ItemList? _charList;
    [Export] private Button? _btnGrantCharacter;
    [Export] private Button? _btnDeployCharacter;
    [Export] private Button? _btnFillParty;

    [Export] private ItemList? _battleList;
    [Export] private LineEdit? _battleSeedInput;
    [Export] private CheckBox? _battleRandomSeedCheck;
    [Export] private Button? _btnStartBattle;
    [Export] private Button? _btnEndWin;
    [Export] private Button? _btnEndLose;

    [Export] private ItemList? _eventList;
    [Export] private Button? _btnTriggerEvent;

    [Export] private LineEdit? _goldInput;
    [Export] private Button? _btnAddGold;
    [Export] private LineEdit? _ringInput;
    [Export] private Button? _btnSetRing;
    [Export] private OptionButton? _phaseOption;
    [Export] private Button? _btnSetPhase;

    [Export] private LineEdit? _potentialInput;
    [Export] private Button? _btnPotentialPool;
    [Export] private Button? _btnPotentialSlot;
    [Export] private Button? _btnAllocatePotential;
    [Export] private Button? _btnDeductPotential;
    [Export] private Button? _btnInspectBattle;

    [Export] private OptionButton? _orbOption;
    [Export] private LineEdit? _orbCountInput;
    [Export] private Button? _btnGrantOrb;
    [Export] private Button? _btnTriggerOrbs;

    [Export] private Label? _statusLabel;
    [Export] private RichTextLabel? _log;

    private readonly List<RunDebugOption> _cardView = [];
    private readonly List<RunDebugOption> _charView = [];
    private readonly List<RunDebugOption> _battleView = [];
    private readonly List<RunDebugOption> _eventView = [];
    private readonly List<RunDebugOption> _orbView = [];

    private RunDebugService? _service;
    private int _selectedCard = -1;
    private int _selectedCharacter = -1;
    private int _selectedBattle = -1;
    private int _selectedEvent = -1;
    private bool _rebuilding;

    public override string UIId => RunUiIds.RunDebug;
    public override string UIDir => "Src/mod/run/Ui";

    /// <summary>当前选中的目标槽位（0-based）。</summary>
    private int Slot => _slotOption?.Selected ?? 0;

    protected override void InitEvent()
    {
        if (_btnClose != null)
        {
            OnClicks(_btnClose, Close);
        }

        if (_cardList != null)
        {
            Binder.OnItemSelected(_cardList, OnCardSelected);
        }

        if (_cardFilter != null)
        {
            Binder.OnTextChanged(_cardFilter, OnCardFilterChanged);
        }

        if (_btnGrantCard != null)
        {
            OnClicks(_btnGrantCard, OnGrantCard);
        }

        if (_btnCardToDeck != null)
        {
            OnClicks(_btnCardToDeck, OnCardToDeck);
        }

        if (_btnRevokeCard != null)
        {
            OnClicks(_btnRevokeCard, OnRevokeCard);
        }

        if (_charList != null)
        {
            Binder.OnItemSelected(_charList, OnCharacterSelected);
        }

        if (_charFilter != null)
        {
            Binder.OnTextChanged(_charFilter, OnCharFilterChanged);
        }

        if (_btnGrantCharacter != null)
        {
            OnClicks(_btnGrantCharacter, OnGrantCharacter);
        }

        if (_btnDeployCharacter != null)
        {
            OnClicks(_btnDeployCharacter, OnDeployCharacter);
        }

        if (_btnFillParty != null)
        {
            OnClicks(_btnFillParty, OnFillParty);
        }

        if (_battleList != null)
        {
            Binder.OnItemSelected(_battleList, OnBattleSelected);
        }

        if (_btnStartBattle != null)
        {
            OnClicks(_btnStartBattle, OnStartBattle);
        }

        if (_btnEndWin != null)
        {
            OnClicks(_btnEndWin, OnEndBattleWin);
        }

        if (_btnEndLose != null)
        {
            OnClicks(_btnEndLose, OnEndBattleLose);
        }

        if (_eventList != null)
        {
            Binder.OnItemSelected(_eventList, OnEventSelected);
        }

        if (_btnTriggerEvent != null)
        {
            OnClicks(_btnTriggerEvent, OnTriggerEvent);
        }

        if (_btnAddGold != null)
        {
            OnClicks(_btnAddGold, OnAddGold);
        }

        if (_btnSetRing != null)
        {
            OnClicks(_btnSetRing, OnSetRing);
        }

        if (_btnSetPhase != null)
        {
            OnClicks(_btnSetPhase, OnSetPhase);
        }

        if (_btnPotentialPool != null)
        {
            OnClicks(_btnPotentialPool, OnGrantPotentialPool);
        }

        if (_btnPotentialSlot != null)
        {
            OnClicks(_btnPotentialSlot, OnGrantPotentialSlot);
        }

        if (_btnAllocatePotential != null)
        {
            OnClicks(_btnAllocatePotential, OnAllocatePotential);
        }

        if (_btnDeductPotential != null)
        {
            OnClicks(_btnDeductPotential, OnDeductPotential);
        }

        if (_btnInspectBattle != null)
        {
            OnClicks(_btnInspectBattle, OnInspectBattle);
        }

        if (_btnGrantOrb != null)
        {
            OnClicks(_btnGrantOrb, OnGrantOrb);
        }

        if (_btnTriggerOrbs != null)
        {
            OnClicks(_btnTriggerOrbs, OnTriggerOrbs);
        }
    }

    protected override void OnOpen()
    {
        _service = RunRuntime.CreateDebugService();
        if (_service is null)
        {
            SetActionsEnabled(false);
            SetStatus(false, Localization.Tr("UI_DEBUG_NO_RUN"));
            return;
        }

        ApplyTabTitles();
        FillSlotOptions();
        FillPhaseOptions();
        FillOrbOptions();
        _phaseOption?.Select((int)_service.State.Phase);
        ClearLog();
        WriteLog(true, $"Run {_service.State.RunId} / story {_service.State.StoryId}");

        RebuildAll();
        RefreshStatus();
    }

    protected override void UpdateView() => RefreshStatus();

    #region 列表构建

    /// <summary>
    /// 重建全部列表。调试操作会改变「已拥有 / 已入池」标记，重建后按 <b>内容 id</b> 复原选中项，
    /// 避免连续加牌时每次都要重新点一遍。
    /// </summary>
    private void RebuildAll()
    {
        var cardId = IdAt(_cardView, _selectedCard);
        var characterId = IdAt(_charView, _selectedCharacter);
        var battleId = IdAt(_battleView, _selectedBattle);
        var eventId = IdAt(_eventView, _selectedEvent);

        RebuildCardList(cardId);
        RebuildCharacterList(characterId);
        RebuildBattleList(battleId);
        RebuildEventList(eventId);
    }

    private static string? IdAt(IReadOnlyList<RunDebugOption> view, int index) =>
        index >= 0 && index < view.Count ? view[index].Id : null;

    private static int IndexOfId(IReadOnlyList<RunDebugOption> view, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return -1;
        }

        for (var i = 0; i < view.Count; i++)
        {
            if (string.Equals(view[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void RebuildCardList(string? keepId)
    {
        if (_cardList is null || _service is null)
        {
            return;
        }

        var filter = _cardFilter?.Text?.Trim() ?? "";
        _cardView.Clear();
        _rebuilding = true;
        _cardList.Clear();

        foreach (var option in _service.ListCards())
        {
            if (!Matches(option, filter))
            {
                continue;
            }

            var owned = _service.State.CanUseCard(option.Id);
            var label = owned ? $"{Localization.Tr("UI_DEBUG_OWNED")} · {option.Label}" : option.Label;
            _cardList.AddItem(label);
            if (owned)
            {
                _cardList.SetItemCustomFgColor(_cardList.ItemCount - 1, OwnedColor);
            }

            _cardView.Add(option);
        }

        _selectedCard = IndexOfId(_cardView, keepId);
        if (_selectedCard >= 0)
        {
            _cardList.Select(_selectedCard);
        }

        _rebuilding = false;
    }

    private void RebuildCharacterList(string? keepId)
    {
        if (_charList is null || _service is null)
        {
            return;
        }

        var filter = _charFilter?.Text?.Trim() ?? "";
        var inPool = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in _service.State.CharacterPool)
        {
            inPool.Add(character.DefinitionId);
        }

        _charView.Clear();
        _rebuilding = true;
        _charList.Clear();

        foreach (var option in _service.ListCharacters())
        {
            if (!Matches(option, filter))
            {
                continue;
            }

            var owned = inPool.Contains(option.Id);
            var label = owned ? $"{Localization.Tr("UI_DEBUG_IN_POOL")} · {option.Label}" : option.Label;
            _charList.AddItem(label);
            if (owned)
            {
                _charList.SetItemCustomFgColor(_charList.ItemCount - 1, OwnedColor);
            }

            _charView.Add(option);
        }

        _selectedCharacter = IndexOfId(_charView, keepId);
        if (_selectedCharacter >= 0)
        {
            _charList.Select(_selectedCharacter);
        }

        _rebuilding = false;
    }

    private void RebuildBattleList(string? keepId)
    {
        if (_battleList is null || _service is null)
        {
            return;
        }

        _battleView.Clear();
        _rebuilding = true;
        _battleList.Clear();
        foreach (var option in _service.ListBattles())
        {
            _battleList.AddItem(option.Label);
            _battleView.Add(option);
        }

        // 只有一场战斗时默认选中，省掉一次点击。
        _selectedBattle = IndexOfId(_battleView, keepId);
        if (_selectedBattle < 0 && _battleView.Count == 1)
        {
            _selectedBattle = 0;
        }

        if (_selectedBattle >= 0)
        {
            _battleList.Select(_selectedBattle);
        }

        _rebuilding = false;
    }

    private void RebuildEventList(string? keepId)
    {
        if (_eventList is null || _service is null)
        {
            return;
        }

        _eventView.Clear();
        _rebuilding = true;
        _eventList.Clear();
        foreach (var option in _service.ListEvents())
        {
            _eventList.AddItem(option.Label);
            _eventView.Add(option);
        }

        _selectedEvent = IndexOfId(_eventView, keepId);
        if (_selectedEvent < 0 && _eventView.Count == 1)
        {
            _selectedEvent = 0;
        }

        if (_selectedEvent >= 0)
        {
            _eventList.Select(_selectedEvent);
        }

        _rebuilding = false;
    }

    private static bool Matches(RunDebugOption option, string filter) =>
        filter.Length == 0 || option.Id.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void ApplyTabTitles()
    {
        if (_tabs is null)
        {
            return;
        }

        // TabContainer 的页签标题取子节点名（英文），这里换成翻译键文案。
        string[] keys =
        [
            "UI_DEBUG_TAB_CARDS",
            "UI_DEBUG_TAB_CHARACTERS",
            "UI_DEBUG_TAB_BATTLES",
            "UI_DEBUG_TAB_EVENTS",
            "UI_DEBUG_TAB_TOOLS",
        ];

        for (var i = 0; i < keys.Length && i < _tabs.GetTabCount(); i++)
        {
            _tabs.SetTabTitle(i, Localization.Tr(keys[i]));
        }
    }

    private void FillSlotOptions()
    {
        if (_slotOption is null)
        {
            return;
        }

        _slotOption.Clear();
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            _slotOption.AddItem(string.Format(Localization.Tr("UI_DEBUG_SLOT_FORMAT"), i + 1));
        }

        _slotOption.Select(0);
    }

    private void FillPhaseOptions()
    {
        if (_phaseOption is null)
        {
            return;
        }

        _phaseOption.Clear();
        foreach (var phase in Enum.GetValues<ERunPhase>())
        {
            _phaseOption.AddItem(Localization.Tr($"UI_RUN_PHASE_{phase.ToString().ToUpperInvariant()}"));
        }
    }

    /// <summary>充能球类型下拉：内容里注册了什么就列什么（含 Mod 的特殊球）。</summary>
    private void FillOrbOptions()
    {
        if (_orbOption is null || _service is null)
        {
            return;
        }

        _orbOption.Clear();
        _orbView.Clear();
        foreach (var orbType in _service.ListOrbTypes())
        {
            var label = string.IsNullOrWhiteSpace(orbType.DisplayNameId)
                ? orbType.Id
                : Localization.Tr(orbType.DisplayNameId);
            _orbView.Add(new RunDebugOption(orbType.Id, label));
            _orbOption.AddItem($"{label}（{orbType.Id}）");
        }

        if (_orbView.Count > 0)
        {
            _orbOption.Select(0);
        }
    }

    #endregion

    #region 选择回调

    private void OnCardSelected(long index)
    {
        if (!_rebuilding)
        {
            _selectedCard = (int)index;
        }
    }

    private void OnCharacterSelected(long index)
    {
        if (!_rebuilding)
        {
            _selectedCharacter = (int)index;
        }
    }

    private void OnBattleSelected(long index)
    {
        if (!_rebuilding)
        {
            _selectedBattle = (int)index;
        }
    }

    private void OnEventSelected(long index)
    {
        if (!_rebuilding)
        {
            _selectedEvent = (int)index;
        }
    }

    private void OnCardFilterChanged(string _) => RebuildCardList(IdAt(_cardView, _selectedCard));

    private void OnCharFilterChanged(string _) => RebuildCharacterList(IdAt(_charView, _selectedCharacter));

    #endregion

    #region 操作

    private void OnGrantCard()
    {
        if (TryGetSelected(_cardView, _selectedCard, out var id))
        {
            Run(service => service.GrantCard(id));
        }
    }

    private void OnCardToDeck()
    {
        if (TryGetSelected(_cardView, _selectedCard, out var id))
        {
            Run(service => service.AddCardToDeck(Slot, id));
        }
    }

    private void OnRevokeCard()
    {
        if (TryGetSelected(_cardView, _selectedCard, out var id))
        {
            Run(service => service.RevokeCard(id));
        }
    }

    private void OnGrantCharacter()
    {
        if (TryGetSelected(_charView, _selectedCharacter, out var id))
        {
            Run(service => service.GrantCharacter(id));
        }
    }

    private void OnDeployCharacter()
    {
        if (TryGetSelected(_charView, _selectedCharacter, out var id))
        {
            Run(service => service.DeployCharacter(id, Slot));
        }
    }

    private void OnFillParty() => Run(service => service.FillPartyFromPool());

    private void OnStartBattle()
    {
        if (!TryGetSelected(_battleView, _selectedBattle, out var id))
        {
            return;
        }

        // 种子：填了就用它；留空时看「随机种子」勾选 —— 勾选取随机值，否则沿用 RunSeed（run_debug 流）。
        int? seed = null;
        var raw = _battleSeedInput?.Text?.Trim();
        if (!string.IsNullOrEmpty(raw))
        {
            if (!int.TryParse(raw, out var parsed))
            {
                SetStatus(false, Localization.Tr("UI_DEBUG_INVALID_NUMBER"));
                return;
            }

            seed = parsed;
        }

        var useRandomSeed = _battleRandomSeedCheck?.ButtonPressed ?? false;

        // 开战成功后关闭调试面板，直接回到 Run 界面（战斗窗由阶段监听打开）；失败保留面板显示原因。
        if (Run(service => service.StartBattle(id, seed, useRandomSeed)).Ok)
        {
            Close();
        }
    }

    private void OnEndBattleWin() => Run(service => service.EndBattle(true));

    private void OnEndBattleLose() => Run(service => service.EndBattle(false));

    private void OnTriggerEvent()
    {
        if (TryGetSelected(_eventView, _selectedEvent, out var id))
        {
            Run(service => service.TriggerEvent(id));
        }
    }

    private void OnAddGold()
    {
        if (!TryParsePositive(_goldInput?.Text, out var amount))
        {
            SetStatus(false, Localization.Tr("UI_DEBUG_INVALID_NUMBER"));
            return;
        }

        Run(service => service.AddGold(amount));
    }

    private void OnSetRing()
    {
        if (!TryParsePositive(_ringInput?.Text, out var ring))
        {
            SetStatus(false, Localization.Tr("UI_DEBUG_INVALID_NUMBER"));
            return;
        }

        Run(service => service.SetRing(ring));
    }

    private void OnSetPhase()
    {
        var phases = Enum.GetValues<ERunPhase>();
        var index = _phaseOption?.Selected ?? -1;
        if (index < 0 || index >= phases.Length)
        {
            SetStatus(false, Localization.Tr("UI_DEBUG_NO_SELECTION"));
            return;
        }

        Run(service => service.SetPhase(phases[index]));
    }

    #region 潜能与战斗检查

    private bool TryGetPotentialAmount(out int amount)
    {
        if (!TryParsePositive(_potentialInput?.Text, out amount))
        {
            SetStatus(false, Localization.Tr("UI_DEBUG_INVALID_NUMBER"));
            return false;
        }

        return true;
    }

    private void OnGrantPotentialPool()
    {
        if (!TryGetPotentialAmount(out var amount))
        {
            return;
        }

        Run(service => service.GrantPotential(amount));
    }

    private void OnGrantPotentialSlot()
    {
        if (!TryGetPotentialAmount(out var amount))
        {
            return;
        }

        Run(service => service.GrantPotential(amount, Slot));
    }

    private void OnAllocatePotential()
    {
        if (!TryGetPotentialAmount(out var amount))
        {
            return;
        }

        Run(service => service.AllocatePotential(Slot, amount));
    }

    private void OnDeductPotential()
    {
        if (!TryGetPotentialAmount(out var amount))
        {
            return;
        }

        Run(service => service.DeductPotential(Slot, amount));
    }

    private void OnInspectBattle()
    {
        if (_service is null)
        {
            SetStatus(false, Localization.Tr("UI_DEBUG_NO_RUN"));
            return;
        }

        var report = _service.InspectBattle();
        SetStatus(true, report.Split('\n').FirstOrDefault() ?? report);
        WriteLog(true, report);
    }

    private void OnGrantOrb()
    {
        var count = 1;
        var text = _orbCountInput?.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (!TryParsePositive(text, out count))
            {
                SetStatus(false, Localization.Tr("UI_DEBUG_INVALID_NUMBER"));
                return;
            }
        }

        if (TryGetSelected(_orbView, _orbOption?.Selected ?? -1, out var orbTypeId))
        {
            Run(service => service.GrantOrb(orbTypeId, count, Slot));
        }
    }

    private void OnTriggerOrbs() => Run(service => service.TriggerOrbs());

    #endregion

    /// <summary>统一执行入口：捕获异常、写状态行与日志，并刷新依赖状态的列表着色；返回本次操作结果。</summary>
    private RunDebugResult Run(Func<RunDebugService, RunDebugResult> action)
    {
        if (_service is null)
        {
            SetStatus(false, Localization.Tr("UI_DEBUG_NO_RUN"));
            return RunDebugResult.Failure(Localization.Tr("UI_DEBUG_NO_RUN"));
        }

        RunDebugResult result;
        try
        {
            result = action(_service);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            result = RunDebugResult.Failure(ex.Message);
        }

        SetStatus(result.Ok, result.Message);
        WriteLog(result.Ok, result.Message);

        // 数据视图可能变化（收藏 / 角色池 / 阶段），重建列表并保留当前页签。
        var activeTab = _tabs?.CurrentTab ?? 0;
        RebuildAll();
        if (_tabs != null)
        {
            _tabs.CurrentTab = activeTab;
        }

        // 此处**不能**再 RefreshStatus()：它无条件重写 _statusLabel.Text，会把刚写的操作结果
        // 换成阶段摘要，只留下成败颜色与内容不符。摘要由 OnOpen / UpdateView 负责显示。
        return result;
    }

    private static bool TryParsePositive(string? text, out int value)
    {
        value = 0;
        if (!int.TryParse(text?.Trim(), out var parsed) || parsed <= 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private bool TryGetSelected(IReadOnlyList<RunDebugOption> view, int index, out string id)
    {
        if (index < 0 || index >= view.Count)
        {
            id = "";
            SetStatus(false, Localization.Tr("UI_DEBUG_NO_SELECTION"));
            return false;
        }

        id = view[index].Id;
        return true;
    }

    private void SetActionsEnabled(bool enabled)
    {
        foreach (var button in ActionButtons())
        {
            if (button != null)
            {
                button.Disabled = !enabled;
            }
        }
    }

    private IEnumerable<Button?> ActionButtons()
    {
        yield return _btnGrantCard;
        yield return _btnCardToDeck;
        yield return _btnRevokeCard;
        yield return _btnGrantCharacter;
        yield return _btnDeployCharacter;
        yield return _btnFillParty;
        yield return _btnStartBattle;
        yield return _btnEndWin;
        yield return _btnEndLose;
        yield return _btnTriggerEvent;
        yield return _btnAddGold;
        yield return _btnSetRing;
        yield return _btnSetPhase;
        yield return _btnPotentialPool;
        yield return _btnPotentialSlot;
        yield return _btnAllocatePotential;
        yield return _btnDeductPotential;
        yield return _btnInspectBattle;
        yield return _btnGrantOrb;
        yield return _btnTriggerOrbs;
    }

    #endregion

    #region 状态与日志

    private void RefreshStatus()
    {
        if (_service is null || _statusLabel is null)
        {
            return;
        }

        var state = _service.State;
        var party = string.Join("/", state.ActiveParty.Select(c => c?.DefinitionId ?? "-"));
        _statusLabel.Text = string.Format(
            Localization.Tr("UI_DEBUG_STATUS_FORMAT"),
            Localization.Tr($"UI_RUN_PHASE_{state.Phase.ToString().ToUpperInvariant()}"),
            state.CurrentRing,
            state.MaxRing,
            _service.Gold,
            state.CardCollection.Count,
            state.CharacterPool.Count,
            party);
    }

    private void SetStatus(bool ok, string message)
    {
        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.AddThemeColorOverride("font_color", ok ? OkColor : ErrorColor);
    }

    private void ClearLog() => _log?.Clear();

    private void WriteLog(bool ok, string message)
    {
        if (_log is null)
        {
            return;
        }

        _log.PushColor(ok ? OwnedColor : ErrorColor);
        _log.AddText(ok ? "· " : "× ");
        _log.Pop();
        _log.AddText(message);
        _log.Newline();
    }

    #endregion
}