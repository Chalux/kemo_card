using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Run.Events;
using KemoCard.Mod.Run.Team;

namespace KemoCard.Mod.Run.Ui;

/// <summary>
/// 队伍编辑：顶部 4 个槽位选项卡，左"当前上阵"、中"角色池"、右"详细信息预览"。
/// </summary>
/// <remarks>
/// <para>本类只做「取值 + 显示 + 把用户操作转成服务调用」；槽位/上阵/下阵/卡组的全部语义在
/// <see cref="RunTeamEditService"/>（不依赖 Godot，可单测）。</para>
/// <para>悬停角色头像刷新右侧预览；单击角色头像进入二级界面（<see cref="RunCharacterDeckDlg"/>）
/// 编辑该角色卡组或上阵。关闭时若有改动则统一保存（<c>RunRuntime.SaveCurrent</c>）。</para>
/// </remarks>
public partial class RunTeamEditDlg : BaseDlg
{
    [Export] private Label? _lblTitle;
    [Export] private Button? _btnClose;
    [Export] private TabBar? _slotTabs;
    [Export] private Label? _lblCurrentCaption;
    [Export] private Control? _currentHolder;
    [Export] private BaseCharacterItem? _currentCharacter;
    [Export] private Button? _btnUnassign;
    [Export] private Label? _lblPoolCaption;
    [Export] private VirtualList? _poolList;
    [Export] private CharacterPresenter? _presenter;
    [Export] private Label? _lblName;
    [Export] private Label? _lblInfo;
    [Export] private Label? _lblAttrs;
    [Export] private Label? _lblDeckCaption;
    [Export] private VirtualList? _deckStrip;
    [Export] private Label? _lblStatus;
    [Export] private Label? _lblPotentialPool;
    [Export] private Label? _lblSlotPotential;
    [Export] private LineEdit? _potentialInput;
    [Export] private Button? _btnAllocate;
    [Export] private Button? _btnDeduct;

    private RunTeamEditService? _service;
    private int _slotIndex;
    private string? _previewInstanceId;
    private IReadOnlyList<TeamPoolEntryView> _pool = [];

    private IEventListener<RunCharacterAssignedPayload>? _onCharacterAssigned;
    private IEventListener<RunDeckChangedPayload>? _onDeckChanged;

    public override string UIId => RunUiIds.TeamEdit;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        if (_btnClose != null)
        {
            OnClicks(_btnClose, Close);
        }

        if (_btnUnassign != null)
        {
            OnClicks(_btnUnassign, OnUnassign);
        }

        if (_btnAllocate != null)
        {
            OnClicks(_btnAllocate, OnAllocate);
        }

        if (_btnDeduct != null)
        {
            OnClicks(_btnDeduct, OnDeduct);
        }

        if (_slotTabs != null)
        {
            var tabs = _slotTabs;
            Bind(() => tabs.TabChanged += OnSlotTabChanged, () => tabs.TabChanged -= OnSlotTabChanged);
        }

        // 列表模板（ItemTemplate / ItemSize）与"当前上阵角色"项都在场景里配置，
        // 编辑器可随时替换预制体，代码不再写死 res:// 路径。
        if (_currentCharacter != null)
        {
            _currentCharacter.ClickAction = ECharacterClickAction.Emit;
            _currentCharacter.Hovered = OnCurrentCharacterHovered;
            _currentCharacter.Clicked = (_, _) => OpenDeckEditor(CurrentSlotInstanceId());
        }

        // 二级界面 / 调试面板改动数据后回来刷新：订阅 Run 功能内部总线（随界面离场自动退订）。
        Bind(SubscribeTeamEvents, UnsubscribeTeamEvents);
    }

    protected override void OnOpen()
    {
        _service = RunRuntime.CreateTeamEditService();
        if (_service is null)
        {
            SetStatus(false, Localization.Tr("UI_TEAM_NO_RUN"));
            if (_btnUnassign != null)
            {
                _btnUnassign.Disabled = true;
            }

            return;
        }

        if (_lblTitle != null)
        {
            _lblTitle.Text = Localization.Tr("UI_TEAM_EDIT_TITLE");
        }

        if (_lblCurrentCaption != null)
        {
            _lblCurrentCaption.Text = Localization.Tr("UI_TEAM_CURRENT_CAPTION");
        }

        if (_lblPoolCaption != null)
        {
            _lblPoolCaption.Text = Localization.Tr("UI_TEAM_POOL_CAPTION");
        }

        if (_lblDeckCaption != null)
        {
            _lblDeckCaption.Text = Localization.Tr("UI_TEAM_DECK_CAPTION");
        }

        RefreshAll();
    }

    protected override void UpdateView() => RefreshAll();

    protected override void OnClose()
    {
        // 保存时机：关闭界面时统一落盘（仅在本次编辑有改动时）。
        if (_service is { IsDirty: true })
        {
            RunRuntime.SaveCurrent();
        }
    }

    #region 刷新

    /// <summary>
    /// 订阅 Run 功能内部总线：槽位上阵变化与卡组变化都要重读（不只是本界面自己改的）。
    /// </summary>
    private void SubscribeTeamEvents()
    {
        if (RunRuntime.Current?.State is not { } mod)
        {
            return;
        }

        _onCharacterAssigned = mod.OnRunCharacterAssigned((_, _) => RefreshAll(), this);
        _onDeckChanged = mod.OnRunDeckChanged((_, _) => RefreshAll(), this);
    }

    private void UnsubscribeTeamEvents()
    {
        _onCharacterAssigned?.Off();
        _onDeckChanged?.Off();
        _onCharacterAssigned = null;
        _onDeckChanged = null;
    }

    private void RefreshAll()
    {
        if (_service is null)
        {
            return;
        }

        var editable = _service.CanEdit;
        if (_slotTabs != null)
        {
            _slotTabs.MouseFilter = editable ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        }

        if (_btnUnassign != null)
        {
            _btnUnassign.Disabled = !editable;
        }

        RefreshSlotTabs();
        RefreshCurrentSlot();
        RefreshPotential();
        RefreshPool();
        RefreshPreview();
    }

    private void RefreshSlotTabs()
    {
        if (_slotTabs is null || _service is null)
        {
            return;
        }

        var slots = _service.GetSlots();
        _slotTabs.ClearTabs();
        foreach (var slot in slots)
        {
            var label = string.Format(Localization.Tr("UI_TEAM_SLOT_FORMAT"), slot.SlotIndex + 1);
            if (!string.IsNullOrWhiteSpace(slot.DisplayNameId))
            {
                label += $" · {Localization.Tr(slot.DisplayNameId!)}";
            }

            _slotTabs.AddTab(label);
        }

        if (_slotTabs.TabCount > 0)
        {
            _slotIndex = Math.Clamp(_slotIndex, 0, _slotTabs.TabCount - 1);
            _slotTabs.CurrentTab = _slotIndex;
        }
    }

    private void RefreshCurrentSlot()
    {
        if (_service is null || _currentCharacter is null)
        {
            return;
        }

        var slots = _service.GetSlots();
        if (_slotIndex < 0 || _slotIndex >= slots.Count)
        {
            return;
        }

        var slot = slots[_slotIndex];
        var definition = slot.InstanceId is null ? null : _service.FindCharacter(slot.InstanceId)?.Definition;
        _currentCharacter.SetData(definition);
        _currentCharacter.SetBadge(null);
        if (_btnUnassign != null)
        {
            _btnUnassign.Disabled = !_service.CanEdit || slot.InstanceId is null;
        }

        // 切换槽位后默认预览该槽位上阵的角色。
        _previewInstanceId = slot.InstanceId;
    }

    /// <summary>当前槽位上阵的角色实例 id（空槽为 null）。</summary>
    private string? CurrentSlotInstanceId()
    {
        var slots = _service?.GetSlots();
        return slots is not null && _slotIndex >= 0 && _slotIndex < slots.Count
            ? slots[_slotIndex].InstanceId
            : null;
    }

    private void OnCurrentCharacterHovered(BaseCharacterItem _, CharacterDto? character)
    {
        if (character is not null)
        {
            ShowCharacterPreview(character, CurrentSlotInstanceId());
        }
    }

    private void RefreshPool()
    {
        if (_service is null || _poolList is null)
        {
            return;
        }

        _pool = _service.GetPoolEntries();
        _poolList.SetData(_pool.Count, RenderPoolItem);

        // 空池必须显式说明，否则"列表空白"无法与"渲染失败"区分（见 2026-09-21 规格 §5）。
        if (_lblPoolCaption != null)
        {
            _lblPoolCaption.Text = _pool.Count == 0
                ? Localization.Tr("UI_TEAM_POOL_EMPTY")
                : Localization.Tr("UI_TEAM_POOL_CAPTION");
        }
    }

    private void RenderPoolItem(int index, Control item)
    {
        if (_service is null || item is not BaseCharacterItem characterItem)
        {
            return;
        }

        if (index < 0 || index >= _pool.Count)
        {
            characterItem.SetData(null);
            return;
        }

        var entry = _pool[index];
        var definition = _service.FindCharacter(entry.InstanceId)?.Definition;
        characterItem.SetData(definition);
        characterItem.SetBadge(entry.AssignedSlotIndex is { } assignedSlot
            ? string.Format(Localization.Tr("UI_TEAM_ASSIGNED_SLOT_FORMAT"), assignedSlot + 1)
            : null);
        characterItem.ClickAction = ECharacterClickAction.Emit;
        characterItem.MouseFilter = _service.CanEdit ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        characterItem.Hovered = (_, character) =>
        {
            if (character is not null)
            {
                ShowCharacterPreview(character, entry.InstanceId);
            }
        };
        characterItem.Clicked = (_, _) => OpenDeckEditor(entry.InstanceId);
    }

    private void RefreshPreview()
    {
        var instanceId = _previewInstanceId;
        var character = instanceId is null ? null : _service?.FindCharacter(instanceId);
        ShowCharacterPreview(character?.Definition, instanceId);
    }

    private void ShowCharacterPreview(CharacterDto? character, string? instanceId)
    {
        if (_service is null)
        {
            return;
        }

        if (character is null)
        {
            if (_presenter != null)
            {
                _presenter.Visible = false;
            }

            if (_lblName != null)
            {
                _lblName.Text = Localization.Tr("UI_TEAM_NO_CHARACTER");
            }

            if (_lblInfo != null)
            {
                _lblInfo.Text = "";
            }

            if (_lblAttrs != null)
            {
                _lblAttrs.Text = "";
            }

            _deckStrip?.SetData(0, static (_, _) => { });
            return;
        }

        _previewInstanceId = instanceId;
        if (_presenter != null)
        {
            _presenter.Visible = true;
            _presenter.Bind(character);
        }

        if (_lblName != null)
        {
            _lblName.Text = string.IsNullOrWhiteSpace(character.DisplayNameId)
                ? character.Id
                : Localization.Tr(character.DisplayNameId);
        }

        if (_lblInfo != null)
        {
            _lblInfo.Text = $"{ElementLabel(character.Element)} / {RaceLabel(character.Race)} / {RoleLabel(character.Role)}";
        }

        var instance = instanceId is null ? null : _service.FindCharacter(instanceId);
        var attributes = instance?.ComputeAttributeMap(AppRoot.Services.ContentModPipeline.Registry)
            ?? new Dictionary<string, float>(StringComparer.Ordinal);
        if (_lblAttrs != null)
        {
            _lblAttrs.Text = attributes.Count == 0
                ? ""
                : string.Join(
                    "\n",
                    AttributeLabels
                        .Order(attributes)
                        .Select(pair => $"{AttributeLabels.Name(pair.Key, Localization.Tr)} {pair.Value:0.##}"));
        }

        RefreshDeckStrip(instance);
    }

    private void RefreshDeckStrip(CharacterInstance? instance)
    {
        if (_deckStrip is null || _service is null)
        {
            return;
        }

        var deck = instance?.GetCurrentDeck();
        var cardIds = deck?.CardIds ?? [];
        _deckStrip.SetData(cardIds.Count, (index, item) =>
        {
            if (item is not BaseCardItem cardItem || index < 0 || index >= cardIds.Count)
            {
                return;
            }

            cardItem.SetData(_service.GetCard(cardIds[index]));
            cardItem.ClickAction = ECardClickAction.OpenDetails;
            // 悬停摘要（CardSummaryBuilder：费用/属性/定位/技能描述）——预览卡也要能看信息，
            // 与卡组编辑、角色详情的卡面同一交互；条目是对象池复用的，每次渲染显式打开。
            cardItem.EnableHoverTip = true;
        });
    }

    /// <summary>
    /// 元素/种族/定位显示名：与角色详情、角色悬停摘要共用 <see cref="CharacterIdentityLabels"/>
    /// 的口径（键与连接符都在那里）。本条信息行是"值 / 值 / 值"，因此空值显式回落 <c>-</c>——
    /// 否则双种族角色会在这里显示 "Animal, Dragon" 这类英文枚举名，或者干脆留一段空白。
    /// </summary>
    private static string ElementLabel(EElement element)
    {
        var text = CharacterIdentityLabels.Element(element, Localization.Tr);
        return text.Length == 0 ? "-" : text;
    }

    private static string RaceLabel(ERace race)
    {
        var text = CharacterIdentityLabels.Race(race, Localization.Tr);
        return text.Length == 0 ? "-" : text;
    }

    /// <summary>
    /// 职业显示名走与图鉴同一套 <c>UI_ROLE_*</c> 键；缺键时才回落枚举原名——直接插
    /// <c>character.Role</c> 会在界面露出 <c>SwordMan</c> 这类英文枚举名（见 <see cref="CharacterIdentityLabels.Role"/>）。
    /// </summary>
    private static string RoleLabel(ERole role) => CharacterIdentityLabels.Role(role, Localization.Tr);

    #endregion

    #region 交互

    private void OnSlotTabChanged(long tab)
    {
        _slotIndex = (int)tab;
        RefreshCurrentSlot();
        RefreshPotential();
        RefreshPreview();
    }

    /// <summary>
    /// 潜能区：团队池可用 + 当前槽位已分配（进度值）；按钮在不可编辑或对应方向没有额度时禁用。
    /// 数额用输入框，超额的请求由服务层拒绝并回报可翻译的原因键。
    /// </summary>
    private void RefreshPotential()
    {
        if (_service is null)
        {
            return;
        }

        var available = _service.AvailablePotential;
        var allocated = _service.SlotAllocatedPotential(_slotIndex);
        if (_lblPotentialPool != null)
        {
            _lblPotentialPool.Text = string.Format(Localization.Tr("UI_TEAM_POTENTIAL_POOL_FORMAT"), available);
        }

        if (_lblSlotPotential != null)
        {
            _lblSlotPotential.Text = string.Format(Localization.Tr("UI_TEAM_POTENTIAL_SLOT_FORMAT"), allocated);
        }

        var editable = _service.CanEdit;
        if (_btnAllocate != null)
        {
            _btnAllocate.Disabled = !editable || available <= 0;
        }

        if (_btnDeduct != null)
        {
            _btnDeduct.Disabled = !editable || allocated <= 0;
        }

        if (_potentialInput != null)
        {
            _potentialInput.Editable = editable;
        }
    }

    private void OnAllocate() => ApplyPotential(allocate: true);

    private void OnDeduct() => ApplyPotential(allocate: false);

    private void ApplyPotential(bool allocate)
    {
        if (_service is null)
        {
            return;
        }

        if (!TryGetPotentialAmount(out var amount))
        {
            SetStatus(false, Localization.Tr("UI_TEAM_POTENTIAL_INVALID"));
            return;
        }

        var result = allocate
            ? _service.AllocatePotential(_slotIndex, amount)
            : _service.DeductPotential(_slotIndex, amount);

        SetStatus(result.Ok, result.Ok
            ? string.Format(
                Localization.Tr(allocate ? "UI_TEAM_POTENTIAL_ALLOCATED_FORMAT" : "UI_TEAM_POTENTIAL_DEDUCTED_FORMAT"),
                amount)
            : Localization.Tr(result.MessageKey));
        RefreshAll();
    }

    private bool TryGetPotentialAmount(out int amount)
    {
        amount = 0;
        return int.TryParse(_potentialInput?.Text?.Trim(), out amount) && amount > 0;
    }

    private void OnUnassign()
    {
        if (_service is null)
        {
            return;
        }

        var result = _service.UnassignSlot(_slotIndex);
        SetStatus(result.Ok, Localization.Tr(result.MessageKey));
        RefreshAll();
    }

    private void OpenDeckEditor(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        _ = RunUiController.OpenCharacterDeckAsync(instanceId, _slotIndex);
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