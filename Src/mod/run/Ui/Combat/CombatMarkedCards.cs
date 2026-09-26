using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 已标记（入队）卡牌的展示口径（2026-09-26）：按角色汇总本回合标记的卡 id，
/// 索引与 <c>PlayerTeam.Characters</c> 对齐，顺序 = 结算顺序（priority 降序 → 入队序号升序）。
/// </summary>
/// <remarks>
/// <para>数据源是 <c>CardExecutionQueue.PeekAllOrdered</c>（只读）：已结算 / 已取消的牌不在队列里，
/// 因此天然不会出现在列表上；不需要额外的"已打出"过滤。</para>
/// <para>纯函数（可单测），由 <c>CombatWin.SyncFromState</c> 喂给左栏队友卡的已标记卡条。</para>
/// </remarks>
public static class CombatMarkedCards
{
    /// <summary>每个角色已标记的卡 id（无标记的角色为空列表）。</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Resolve(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var characters = simulation.PlayerTeam.Characters;
        var buckets = new List<string>[characters.Count];
        for (var i = 0; i < buckets.Length; i++)
        {
            buckets[i] = [];
        }

        foreach (var entry in simulation.CardQueue.PeekAllOrdered())
        {
            if (entry.CharacterIndex < 0 || entry.CharacterIndex >= buckets.Length)
            {
                continue;
            }

            buckets[entry.CharacterIndex].Add(entry.CardId);
        }

        return buckets;
    }
}
