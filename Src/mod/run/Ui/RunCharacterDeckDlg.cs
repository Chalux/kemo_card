using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Run.Team;

namespace KemoCard.Mod.Run.Ui;

/// <summary>二级界面的载荷：要编辑的角色实例 + 上阵目标槽位。</summary>
public record struct RunCharacterDeckDlgPayload
{
    public string InstanceId { get; init; }

    public int SlotIndex { get; init; }
}

/// <summary>
/// 队伍编辑的二级界面：编辑某个角色的卡组（点击增删 / 长按查看卡牌详情），或把他上阵到指定槽位。
/// </summary>
/// <remarks>语义全部在 <see cref="RunTeamEditService"/>；本类只做绑定与显示。</remarks>
public partial class RunCharacterDeckDlg : BaseDlg
{
    [Export] private Label? _lblTitle;
    [Export] private Button? _btnClose;
    [Export] private Label? _lblDeckCaption;
    [Export] private TabBar? _deckTabs;
    [Export] private Button? _btnNewDeck;
    [Export] private Label? _lblDeckCount;
    [Export] private VirtualList? _deckList;
    [Export] private Label? _lblPoolCaption;
    [Export] private VirtualList? _poolList;
    [Export] private Button? _btnDeploy;
    [Export] private Label? _lblStatus;
    [Export] private Control? _passiveBox;
    [Export] private RichTextLabel? _rtPassives;

    private RunTeamEditService? _service;
    private string _instanceId = "";
    private int _slotIndex;
    private int _deckIndex;
    private DeckEditView? _deck;

    /// <summary>"可加入卡组"列表的数据（含"已在卡组内"标记），渲染回调按索引取用。</summary>
    private IReadOnlyList<DeckPoolCardView> _poolCards = [];

    /// <summary>true = 正在重建卡组页签：程序化选中不得被当成用户点击（否则递归）。</summary>
    private bool _rebuildingTabs;

    public override string UIId => RunUiIds.CharacterDeck;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        if (_btnClose != null)
        {
            OnClicks(_btnClose, Close);
        }

        if (_btnNewDeck != null)
        {
            OnClicks(_btnNewDeck, OnCreateDeck);
        }

        if (_btnDeploy != null)
        {
            OnClicks(_btnDeploy, OnDeploy);
        }

        if (_deckTabs != null)
        {
            var tabs = _deckTabs;
            Bind(() => tabs.TabChanged += OnDeckTabChanged, () => tabs.TabChanged -= OnDeckTabChanged);
        }
        // 列表模板（ItemTemplate / ItemSize）在场景里配置，编辑器可随时替换预制体，
        // 代码不再写死 res:// 路径。
    }

    protected override void OnOpen()
    {
        var payload = GetTypedPayload<RunCharacterDeckDlgPayload>();
        _instanceId = payload.InstanceId ?? "";
        _slotIndex = payload.SlotIndex;

        _service = RunRuntime.CreateTeamEditService();
        if (_service is null)
        {
            SetStatus(false, Localization.Tr("UI_TEAM_NO_RUN"));
            return;
        }

        if (_lblDeckCaption != null)
        {
            _lblDeckCaption.Text = Localization.Tr("UI_TEAM_DECK_CAPTION");
        }

        if (_lblPoolCaption != null)
        {
            _lblPoolCaption.Text = Localization.Tr("UI_TEAM_CARD_POOL_CAPTION");
        }

        var character = _service.FindCharacter(_instanceId);
        if (character is null)
        {
            SetStatus(false, Localization.Tr("UI_TEAM_CHARACTER_MISSING"));
            return;
        }

        _deckIndex = character.CurrentDeckIndex;
        RefreshAll();
    }

    protected override void UpdateView() => RefreshAll();

    #region 刷新

    private void RefreshAll()
    {
        if (_service is null)
        {
            return;
        }

        var character = _service.FindCharacter(_instanceId);
        if (character is null)
        {
            return;
        }

        if (_lblTitle != null)
        {
            var name = string.IsNullOrWhiteSpace(character.Definition?.DisplayNameId)
                ? character.DefinitionId
                : Localization.Tr(character.Definition!.DisplayNameId);
            _lblTitle.Text = string.Format(Localization.Tr("UI_TEAM_DECK_TITLE_FORMAT"), name);
        }

        RefreshDeckTabs();
        RefreshDeckList();
        RefreshPoolList();
        RefreshPassives();
        RefreshFooter(character);
    }

    /// <summary>
    /// 左栏被动区：角色的 4 条被动（门槛 + 解锁状态 + 描述），解锁状态取当前 Run 的实例。
    /// 没有被动的角色整块隐藏（标题与文本一起），不留空面板。
    /// </summary>
    private void RefreshPassives()
    {
        if (_rtPassives is null)
        {
            return;
        }

        var passives = _service?.GetPassives(_instanceId) ?? [];
        if (_passiveBox != null)
        {
            _passiveBox.Visible = passives.Count > 0;
        }

        if (passives.Count == 0)
        {
            _rtPassives.Text = "";
            return;
        }

        _rtPassives.Text = string.Join(
            "\n\n",
            passives.Select(passive => PassiveTextBuilder.Entry(
                passive.RequiredPotential,
                passive.Unlocked,
                string.IsNullOrWhiteSpace(passive.DescriptionId)
                    ? ""
                    : Localization.Tr(passive.DescriptionId),
                Localization.Tr)));
    }

    private void RefreshDeckTabs()
    {
        if (_deckTabs is null || _service is null)
        {
            return;
        }

        var decks = _service.GetDecks(_instanceId);

        // 重建期间的选中赋值必须屏蔽 TabChanged：ClearTabs 会把 CurrentTab 复位成 -1、首个
        // AddTab 又把它置 0（**不发信号**），因此随后赋 _deckIndex 只要 ≠ 0 就会同步发出
        // tab_changed → OnDeckTabChanged → RefreshAll → 回到本方法再赋值……无界递归（栈溢出）。
        _rebuildingTabs = true;
        try
        {
            _deckTabs.ClearTabs();
            foreach (var deck in decks)
            {
                _deckTabs.AddTab(string.Format(Localization.Tr("UI_TEAM_DECK_TAB_FORMAT"), deck.DeckIndex + 1));
            }

            if (_deckTabs.TabCount > 0)
            {
                _deckIndex = Math.Clamp(_deckIndex, 0, _deckTabs.TabCount - 1);
                _deckTabs.CurrentTab = _deckIndex;
            }
        }
        finally
        {
            _rebuildingTabs = false;
        }

        var character = _service.FindCharacter(_instanceId);
        if (_btnNewDeck != null)
        {
            _btnNewDeck.Disabled = !_service.CanEdit ||
                character is null ||
                character.IsDeckLocked ||
                character.Decks.Count >= Combat.CombatConstants.MaxDecksPerCharacter;
        }
    }

    private void RefreshDeckList()
    {
        if (_deckList is null || _service is null)
        {
            return;
        }

        _deck = _service.GetDeck(_instanceId, _deckIndex);
        var cards = _deck?.CardIds ?? [];
        if (_lblDeckCount != null)
        {
            _lblDeckCount.Text = string.Format(
                Localization.Tr("UI_TEAM_DECK_COUNT_FORMAT"),
                cards.Count,
                _deck?.MaxCards ?? _service.MaxCardsPerDeck);
        }

        _deckList.SetData(cards.Count, (index, item) => RenderDeckCard(index, item, cards));
    }

    private void RenderDeckCard(int index, Control item, IReadOnlyList<string> cards)
    {
        if (item is not BaseCardItem cardItem || _service is null || index < 0 || index >= cards.Count)
        {
            return;
        }

        var cardId = cards[index];
        cardItem.SetData(_service.GetCard(cardId));
        cardItem.EnableHoverTip = true;
        cardItem.EnableLongPress = true;
        cardItem.ClickAction = ECardClickAction.Emit;
        cardItem.MouseFilter = _service.CanEdit ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        cardItem.Clicked = (_, card) => RemoveCard(card.Id);
        cardItem.LongPressed = (_, card) => OpenCardDetails(card.Id);
    }

    private void RefreshPoolList()
    {
        if (_poolList is null || _service is null)
        {
            return;
        }

        _poolCards = _service.GetPoolCards(_instanceId, _deckIndex);
        _poolList.SetData(_poolCards.Count, RenderPoolCard);
    }

    private void RenderPoolCard(int index, Control item)
    {
        if (item is not BaseCardItem cardItem || _service is null || index < 0 || index >= _poolCards.Count)
        {
            return;
        }

        var entry = _poolCards[index];
        cardItem.SetData(_service.GetCard(entry.CardId));
        cardItem.EnableHoverTip = true;
        cardItem.EnableLongPress = true;
        cardItem.ClickAction = ECardClickAction.Emit;
        cardItem.MouseFilter = _service.CanEdit ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        // 已在当前卡组内的牌：遮罩 + "已在卡组内"提示，并且**不再响应点击**——
        // 卡组不允许重复，点下去只会拿到一句失败提示（服务层仍会兜底拒绝）。
        if (entry.InDeck)
        {
            cardItem.Clicked = null;
            cardItem.SetOverlay(Localization.Tr("UI_TEAM_DECK_DUPLICATE"));
        }
        else
        {
            cardItem.Clicked = (_, card) => AddCard(card.Id);
        }

        cardItem.LongPressed = (_, card) => OpenCardDetails(card.Id);
    }

    /// <summary>长按查看卡牌详情（与 <c>BaseCardItem</c> 的默认点击行为共用同一入口）。</summary>
    private static void OpenCardDetails(string cardId) => _ = GlobalModController.OpenCardDetailsAsync(cardId);

    private void RefreshFooter(CharacterInstance character)
    {
        if (_btnDeploy != null)
        {
            var deployedHere = _service?.FindPoolEntry(_instanceId)?.AssignedSlotIndex == _slotIndex;
            _btnDeploy.Text = deployedHere
                ? Localization.Tr("UI_TEAM_ALREADY_DEPLOYED")
                : string.Format(Localization.Tr("UI_TEAM_DEPLOY_TO_SLOT_FORMAT"), _slotIndex + 1);
            _btnDeploy.Disabled = _service is null || !_service.CanEdit;
        }

        if (_deck is { InvalidCardIds.Count: > 0 } deck)
        {
            SetStatus(false, string.Format(Localization.Tr("UI_TEAM_DECK_INVALID_FORMAT"), deck.InvalidCardIds.Count));
            return;
        }

        if (_service is { CanEdit: false } service && service.EditBlockReasonKey is { } reasonKey)
        {
            SetStatus(false, Localization.Tr(reasonKey));
            return;
        }

        SetStatus(true, Localization.Tr("UI_TEAM_HINT_CLICK_OR_HOLD"));
    }

    #endregion

    #region 交互

    private void OnDeckTabChanged(long tab)
    {
        // 只看用户点击：RefreshDeckTabs 的程序化选中已在重建窗口内被屏蔽。
        if (_rebuildingTabs || tab < 0 || (int)tab == _deckIndex)
        {
            return;
        }

        _deckIndex = (int)tab;
        if (_service is not null)
        {
            _service.SetCurrentDeck(_instanceId, _deckIndex);
        }

        RefreshAll();
    }

    private void OnCreateDeck()
    {
        if (_service is null)
        {
            return;
        }

        var result = _service.CreateDeck(_instanceId);
        if (result.Ok && _service.FindCharacter(_instanceId) is { } character)
        {
            _deckIndex = character.CurrentDeckIndex;
        }

        // 顺序不能反：RefreshAll → RefreshFooter 会写常驻提示，先写结果就会被它覆盖掉。
        RefreshAll();
        SetStatus(result.Ok, Localization.Tr(result.MessageKey));
    }

    private void OnDeploy()
    {
        if (_service is null)
        {
            return;
        }

        var result = _service.AssignToSlot(_slotIndex, _instanceId);
        RefreshAll();
        SetStatus(result.Ok, Localization.Tr(result.MessageKey));
    }

    private void AddCard(string cardId)
    {
        if (_service is null)
        {
            return;
        }

        var result = _service.AddCard(_instanceId, _deckIndex, cardId);
        RefreshAll();
        SetStatus(result.Ok, Localization.Tr(result.MessageKey));
    }

    private void RemoveCard(string cardId)
    {
        if (_service is null)
        {
            return;
        }

        var result = _service.RemoveCard(_instanceId, _deckIndex, cardId);
        RefreshAll();
        SetStatus(result.Ok, Localization.Tr(result.MessageKey));
    }

    #endregion

    #region 状态

    private void SetStatus(bool ok, string message)
    {
        if (_lblStatus is null)
        {
            return;
        }

        _lblStatus.Text = message;
        _lblStatus.AddThemeColorOverride(
            "font_color",
            ok ? KemoPalette.TextSecondary : KemoPalette.TextDanger);
    }

    #endregion
}