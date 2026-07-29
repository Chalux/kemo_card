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
    public string DisplayName { get; init; }
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
        _ = ringIndex;

        var rewards = new Dictionary<int, RewardOptionsDto>();
        for (var slot = 0; slot < playerStates.Count; slot++)
        {
            if (playerStates[slot] == null)
                continue;

            var slotRng = new HostRng(rng.NextInt(0, int.MaxValue), $"reward.p{slot}");
            var options = new RewardOptionDto[optionCount];
            for (var i = 0; i < optionCount; i++)
            {
                options[i] = new RewardOptionDto
                {
                    Type = ERewardType.Gold,
                    DisplayName = $"Gold x{slotRng.NextInt(10, 51)}",
                };
            }

            rewards[slot] = new RewardOptionsDto { Options = options };
        }

        return new PerPlayerRewardSet { Rewards = rewards };
    }

    public void ApplyReward(PlayerRunState player, RewardOptionsDto options, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (selectedIndex < 0 || selectedIndex >= options.Options.Length)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        _ = options.Options[selectedIndex];
    }
}