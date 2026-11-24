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

/// <summary>公共手牌中的卡牌展示和交互。</summary>
public partial class HandSlotCmp : BaseCmp
{
    [Export] private BaseCardItem? _card;
    [Export] private Label? _owner;
    [Export] private Panel? _pendingFrame;

    /// <summary>点击回调，参数为槽位索引。</summary>
    public Action<int>? Clicked { get; set; }

    public int SlotIndex { get; private set; } = -1;
    public int CharacterIndex { get; private set; } = -1;
    public BaseCardItem? Card => _card;
    public string? RuntimeInstanceId { get; private set; }
    public bool HasCard { get; private set; }

    private bool _interactable;
    private Vector2 _home;
    private float _angle;
    private bool _dragging;
    private bool _hovered;
    private Tween? _returnTween;
    private Tween? _liftTween;

    #region 扇形牌面

    public void SetFanPose(Vector2 position, float angle)
    {
        _home = position;
        _angle = angle;
        PivotOffset = Size * 0.5f;
        if (_dragging || _returnTween?.IsRunning() == true)
            return;
        Position = position;
        Rotation = angle;
        UpdateLift(false);
    }

    private void UpdateLift(bool animate)
    {
        var lifted = _hovered || _dragging;
        var offset = lifted ? new Vector2(0f, -22f).Rotated(-Rotation) : Vector2.Zero;
        var rotation = lifted ? -Rotation : 0f;
        var scale = Vector2.One * (lifted ? 1.12f : 1f);
        ZIndex = lifted ? 2 : 0;
        _liftTween?.Kill();
        if (!animate)
        {
            OffsetTransformPosition = offset;
            OffsetTransformRotation = rotation;
            OffsetTransformScale = scale;
            return;
        }
        _liftTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _liftTween.TweenProperty(this, PropertyName.OffsetTransformPosition.ToString(), offset, 0.1f);
        _liftTween.TweenProperty(this, PropertyName.OffsetTransformRotation.ToString(), rotation, 0.1f);
        _liftTween.TweenProperty(this, PropertyName.OffsetTransformScale.ToString(), scale, 0.1f);
    }

    public void BeginDrag()
    {
        _returnTween?.Kill();
        _dragging = true;
        Position = _home;
        Rotation = _angle;
        UpdateLift(false);
    }

    public void FollowPointer(Vector2 canvasPoint)
    {
        Rotation = 0f;
        OffsetTransformRotation = 0f;
        OffsetTransformPosition = Vector2.Zero;
        Position = GetParent<Control>().GetGlobalTransformWithCanvas().AffineInverse() * canvasPoint - PivotOffset;
    }

    public void EndDrag()
    {
        _dragging = false;
        _hovered = false;
        UpdateLift(true);
        // 每帧读取当前扇形落点，回弹期间抽牌或改变窗口尺寸也能归位。
        var from = Position;
        var angle = Rotation;
        _returnTween = CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _returnTween.TweenMethod(Callable.From<float>(t =>
        {
            Position = from.Lerp(_home, t);
            Rotation = Mathf.LerpAngle(angle, _angle, t);
        }), 0f, 1f, 0.16f);
    }

    #endregion

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
        _card.Hovered = (_, card) =>
        {
            _hovered = card is not null;
            if (!_dragging)
                UpdateLift(true);
        };
    }

    protected override void OnExitTree()
    {
        if (_card == null)
            return;

        _card.Clicked = null;
        _card.LongPressed = null;
        _card.Hovered = null;
        _hovered = _dragging = false;
        _liftTween?.Kill();
        _returnTween?.Kill();
    }

    public void Clear() => Bind(-1, -1, null, null, false, false);

    public void Bind(int characterIndex, int slotIndex, string? runtimeInstanceId, CardDto? card, bool pending, bool interactable, string ownerName = "")
    {
        if (RuntimeInstanceId != runtimeInstanceId)
        {
            _dragging = _hovered = false;
            _returnTween?.Kill();
            SetFanPose(_home, _angle);
        }
        CharacterIndex = characterIndex;
        SlotIndex = slotIndex;
        _interactable = interactable;
        RuntimeInstanceId = runtimeInstanceId;
        HasCard = card is not null && runtimeInstanceId is not null;
        Visible = HasCard;
        if (_owner is not null)
            _owner.Text = ownerName;

        if (_card != null)
        {
            _card.Visible = HasCard;
            _card.SetData(HasCard ? card : null);
            _card.Modulate = _card.Modulate with { A = interactable || !HasCard ? 1f : 0.7f };
        }

        SetPending(pending);
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