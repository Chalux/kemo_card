namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>待出牌态（Run 规格 §14.3）。</summary>
public enum ECombatPendingMode
{
    None,

    /// <summary>目标可自动解析，等待玩家点「确认出牌」。</summary>
    ConfirmPlay,

    /// <summary>需要玩家点选一个合法单位作为目标。</summary>
    PickTarget,
}

/// <summary>
/// 战斗界面的纯 UI 态（不进模拟器）：当前操控槽位、控制权判定、待出牌态与播放锁。
/// 与 Godot 无关，便于单测；<see cref="CombatWin"/> 只做读写与刷新。
/// </summary>
public sealed class CombatUiState
{
    private readonly Func<int, bool> _canControl;

    public CombatUiState(int slotCount, Func<int, bool> canControl)
    {
        if (slotCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        ArgumentNullException.ThrowIfNull(canControl);
        SlotCount = slotCount;
        _canControl = canControl;
        ControlledSlot = FirstControllableSlot();
    }

    public int SlotCount { get; }

    /// <summary>当前正在操控的槽位（0 起）；无任何可控槽位时为 0（界面全部禁用）。</summary>
    public int ControlledSlot { get; private set; }

    public ECombatPendingMode PendingMode { get; private set; }

    /// <summary>待出牌的手牌槽索引；无待出牌时为 -1。</summary>
    public int PendingHandSlot { get; private set; } = -1;

    public bool HasPending => PendingMode != ECombatPendingMode.None;

    /// <summary>表现播放期间锁输入（暂停按钮除外）。</summary>
    public bool InputLocked { get; set; }

    /// <summary>本地玩家是否有权控制该槽位。</summary>
    public bool CanControl(int slot) => slot >= 0 && slot < SlotCount && _canControl(slot);

    /// <summary>
    /// 切换操控槽位：需有权控制且未锁输入；成功时清掉待出牌态。返回是否真的发生了切换。
    /// </summary>
    public bool TrySelectSlot(int slot)
    {
        if (InputLocked || !CanControl(slot))
            return false;

        ClearPending();
        if (ControlledSlot == slot)
            return false;

        ControlledSlot = slot;
        return true;
    }

    /// <summary>
    /// 点手牌的切换语义：同一张再点一次 = 取消；否则进入新的待出牌态。返回当前是否处于待出牌。
    /// </summary>
    public bool TogglePending(int handSlot, ECombatPendingMode mode)
    {
        if (mode == ECombatPendingMode.None)
            throw new ArgumentOutOfRangeException(nameof(mode));

        if (HasPending && PendingHandSlot == handSlot)
        {
            ClearPending();
            return false;
        }

        PendingMode = mode;
        PendingHandSlot = handSlot;
        return true;
    }

    public void ClearPending()
    {
        PendingMode = ECombatPendingMode.None;
        PendingHandSlot = -1;
    }

    private int FirstControllableSlot()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            if (_canControl(i))
                return i;
        }

        return 0;
    }

    #region 与 Run 的接线

    /// <summary>
    /// 本地玩家 id：当前里程碑是单人，本地玩家即房主（<c>IsOwner</c>）。
    /// 联机接入后应改为读本地会话身份（Run 规格 §14.5 后置项）。
    /// </summary>
    public static string? ResolveLocalPlayerId(RunMod run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.PlayerControllers.FirstOrDefault(controller => controller.IsOwner)?.PlayerId;
    }

    /// <summary>按 Run 的槽位控制权表构造：有权 = 该槽位归属本地玩家。</summary>
    public static CombatUiState FromRun(RunMod run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var localId = ResolveLocalPlayerId(run);
        return new CombatUiState(RunConstants.SlotCount, slot =>
            localId is not null &&
            run.SlotOwnership.TryGetValue(slot, out var owner) &&
            string.Equals(owner, localId, StringComparison.Ordinal));
    }

    #endregion
}