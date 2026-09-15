using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Run.Reward;

public enum ERewardType
{
    Character,
    Card,
    Gold,
    Modifier,
    Heal,
}

public readonly struct RewardOptionDto
{
    public ERewardType Type { get; init; }

    /// <summary>
    /// 显示名翻译键（新增键写入 <c>Resource/Locale/strings.csv</c>）；
    /// UI 侧用 <c>Localization.Tr(DisplayNameKey)</c> 取文案，不在此处拼可见文案。
    /// </summary>
    /// <remarks>
    /// 键内可含 <c>{0}</c> 占位符（与 <c>UI_ALERT_DISPLAY_DESC</c> 同约定），
    /// 由 UI 用 <c>string.Format(Localization.Tr(key), Amount)</c> 填入 <see cref="Amount"/>。
    /// 注意：<c>Resource/Locale/strings.*.translation</c> 是 Godot 编译出的二进制
    /// <c>OptimizedTranslation</c> 资源，不能在代码里改；新增键只写 strings.csv，
    /// 需要由 Godot 重新导入生成 translation 后才能在运行时生效。
    /// </remarks>
    public string DisplayNameKey { get; init; }

    /// <summary>
    /// 该奖励的数值（当前仅 Gold 使用，表示金币数量），供 UI 填入翻译键占位符。
    /// </summary>
    public int Amount { get; init; }

    public object? Data { get; init; }
}

public readonly struct RewardOptionsDto
{
    public RewardOptionDto[] Options { get; init; }
}

public readonly struct PerPlayerRewardSet
{
    public Dictionary<int, RewardOptionsDto> Rewards { get; init; }
}

public sealed class RunRewardDistributor
{
    /// <summary>
    /// 生成索引出的金币奖励数量下限（含）。
    /// </summary>
    private const int GoldAmountMin = 10;

    /// <summary>
    /// 生成索引出的金币奖励数量上限（不含）。
    /// </summary>
    private const int GoldAmountMaxExclusive = 51;

    /// <summary>
    /// 金币奖励的显示名翻译键。
    /// </summary>
    private const string GoldRewardNameKey = "UI_REWARD_GOLD_NAME";

    /// <summary>
    /// 为每个有数据的槽位生成 <paramref name="optionCount"/> 个奖励选项（联机绑槽分发路径）。
    /// </summary>
    /// <param name="ringIndex">
    /// 当前环序号。按环缩放奖励尚未设计，本参数当前只参与随机流派生
    /// （在规格约定的槽位流键 <c>reward.p{slot}</c> 后追加环作用域，得到
    /// <c>reward.p{slot}.r{ringIndex}</c>），使不同环得到可复现但不同的选项序列；
    /// 待环缩放规则定稿后在此接入真实数值/类型规则。
    /// </param>
    public PerPlayerRewardSet GenerateRewards(
        IReadOnlyList<PlayerRunState?> playerStates,
        GameDefinitionRegistry definitions,
        int ringIndex,
        int optionCount,
        HostRng rng)
    {
        ArgumentNullException.ThrowIfNull(playerStates);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rng);

        var rewards = new Dictionary<int, RewardOptionsDto>();
        for (var slot = 0; slot < playerStates.Count; slot++)
        {
            if (playerStates[slot] == null)
                continue;

            // 环序号进入流键：同一 RunSeed 下不同环派生不同随机流，仍完全可复现。
            var slotRng = new HostRng(rng.NextInt(0, int.MaxValue), $"reward.p{slot}.r{ringIndex}");
            var options = new RewardOptionDto[optionCount];
            for (var i = 0; i < optionCount; i++)
            {
                options[i] = new RewardOptionDto
                {
                    Type = ERewardType.Gold,
                    DisplayNameKey = GoldRewardNameKey,
                    Amount = slotRng.NextInt(GoldAmountMin, GoldAmountMaxExclusive),
                };
            }

            rewards[slot] = new RewardOptionsDto { Options = options };
        }

        return new PerPlayerRewardSet { Rewards = rewards };
    }

    /// <summary>
    /// 应用玩家选中的奖励选项。未实现的奖励类型直接抛错，绝不静默丢弃奖励。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="selectedIndex"/> 越界。</exception>
    /// <exception cref="NotSupportedException">该奖励类型尚未实现。</exception>
    public void ApplyReward(PlayerRunState player, RewardOptionsDto options, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (selectedIndex < 0 || selectedIndex >= options.Options.Length)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        var option = options.Options[selectedIndex];
        switch (option.Type)
        {
            case ERewardType.Gold:
                player.AddGold(option.Amount);
                break;
            default:
                throw new NotSupportedException(
                    $"奖励类型 {option.Type} 尚未实现：拒绝静默丢弃该奖励。");
        }
    }
}