using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>待出牌态（Run 规格 §14.3）。</summary>
public enum ECombatPendingMode
{
    None,

    /// <summary>需要玩家点选一个合法单位作为目标。</summary>
    PickTarget,

    PickDiscard,

    PickActiveTarget,
}

/// <summary>
/// 战斗界面的纯 UI 态（不进模拟器）：当前操控槽位、控制权判定、待出牌态与播放锁。
/// 与 Godot 无关，便于单测；<see cref="CombatWin"/> 只做读写与刷新。
/// </summary>
public sealed class CombatUiState
{
    private readonly Func<int, bool> _canControl;

    #region 操控与待操作状态

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

    public HandCardChoice? PendingCard { get; private set; }

    private readonly List<HandCardChoice> _discardCards = [];
    public IReadOnlyList<HandCardChoice> DiscardCards => _discardCards.AsReadOnly();
    public int DiscardMinimum { get; private set; }
    public int DiscardMaximum { get; private set; }
    public bool CanConfirmDiscard => PendingMode == ECombatPendingMode.PickDiscard && _discardCards.Count >= DiscardMinimum;

    public void BeginDiscard(int minimum, int maximum)
    {
        ClearPending();
        PendingMode = ECombatPendingMode.PickDiscard;
        DiscardMinimum = minimum;
        DiscardMaximum = maximum;
    }

    public void BeginActiveTarget()
    {
        ClearPending();
        PendingMode = ECombatPendingMode.PickActiveTarget;
    }

    public void ToggleDiscard(HandCardChoice card)
    {
        if (InputLocked || PendingMode != ECombatPendingMode.PickDiscard)
            return;
        if (!_discardCards.Remove(card) && _discardCards.Count < DiscardMaximum)
            _discardCards.Add(card);
    }

    public bool HasPending => PendingMode != ECombatPendingMode.None;

    /// <summary>表现播放期间锁输入（暂停按钮除外）。</summary>
    public bool InputLocked { get; set; }

    internal bool CanSelectHandCard(bool ownerSealed, bool inDiscardPool) =>
        !InputLocked && PendingMode switch
        {
            ECombatPendingMode.PickActiveTarget => false,
            ECombatPendingMode.PickDiscard => inDiscardPool,
            _ => !ownerSealed,
        };

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
    public bool TogglePending(HandCardChoice card, ECombatPendingMode mode)
    {
        if (mode == ECombatPendingMode.None)
            throw new ArgumentOutOfRangeException(nameof(mode));

        if (HasPending && PendingCard == card)
        {
            ClearPending();
            return false;
        }

        PendingMode = mode;
        PendingCard = card;
        return true;
    }

    public void ClearPending()
    {
        PendingMode = ECombatPendingMode.None;
        PendingCard = null;
        _discardCards.Clear();
        DiscardMinimum = DiscardMaximum = 0;
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

    #endregion

    public static CombatUiState CreateLocal() => new(RunConstants.SlotCount, _ => true);
}
