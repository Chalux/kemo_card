using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Run;

/// <summary>按故事与环派生固定随机流；同种子读档/重试不会重抽战斗。</summary>
public static class RingBattleSelector
{
    public static BattleDto Select(GameDefinitionRegistry definitions, string storyId, int ring, int runSeed)
    {
        if (!definitions.Store.TryGetStory(storyId, out var story) || story.RingBattlePools.Count == 0)
            throw new InvalidOperationException($"故事 '{storyId}' 尚未配置环战斗池。");
        var pool = story.RingBattlePools.SingleOrDefault(pool => pool.Ring == ring);
        if (pool is null || pool.Battles.Count == 0)
            throw new InvalidOperationException($"故事 '{storyId}' 的第 {ring} 环没有战斗池。");
        var entries = pool.Battles.OrderBy(entry => entry.BattleId, StringComparer.Ordinal).ToArray();
        var total = entries.Aggregate(0, (sum, entry) => checked(sum + entry.Weight));
        if (entries.Any(entry => entry.Weight < 1))
            throw new InvalidOperationException("战斗池权重必须大于零。");
        var roll = new HostRng(runSeed, $"run.battle.{storyId}.r{ring}").NextInt(0, total);
        foreach (var entry in entries)
        {
            roll -= entry.Weight;
            if (roll < 0)
                return definitions.Store.Battles[entry.BattleId];
        }
        throw new InvalidOperationException("战斗池抽取失败。");
    }
}