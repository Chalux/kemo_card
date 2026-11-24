using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>Run 规格 §14.3：战斗界面的操控槽 / 控制权 / 待出牌态。</summary>
[TestFixture]
public sealed class CombatUiStateTests
{
    [Test]
    public void Discard_selection_toggles_up_to_limit_and_cancels_without_changing_control()
    {
        var state = new CombatUiState(4, _ => true);
        state.BeginDiscard(0, 2);
        Assert.That(state.CanConfirmDiscard, Is.True);
        state.ToggleDiscard(Card(0));
        state.ToggleDiscard(Card(2));
        state.ToggleDiscard(Card(4));
        Assert.That(state.DiscardCards, Is.EqualTo(new[] { Card(0), Card(2) }));
        state.ToggleDiscard(Card(0));
        Assert.That(state.DiscardCards, Is.EqualTo(new[] { Card(2) }));
        state.ClearPending();
        Assert.That(state.DiscardCards, Is.Empty);
        Assert.That(state.HasPending, Is.False);
        state.BeginDiscard(2, 2);
        state.ToggleDiscard(Card(0));
        Assert.That(state.CanConfirmDiscard, Is.False);
        state.ToggleDiscard(Card(1));
        Assert.That(state.CanConfirmDiscard, Is.True);
        state.TrySelectSlot(1);
        Assert.That(state.HasPending, Is.False);
    }
    [Test]
    public void Initial_controlled_slot_is_first_controllable()
    {
        var state = new CombatUiState(4, slot => slot >= 2);

        Assert.That(state.ControlledSlot, Is.EqualTo(2));
        Assert.That(state.CanControl(0), Is.False);
        Assert.That(state.CanControl(3), Is.True);
        Assert.That(state.CanControl(4), Is.False, "越界槽位不可控");
    }

    [Test]
    public void Select_slot_requires_permission_and_clears_pending()
    {
        var state = new CombatUiState(4, slot => slot != 1);
        state.TogglePending(Card(0), ECombatPendingMode.PickTarget);

        Assert.That(state.TrySelectSlot(1), Is.False, "无权控制");
        Assert.That(state.HasPending, Is.True, "失败的切换不动待出牌态");

        Assert.That(state.TrySelectSlot(2), Is.True);
        Assert.That(state.ControlledSlot, Is.EqualTo(2));
        Assert.That(state.HasPending, Is.False, "切换操控角色清掉待出牌");

        Assert.That(state.TrySelectSlot(2), Is.False, "重复选中同一槽位不算切换");
    }

    [Test]
    public void Input_lock_blocks_slot_switch()
    {
        var state = new CombatUiState(4, _ => true) { InputLocked = true };

        Assert.That(state.TrySelectSlot(3), Is.False);
        Assert.That(state.ControlledSlot, Is.Zero);
    }

    [Test]
    public void Active_target_selection_clears_previous_pending_state_and_can_be_cancelled()
    {
        var state = new CombatUiState(4, _ => true);
        state.BeginDiscard(0, 2);
        state.ToggleDiscard(Card(1));
        state.BeginActiveTarget();
        Assert.That(state.PendingMode, Is.EqualTo(ECombatPendingMode.PickActiveTarget));
        Assert.That(state.PendingCard, Is.Null);
        Assert.That(state.DiscardCards, Is.Empty);
        state.TrySelectSlot(2);
        Assert.That(state.HasPending, Is.False);
    }

    [Test]
    public void Toggle_pending_same_slot_cancels_other_slot_replaces()
    {
        var state = new CombatUiState(4, _ => true);

        Assert.That(state.TogglePending(Card(1), ECombatPendingMode.PickTarget), Is.True);
        Assert.That(state.PendingMode, Is.EqualTo(ECombatPendingMode.PickTarget));
        Assert.That(state.PendingCard, Is.EqualTo(Card(1)));

        Assert.That(state.TogglePending(Card(3), ECombatPendingMode.PickTarget), Is.True, "换一张牌 = 改选");
        Assert.That(state.PendingCard, Is.EqualTo(Card(3)));
        Assert.That(state.PendingMode, Is.EqualTo(ECombatPendingMode.PickTarget));

        Assert.That(state.TogglePending(Card(3), ECombatPendingMode.PickTarget), Is.False, "再点同一张 = 取消");
        Assert.That(state.HasPending, Is.False);
        Assert.That(state.PendingCard, Is.Null);
    }

    [Test]
    public void Local_player_controls_all_party_slots()
    {
        var state = CombatUiState.CreateLocal();

        for (var i = 0; i < RunConstants.SlotCount; i++)
            Assert.That(state.CanControl(i), Is.True);
        Assert.That(state.CanControl(RunConstants.SlotCount), Is.False);
    }

    private static HandCardChoice Card(int index) => new(0, index.ToString());

    [Test]
    public void Sealed_allies_hand_cards_are_selectable_for_discard_only_in_the_target_pool()
    {
        var state = CombatUiState.CreateLocal();
        Assert.That(state.CanSelectHandCard(ownerSealed: true, inDiscardPool: true), Is.False);
        state.BeginDiscard(2, 2);
        Assert.That(state.CanSelectHandCard(ownerSealed: true, inDiscardPool: true), Is.True);
        Assert.That(state.CanSelectHandCard(ownerSealed: true, inDiscardPool: false), Is.False);
        state.InputLocked = true;
        Assert.That(state.CanSelectHandCard(ownerSealed: true, inDiscardPool: true), Is.False);
    }
}
