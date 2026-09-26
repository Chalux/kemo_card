using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 嘲讽标识（Run 规格 §14.2）：给"当前嘲讽值最高"的队友打 Crosshair 准星。
/// </summary>
/// <remarks>
/// <para>判定规则（2026-09-26 定案）：</para>
/// <list type="bullet">
/// <item>全员嘲讽值都为 0 → 谁都不标（嘲讽不在场）；</item>
/// <item>否则标出**所有嘲讽值等于最大值**的槽位——并列最高时全部显示；最大值本身为 0
/// （有人被减成负数）时标那些 0。</item>
/// </list>
/// <para>例：<c>[0, -2, 0, 0]</c> → 1/3/4 号；<c>[0, 0, 0, 0]</c> → 全不标；
/// <c>[0, 2, 0, 2]</c> → 2/4 号；<c>[0, 2, 0, 0]</c> → 2 号。</para>
/// <para>判定是纯函数（可单测）；落笔由 <see cref="AllyUnitCmp"/> / <see cref="PartyMemberCmp"/>
/// 的 <c>Crosshair</c> 节点负责，避免两处各写一套。</para>
/// </remarks>
public static class CombatTauntMarks
{
    private const float Epsilon = 0.0001f;

    /// <summary>读全队当前嘲讽值（<see cref="AttributeIds.Taunt"/>）后判定；索引与 <c>PlayerTeam.Characters</c> 对齐。</summary>
    public static bool[] Resolve(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var characters = simulation.PlayerTeam.Characters;
        var values = new float[characters.Count];
        for (var i = 0; i < characters.Count; i++)
        {
            values[i] = characters[i].Asc.GetCurrentValue(AttributeIds.Taunt);
        }

        return Resolve(values);
    }

    /// <summary>按槽位给出是否显示 Crosshair（索引与 <c>PlayerTeam.Characters</c> 对齐）。</summary>
    public static bool[] Resolve(IReadOnlyList<float> taunts)
    {
        ArgumentNullException.ThrowIfNull(taunts);

        var marks = new bool[taunts.Count];
        if (taunts.Count == 0)
        {
            return marks;
        }

        var highest = float.NegativeInfinity;
        var anyNonZero = false;
        foreach (var taunt in taunts)
        {
            if (taunt > highest)
            {
                highest = taunt;
            }

            if (MathF.Abs(taunt) > Epsilon)
            {
                anyNonZero = true;
            }
        }

        // 全 0 = 嘲讽不在场：连"最高"（0）都不标。
        if (!anyNonZero)
        {
            return marks;
        }

        for (var i = 0; i < taunts.Count; i++)
        {
            marks[i] = MathF.Abs(taunts[i] - highest) <= Epsilon;
        }

        return marks;
    }
}
