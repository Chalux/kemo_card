using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run.Potential;

/// <summary>潜能消费策略设置：读全局联机设置（房主配置，房主同样受约束）。</summary>
public sealed record PotentialPolicySettings(
    bool RequireVote,
    int ProposalsPerRing,
    bool UnlimitedProposals)
{
    public static PotentialPolicySettings Default { get; } = new(
        RequireVote: false,
        ProposalsPerRing: 2,
        UnlimitedProposals: false);
}

/// <summary>
/// 表决通道：联机实现走网络提议 + 团队表决（3 人同意含提议者）；
/// 单机实现直接放行（策略为自由消费时不会走到这里）。
/// </summary>
public interface IPotentialProposalApprover
{
    bool RequestApproval(int slotIndex, string description);
}

/// <summary>单机实现：直接同意。</summary>
public sealed class SinglePlayerPotentialApprover : IPotentialProposalApprover
{
    public static readonly SinglePlayerPotentialApprover Instance = new();

    public bool RequestApproval(int slotIndex, string description) => true;
}

/// <summary>划拨失败原因：界面据此选本地化键，调试面板据此打中文日志。</summary>
public enum EPotentialFailure
{
    None,
    InvalidSlot,
    InvalidAmount,
    PoolShort,
    SlotShort,
    VoteRejected,
}

public readonly record struct PotentialTransferResult(
    bool Success,
    int Amount,
    EPotentialFailure Failure,
    string Detail)
{
    public static PotentialTransferResult Ok(int amount) => new(true, amount, EPotentialFailure.None, "");

    public static PotentialTransferResult Fail(EPotentialFailure failure, string detail) =>
        new(false, 0, failure, detail);
}

/// <summary>
/// 团体潜能（2026-09-26 起为**进度值**模型）：全队共享池 + 槽位「已分配潜能」。
/// 槽位上阵角色的被动按「已分配 ≥ 门槛」**自动解锁**，扣回低于门槛即重新锁定；
/// 分配 = 团队池 → 槽位已分配（投票模式下需表决），扣除 = 槽位已分配 → 团队池（无需表决）。
/// </summary>
/// <remarks>
/// 旧「消费流水」模型（解锁显式花潜能、流水可返还）已废止：`PotentialSpent` 只为老档
/// 反序列化保留，运行态不再读写。槽位账本仍按**玩家槽位**记（切换角色不丢失数据）。
/// </remarks>
public sealed class PotentialService
{
    /// <summary>重复获得角色转化的潜能额度。</summary>
    public const int DuplicateReward = 20;

    private readonly RunMod _model;
    private readonly Func<PotentialPolicySettings> _policyProvider;
    private readonly IPotentialProposalApprover _approver;
    private readonly int[] _proposalsThisRing = new int[RunConstants.SlotCount];

    public PotentialService(
        RunMod model,
        Func<PotentialPolicySettings>? policyProvider = null,
        IPotentialProposalApprover? approver = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _policyProvider = policyProvider ?? (() => PotentialPolicySettings.Default);
        _approver = approver ?? SinglePlayerPotentialApprover.Instance;
    }

    public int TeamPool => _model.TeamPotentialPool;

    /// <summary>槽位已分配潜能（进度值，不消费）；槽位越界返回 0。</summary>
    public int AllocatedFor(int slotIndex) =>
        slotIndex >= 0 && slotIndex < RunConstants.SlotCount
            ? _model.PlayerStates[slotIndex].AllocatedPotential
            : 0;

    /// <summary>角色当前所在槽位（未上阵为 null）。</summary>
    public static int? FindAssignedSlot(RunMod model, CharacterInstance character)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(character);

        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            if (model.PlayerStates[i].ActiveCharacter is { } active &&
                string.Equals(active.InstanceId, character.InstanceId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// 解锁判定：门槛 ≤ 0 恒解锁；否则**该角色所在槽位的已分配潜能 ≥ 门槛**。
    /// 未上阵的角色没有槽位（视为 0），只有门槛 0 的被动解锁。
    /// </summary>
    public static bool IsPassiveUnlocked(RunMod model, CharacterInstance character, PassiveRefDto passive)
    {
        ArgumentNullException.ThrowIfNull(passive);

        if (passive.RequiredPotential <= 0)
            return true;

        return FindAssignedSlot(model, character) is { } slot &&
            model.PlayerStates[slot].AllocatedPotential >= passive.RequiredPotential;
    }

    /// <summary>把 <paramref name="amount"/> 点潜能从团队池分配到槽位（投票模式下需表决）。</summary>
    public PotentialTransferResult TryAllocate(int slotIndex, int amount)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return PotentialTransferResult.Fail(EPotentialFailure.InvalidSlot, $"槽位 {slotIndex} 越界。");
        if (amount <= 0)
            return PotentialTransferResult.Fail(EPotentialFailure.InvalidAmount, "潜能数量必须为正数。");
        if (_model.TeamPotentialPool < amount)
            return PotentialTransferResult.Fail(
                EPotentialFailure.PoolShort,
                $"可用潜能不足（团队池 {_model.TeamPotentialPool}，需要 {amount}）。");

        var policy = _policyProvider();
        if (policy.RequireVote && !RequestProposal(slotIndex, policy, amount))
            return PotentialTransferResult.Fail(
                EPotentialFailure.VoteRejected,
                "表决未通过或本环提议次数已用尽。");

        _model.TeamPotentialPool -= amount;
        _model.PlayerStates[slotIndex].AllocatePotential(amount);
        return PotentialTransferResult.Ok(amount);
    }

    /// <summary>从槽位扣除 <paramref name="amount"/> 点已分配潜能退回团队池（无需表决）。</summary>
    public PotentialTransferResult TryDeduct(int slotIndex, int amount)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return PotentialTransferResult.Fail(EPotentialFailure.InvalidSlot, $"槽位 {slotIndex} 越界。");
        if (amount <= 0)
            return PotentialTransferResult.Fail(EPotentialFailure.InvalidAmount, "潜能数量必须为正数。");

        var state = _model.PlayerStates[slotIndex];
        if (state.AllocatedPotential < amount)
            return PotentialTransferResult.Fail(
                EPotentialFailure.SlotShort,
                $"该槽位已分配潜能不足（现有 {state.AllocatedPotential}，需要 {amount}）。");

        state.DeductPotential(amount);
        _model.TeamPotentialPool += amount;
        return PotentialTransferResult.Ok(amount);
    }

    /// <summary>
    /// 重复获得角色 +20：重复的是该槽位自己已有的角色 → 直接分配到该槽位；否则入团队池。
    /// </summary>
    public void GrantDuplicateReward(int? slotIndex = null) => Grant(DuplicateReward, slotIndex);

    /// <summary>任意数额入账（调试通道与后置的奖励管线共用）：目标槽位有效时直接分配到该槽位，否则入团队池。</summary>
    public void Grant(int amount, int? slotIndex = null)
    {
        if (amount <= 0)
            return;

        if (slotIndex is >= 0 and < RunConstants.SlotCount)
            _model.PlayerStates[slotIndex.Value].AllocatePotential(amount);
        else
            _model.TeamPotentialPool += amount;
    }

    /// <summary>每环开始重置提议计数（环间调用；运行态不落盘，重开 Run 视为新的一环）。</summary>
    public void ResetRingProposalCounters() => Array.Clear(_proposalsThisRing);

    public int ProposalsUsedThisRing(int slotIndex) =>
        slotIndex >= 0 && slotIndex < _proposalsThisRing.Length ? _proposalsThisRing[slotIndex] : 0;

    private bool RequestProposal(int slotIndex, PotentialPolicySettings policy, int amount)
    {
        if (!policy.UnlimitedProposals &&
            _proposalsThisRing[slotIndex] >= Math.Max(0, policy.ProposalsPerRing))
            return false;

        _proposalsThisRing[slotIndex]++;
        return _approver.RequestApproval(
            slotIndex,
            $"把 {amount} 点潜能分配到槽位 {slotIndex + 1}");
    }
}
