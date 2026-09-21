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

public sealed record PotentialUnlockResult(bool Success, string? Error = null)
{
    public static PotentialUnlockResult Ok { get; } = new(true);
}

/// <summary>
/// 团体潜能：全队共享池 + 槽位记账（直充余额 + 消费流水）。
/// 消费 = 解锁角色被动（阈值即成本）；返还 = 逐笔退回并重新锁定被动；
/// 消费顺序先扣本槽位直充（无需表决）再扣团队池（表决模式下需提议通过）。
/// </summary>
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

    public static bool IsPassiveUnlocked(RunMod model, CharacterInstance character, PassiveRefDto passive)
    {
        if (passive.RequiredPotential <= 0)
            return true;

        return model.PlayerStates.Any(state => state.PotentialSpent.Any(entry =>
            string.Equals(entry.CharacterInstanceId, character.InstanceId, StringComparison.Ordinal) &&
            string.Equals(entry.BuffId, passive.BuffId, StringComparison.Ordinal)));
    }

    /// <summary>
    /// 消费潜能解锁一条被动。直充部分无需表决；团队池部分在表决模式下先过提议
    /// （每环每槽位提议次数受策略限制），再扣账。
    /// </summary>
    public PotentialUnlockResult TryUnlock(int slotIndex, CharacterInstance character, PassiveRefDto passive)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(passive);

        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return new PotentialUnlockResult(false, "槽位索引无效。");
        if (passive.RequiredPotential <= 0)
            return PotentialUnlockResult.Ok;
        if (IsPassiveUnlocked(_model, character, passive))
            return PotentialUnlockResult.Ok;

        var state = _model.PlayerStates[slotIndex];
        var cost = passive.RequiredPotential;
        var creditPart = Math.Min(state.PotentialDirectCredit, cost);
        var poolPart = cost - creditPart;
        if (_model.TeamPotentialPool < poolPart)
            return new PotentialUnlockResult(false, $"潜能不足：需要 {cost}（直充 {creditPart} + 池 {_model.TeamPotentialPool}）。");

        var policy = _policyProvider();
        if (poolPart > 0 && policy.RequireVote && !RequestProposal(slotIndex, policy, character, passive))
            return new PotentialUnlockResult(false, "表决未通过或本环提议次数已用尽。");

        if (creditPart > 0)
        {
            state.ConsumePotentialDirectCredit(creditPart);
            state.AddPotentialSpendEntry(new PotentialSpendEntryDto
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Source = "credit",
                Amount = creditPart,
                CharacterInstanceId = character.InstanceId,
                BuffId = passive.BuffId,
            });
        }

        if (poolPart > 0)
        {
            _model.TeamPotentialPool -= poolPart;
            state.AddPotentialSpendEntry(new PotentialSpendEntryDto
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Source = "pool",
                Amount = poolPart,
                CharacterInstanceId = character.InstanceId,
                BuffId = passive.BuffId,
            });
        }

        return PotentialUnlockResult.Ok;
    }

    /// <summary>
    /// 返还一笔消费：解锁判定按「存在匹配流水」，因此<b>整笔消费的全部流水</b>（直充 + 池拆账）
    /// 必须原子退回，否则只退一部分就能在保留解锁的情况下回收额度（打折解锁漏洞）。
    /// 每笔按原来源退回（直充回槽位、池回团队池），对应被动自动重新锁定。
    /// </summary>
    /// <returns>实际返还总额；流水不存在时为 0。</returns>
    public int Refund(int slotIndex, string entryId)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return 0;

        var state = _model.PlayerStates[slotIndex];
        var entry = state.PotentialSpent.FirstOrDefault(entry =>
            string.Equals(entry.EntryId, entryId, StringComparison.Ordinal));
        if (entry is null)
            return 0;

        // 同一次解锁可能拆成 credit + pool 两笔：按 (角色实例, 被动) 整组退回。
        var purchase = state.PotentialSpent
            .Where(candidate =>
                string.Equals(candidate.CharacterInstanceId, entry.CharacterInstanceId, StringComparison.Ordinal) &&
                string.Equals(candidate.BuffId, entry.BuffId, StringComparison.Ordinal))
            .ToList();

        var refunded = 0;
        foreach (var item in purchase)
        {
            if (!state.RemovePotentialSpendEntry(item.EntryId))
                continue;

            refunded += item.Amount;
            if (string.Equals(item.Source, "credit", StringComparison.Ordinal))
                state.RestorePotentialDirectCredit(item.Amount);
            else
                _model.TeamPotentialPool += item.Amount;
        }

        return refunded;
    }

    /// <summary>
    /// 重复获得角色 +20：重复的是该槽位自己已有的角色 → 直充本槽位；否则入团队池。
    /// </summary>
    public void GrantDuplicateReward(int? slotIndex = null) => Grant(DuplicateReward, slotIndex);

    /// <summary>任意数额入账（调试通道与后置的奖励管线共用）：目标槽位有效时直充该槽位，否则入团队池。</summary>
    public void Grant(int amount, int? slotIndex = null)
    {
        if (amount <= 0)
            return;

        if (slotIndex is >= 0 and < RunConstants.SlotCount)
            _model.PlayerStates[slotIndex.Value].AddPotentialDirectCredit(amount);
        else
            _model.TeamPotentialPool += amount;
    }

    /// <summary>每环开始重置提议计数（环间调用；运行态不落盘，重开 Run 视为新的一环）。</summary>
    public void ResetRingProposalCounters() => Array.Clear(_proposalsThisRing);

    public int ProposalsUsedThisRing(int slotIndex) =>
        slotIndex >= 0 && slotIndex < _proposalsThisRing.Length ? _proposalsThisRing[slotIndex] : 0;

    private bool RequestProposal(
        int slotIndex,
        PotentialPolicySettings policy,
        CharacterInstance character,
        PassiveRefDto passive)
    {
        if (!policy.UnlimitedProposals &&
            _proposalsThisRing[slotIndex] >= Math.Max(0, policy.ProposalsPerRing))
            return false;

        _proposalsThisRing[slotIndex]++;
        return _approver.RequestApproval(
            slotIndex,
            $"解锁 {character.DefinitionId} 的被动 {passive.BuffId}（成本 {passive.RequiredPotential}）");
    }
}
