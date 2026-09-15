using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class PlayerTeamState
{
    private readonly CharacterBattleInstance[] _characters;

    public AbilitySystemComponent Asc { get; }
    public int SharedHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.Health));
    public int MaxHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.MaxHealth));
    public CombatDomain? ActiveDomain { get; set; }
    public IReadOnlyList<CharacterBattleInstance> Characters => _characters;

    /// <summary>
    /// 存活判定必须用未取整的账本值：<see cref="SharedHp"/> 走 <c>MathF.Round</c>（银行家舍入），
    /// 账本剩 0.5 时会被舍入为 0，导致队伍在还有血量时被判负。
    /// </summary>
    public bool IsDefeated => SharedHpExact <= 0f;

    /// <summary>
    /// BattleStart 通道约束（规格 §1.2）：被动/修饰技能禁止读写 SharedHp。
    /// 置位期间伤害与治疗一律无操作，仅累加 <see cref="BlockedSharedHpWriteCount"/>。
    /// </summary>
    public bool SharedHpLocked { get; set; }

    /// <summary>写锁期间被拦截的 SharedHp 写入次数，供诊断与测试断言。</summary>
    public int BlockedSharedHpWriteCount { get; private set; }

    /// <summary>
    /// 规格 §1.3：治疗只能回队伍账本。点名玩家槽位的治疗被软失败丢弃时累加，供诊断与测试断言。
    /// </summary>
    public int RejectedSlotHealCount { get; private set; }

    /// <summary>账本的未取整值。分槽结算需要它做工作血量基准，避免每槽都吃一次取整误差。</summary>
    internal float SharedHpExact => Asc.GetCurrentValue(AttributeIds.Health);

    public PlayerTeamState(
        IReadOnlyList<CharacterBattleInstance> characters,
        int sharedMaxHp,
        AbilitySystemComponent? teamAsc = null)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (characters.Count == 0)
            throw new ArgumentException("至少一名角色。", nameof(characters));
        if (sharedMaxHp <= 0)
            throw new ArgumentOutOfRangeException(nameof(sharedMaxHp));
        _characters = characters.ToArray();
        Asc = teamAsc ?? new CombatAscFactory().CreateTeamAsc(
            new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
            sharedMaxHp);
    }

    /// <summary>
    /// 唯一的账本扣血入口（规格 §1.2）。取 <see cref="float"/> 是为了让分槽结算保留 GAS 公式算出的小数，
    /// 多槽 AoE 才不会每槽都吃一次取整误差。
    /// </summary>
    public void ApplySharedDamage(float amount)
    {
        if (amount <= 0f)
            return;
        if (SharedHpLocked)
        {
            BlockedSharedHpWriteCount++;
            return;
        }

        if (IsDefeated)
            return;
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, SharedHpExact - amount));
    }

    public void HealShared(float amount)
    {
        if (amount <= 0f)
            return;
        if (SharedHpLocked)
        {
            BlockedSharedHpWriteCount++;
            return;
        }

        if (IsDefeated)
            return;
        Asc.Attributes.SetCurrentValue(
            AttributeIds.Health,
            MathF.Min(Asc.GetCurrentValue(AttributeIds.MaxHealth), SharedHpExact + amount));
    }

    /// <summary>规格 §1.3：点名玩家槽位的治疗软失败，只记一次诊断计数。</summary>
    internal void CountRejectedSlotHeal() => RejectedSlotHealCount++;

    /// <summary>
    /// 规格 §1.2 第 3 步：按当时 <see cref="MaxHp"/> 冻结账本并补满一次。
    /// 这是唯一合法的补满入口，不受 <see cref="SharedHpLocked"/> 影响。
    /// </summary>
    public void FreezeAndFillSharedHp() =>
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, Asc.GetCurrentValue(AttributeIds.MaxHealth));
}