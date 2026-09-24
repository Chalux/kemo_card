using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>Run 规格 §14.3：战斗界面的操控槽 / 控制权 / 待出牌态。</summary>
[TestFixture]
public sealed class CombatUiStateTests
{
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
        state.TogglePending(0, ECombatPendingMode.PickTarget);

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
    public void Toggle_pending_same_slot_cancels_other_slot_replaces()
    {
        var state = new CombatUiState(4, _ => true);

        Assert.That(state.TogglePending(1, ECombatPendingMode.ConfirmPlay), Is.True);
        Assert.That(state.PendingMode, Is.EqualTo(ECombatPendingMode.ConfirmPlay));
        Assert.That(state.PendingHandSlot, Is.EqualTo(1));

        Assert.That(state.TogglePending(3, ECombatPendingMode.PickTarget), Is.True, "换一张牌 = 改选");
        Assert.That(state.PendingHandSlot, Is.EqualTo(3));
        Assert.That(state.PendingMode, Is.EqualTo(ECombatPendingMode.PickTarget));

        Assert.That(state.TogglePending(3, ECombatPendingMode.PickTarget), Is.False, "再点同一张 = 取消");
        Assert.That(state.HasPending, Is.False);
        Assert.That(state.PendingHandSlot, Is.EqualTo(-1));
    }

    [Test]
    public void FromRun_single_player_controls_all_slots()
    {
        var run = new RunMod();
        run.AddPlayerController(new PlayerController("local", "Player", isOwner: true));
        for (var i = 0; i < RunConstants.SlotCount; i++)
            run.AssignSlotInternal(i, "local");

        var state = CombatUiState.FromRun(run);

        Assert.That(CombatUiState.ResolveLocalPlayerId(run), Is.EqualTo("local"));
        for (var i = 0; i < RunConstants.SlotCount; i++)
            Assert.That(state.CanControl(i), Is.True, $"槽位 {i}");
    }

    [Test]
    public void FromRun_only_slots_owned_by_local_player_are_controllable()
    {
        var run = new RunMod();
        run.AddPlayerController(new PlayerController("host", "Host", isOwner: true));
        run.AddPlayerController(new PlayerController("guest", "Guest", isOwner: false));
        run.AssignSlotInternal(0, "host");
        run.AssignSlotInternal(1, "guest");
        run.AssignSlotInternal(2, "guest");
        run.AssignSlotInternal(3, "host");

        var state = CombatUiState.FromRun(run);

        Assert.That(state.CanControl(0), Is.True);
        Assert.That(state.CanControl(1), Is.False);
        Assert.That(state.CanControl(2), Is.False);
        Assert.That(state.CanControl(3), Is.True);
        Assert.That(state.ControlledSlot, Is.Zero);
    }
}