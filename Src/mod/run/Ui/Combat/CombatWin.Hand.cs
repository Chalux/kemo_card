using Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Global.Ui.Themes;

namespace KemoCard.Mod.Run.Ui.CombatUi;

public partial class CombatWin
{
    private sealed record CardDrag(HandSlotCmp Hand, int SlotIndex, HandCardChoice Choice, CardDto Card, bool Aimed);
    private CardDrag? _drag;
    private Vector2 _aimPoint;
    private bool _dropValid;

    #region 手牌拖放

    private Variant BeginCardDrag(HandSlotCmp hand, int owner, CardDto card)
    {
        var simulation = Simulation;
        if (_dropSurface is null || _ui is null || simulation?.Phase != ECombatPhase.Player ||
            _ui.PendingMode is ECombatPendingMode.PickDiscard or ECombatPendingMode.PickActiveTarget ||
            !_ui.CanControl(owner) || !CombatCommandValidator.TryGetCharacter(simulation, owner, out var character, out _) ||
            !_ui.CanSelectHandCard(character.IsSealed, false) || hand.RuntimeInstanceId is not { } id ||
            character.HandSlots[hand.SlotIndex].RuntimeInstanceId != id)
            return default;

        var choice = new HandCardChoice(owner, id);
        _ui.TrySelectSlot(owner);
        var aimed = CombatTargeting.RequiresExplicitTarget(card);
        if (aimed)
            _ui.TogglePending(choice, ECombatPendingMode.PickTarget);
        SyncFromState();
        _drag = new CardDrag(hand, hand.SlotIndex, choice, card, aimed);
        hand.BeginDrag();
        _aimPoint = _dropSurface.GetLocalMousePosition();
        _dropValid = false;
        _dropSurface.Show();
        return id;
    }

    private bool PreviewDrop(Vector2 point, Variant data)
    {
        if (_drag is not { } drag || _dropSurface is null)
            return false;
        var canvasPoint = _dropSurface.GetGlobalTransformWithCanvas() * point;
        if (!drag.Aimed)
            drag.Hand.FollowPointer(canvasPoint);
        _aimPoint = point;
        _dropValid = TryResolveDrop(canvasPoint, data, out _);
        _dropSurface.QueueRedraw();
        return _dropValid;
    }

    private bool TryResolveDrop(Vector2 canvasPoint, Variant data, out CombatTargetRef? target)
    {
        target = null;
        var simulation = Simulation;
        if (_drag is not { } drag || data.VariantType != Variant.Type.String ||
            data.AsString() != drag.Choice.RuntimeInstanceId || simulation?.Phase != ECombatPhase.Player ||
            _ui is not { InputLocked: false } || !ContainsPoint(_battlefield, canvasPoint))
            return false;

        var owner = drag.Choice.CharacterIndex;
        var character = simulation.PlayerTeam.Characters[owner];
        if (character.IsSealed || drag.Hand.RuntimeInstanceId != drag.Choice.RuntimeInstanceId ||
            character.HandSlots[drag.SlotIndex].RuntimeInstanceId != drag.Choice.RuntimeInstanceId)
            return false;
        if (!drag.Aimed)
            return true;

        foreach (var ally in _allies)
            if (ContainsPoint(ally, canvasPoint))
                target = new CombatTargetRef(ECombatSide.Player, ally.SlotIndex);
        foreach (var enemy in _enemies)
            if (ContainsPoint(enemy, canvasPoint))
                target = new CombatTargetRef(ECombatSide.Enemy, enemy.EnemyIndex);
        return target is { } unit && CombatTargeting.IsLegalTarget(simulation, drag.Card, owner, unit);
    }

    private static bool ContainsPoint(Control? control, Vector2 canvasPoint) =>
        control is not null && control.IsVisibleInTree() &&
        new Rect2(Vector2.Zero, control.Size).HasPoint(control.GetGlobalTransformWithCanvas().AffineInverse() * canvasPoint);

    private void DropCard(Vector2 point, Variant data)
    {
        if (_drag is not { } drag || _dropSurface is null ||
            !TryResolveDrop(_dropSurface.GetGlobalTransformWithCanvas() * point, data, out var target))
            return;

        // 成功时保留释放位置，交给结算表现从这里把原牌飞向墓地。
        FinishCardDrag(returnToHand: false);
        if (!ApplyCommand(new PlayCardCommand(drag.Choice.CharacterIndex, drag.SlotIndex,
            target is { } unit ? [unit] : [], drag.Choice.RuntimeInstanceId)))
            drag.Hand.EndDrag();
    }

    private void FinishCardDrag(bool returnToHand = true)
    {
        if (_drag is not { } drag)
            return;
        _drag = null;
        _dropSurface?.Hide();
        if (returnToHand)
            drag.Hand.EndDrag();
        _ui?.ClearPending();
    }

    private void CancelCardDrag()
    {
        if (_drag is null)
            return;
        GetViewport().GuiCancelDrag();
        // 引擎通常同步发 DragEnd；窗口失焦时也可能已先清掉原生拖放。
        if (_drag is not null)
        {
            FinishCardDrag();
            SyncFromState();
        }
    }

    public override void _Notification(int what)
    {
        if (_drag is null)
            return;
        if (what == NotificationDragEnd)
        {
            FinishCardDrag();
            SyncFromState();
        }
        else if (what == NotificationWMWindowFocusOut || (what == NotificationVisibilityChanged && !IsVisibleInTree()))
            CancelCardDrag();
    }

    public override void _Input(InputEvent @event)
    {
        if (_drag is null || !(@event.IsActionPressed("ui_cancel") ||
            @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }))
            return;
        GetViewport().SetInputAsHandled();
        CancelCardDrag();
    }

    private void DrawCardAim()
    {
        if (_drag is not { } drag || _dropSurface is not { } surface || _battlefield is null)
            return;
        var toLocal = surface.GetGlobalTransformWithCanvas().AffineInverse();
        var color = _dropValid ? KemoPalette.Accent : KemoPalette.TextSecondary;
        if (!drag.Aimed)
        {
            var rect = (toLocal * _battlefield.GetGlobalTransformWithCanvas()) * new Rect2(Vector2.Zero, _battlefield.Size);
            surface.DrawRect(rect, color with { A = _dropValid ? 0.8f : 0.35f }, false, 2f);
            return;
        }

        var start = toLocal * drag.Hand.GetGlobalTransformWithCanvas() * new Vector2(drag.Hand.Size.X * 0.5f, 0f);
        var bend = Mathf.Clamp(start.DistanceTo(_aimPoint) * 0.36f, 28f, 105f);
        var control = start + Vector2.Up * bend;
        var direction = control.DirectionTo(_aimPoint);
        if (direction.IsZeroApprox())
            direction = Vector2.Up;
        var basePoint = _aimPoint - direction * 13f;
        var dots = Mathf.Clamp((int)(start.DistanceTo(basePoint) / 13f), 3, 60);
        for (var i = 0; i < dots; i++)
        {
            var t = (float)i / dots;
            var center = start.Lerp(control, t).Lerp(control.Lerp(basePoint, t), t);
            surface.DrawCircle(center, Mathf.Lerp(2f, 4f, t), color, antialiased: true);
        }
        var wing = direction.Orthogonal() * 7f;
        surface.DrawColoredPolygon([_aimPoint, basePoint + wing, basePoint - wing], color);
    }

    #endregion
}