using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Run.Ui.CombatUi.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 手牌槽：<see cref="BaseCardItem"/>（点击 Emit / 悬停摘要 / 长按详情）+ 已标记遮罩 + 待出牌高亮 + 槽位 buff 图标 +
/// 充能指示（<see cref="SlotChargeCmp"/>：环绕光晕 + 顶部进度条）。
/// 空槽只显示底板。
/// </summary>
public partial class HandSlotCmp : BaseCmp
{
    [Export] private BaseCardItem? _card;
    [Export] private Panel? _pendingFrame;
    [Export] private BuffListCmp? _slotBuffs;
    [Export] private Control? _emptyHint;
    [Export] private SlotChargeCmp? _charge;

    /// <summary>点击回调，参数为槽位索引。</summary>
    public Action<int>? Clicked { get; set; }

    public int SlotIndex { get; private set; } = -1;
    public BaseCardItem? Card => _card;
    public BuffListCmp? SlotBuffs => _slotBuffs;
    public bool HasCard { get; private set; }

    private bool _interactable;

    protected override void OnReady()
    {
        if (_card == null)
            return;

        _card.ClickAction = ECardClickAction.Emit;
        _card.EnableHoverTip = true;
        _card.EnableLongPress = true;
        _card.PreferTipSide = Frame.Content.Keywords.TipSide.Right;
    }

    protected override void InitEvent()
    {
        if (_card == null)
            return;

        // 回调是属性赋值而不是信号订阅：离场时框架不会自动清，这里成对写在 InitEvent / OnExitTree。
        _card.Clicked = OnCardClicked;
        _card.LongPressed = OnCardLongPressed;
    }

    protected override void OnExitTree()
    {
        if (_card == null)
            return;

        _card.Clicked = null;
        _card.LongPressed = null;
    }

    public void Bind(int slotIndex, HandSlot slot, CardDto? card, bool pending, bool interactable)
    {
        SlotIndex = slotIndex;
        _interactable = interactable;
        HasCard = card is not null && !slot.IsEmpty;

        if (_card != null)
        {
            _card.Visible = HasCard;
            _card.SetData(HasCard ? card : null);
            _card.SetOverlay(HasCard && slot.IsMarked ? Localization.Tr("UI_COMBAT_CARD_MARKED") : null);
            _card.Modulate = _card.Modulate with { A = interactable || !HasCard ? 1f : 0.7f };
        }

        if (_emptyHint != null)
            _emptyHint.Visible = !HasCard;

        SetPending(pending);
        _slotBuffs?.Bind(slot.Buffs.Visible);
        _charge?.Bind(slot.Buffs.FindByTag(BuiltinBuffTags.SlotCharge));
    }

    public void SetPending(bool pending)
    {
        if (_pendingFrame != null)
        {
            _pendingFrame.Visible = pending;
            _pendingFrame.SelfModulate = KemoPalette.Accent;
        }
    }

    public Task PopInAsync(float duration) =>
        _card is null ? Task.CompletedTask : UnitTweens.PulseAsync(_card, 1.1f, duration);

    /// <summary>弃牌：卡牌飞向 <paramref name="globalTarget"/> 并淡出，随后由对账重绘。</summary>
    public async Task FlyOutAsync(Vector2 globalTarget, float duration)
    {
        if (_card is null || !_card.Visible || !IsInsideTree())
            return;

        var origin = _card.Position;
        var originModulate = _card.Modulate;
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(_card, Control.PropertyName.GlobalPosition.ToString(), globalTarget, duration)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenProperty(_card, CanvasItem.PropertyName.Modulate.ToString(), originModulate with { A = 0f }, duration);
        await ToSignal(tween, Tween.SignalName.Finished);
        if (!GodotObject.IsInstanceValid(_card))
            return;

        _card.Position = origin;
        _card.Modulate = originModulate;
    }

    private void OnCardClicked(BaseCardItem item, CardDto card)
    {
        if (_interactable && SlotIndex >= 0)
            Clicked?.Invoke(SlotIndex);
    }

    private static void OnCardLongPressed(BaseCardItem item, CardDto card)
    {
        _ = UIManager.Instance?.OpenAsync(
            new UiId<CardDetailsDlgPayload>(GlobalUiIds.CardDetails),
            new CardDetailsDlgPayload { CardId = card.Id });
    }
}