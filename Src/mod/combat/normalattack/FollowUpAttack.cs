using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Buffs;

namespace KemoCard.Mod.Combat.NormalAttack;

/// <summary>
/// 追打（2026-09-21）：持有 <see cref="BuiltinBuffTags.TraitFollowUp"/> 的角色，在<b>不是</b>本回合
/// 普攻归属角色时，仍以 <c>params.percent</c>%（缺省 100）的攻击力参与该次普攻。
/// </summary>
/// <remarks>
/// 口径决议：
/// <list type="bullet">
/// <item>多个追打 buff（含同名 buff 的多个实例）同时存在时<b>只取最高值</b>，不叠乘、不叠层；</item>
/// <item>本回合的归属角色自己持有追打时<b>不重复出手</b>（他的普攻已经就是那一次）；</item>
/// <item>追打沿用普攻公式：<c>(攻击力 × percent × NormalAttackScale) − 目标对应防御</c>，
/// 物/魔取高者、带攻击者元素、走统一的伤害包管线；</item>
/// <item>普攻次数加成（<see cref="KemoCard.Frame.Gas.AttributeIds.NormalAttackCount"/>）按"每一次普攻"
/// 都重新结算，因此追打者在多次普攻里每次都会补打。</item>
/// </list>
/// </remarks>
public static class FollowUpAttack
{
    /// <summary>追打百分比的缺省值（未声明 <c>params.percent</c> 时）。</summary>
    public const float DefaultPercent = 100f;

    /// <summary>该角色的追打百分比（0 = 不追打）；取全部生效实例的最高值。</summary>
    public static float ResolvePercent(CharacterBattleInstance character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var best = 0f;
        foreach (var instance in character.Buffs.All)
        {
            if (instance.IsDormant ||
                !instance.Def.EffectiveTags.Contains(BuiltinBuffTags.TraitFollowUp, StringComparer.Ordinal))
            {
                continue;
            }

            best = MathF.Max(best, ReadPercent(instance));
        }

        return best;
    }

    /// <summary>按槽位顺序列出本回合参与追打的角色（不含归属者本人）。</summary>
    public static IEnumerable<(int Index, float Percent)> ResolveParticipants(
        IReadOnlyList<CharacterBattleInstance> characters,
        int ownerIndex)
    {
        ArgumentNullException.ThrowIfNull(characters);

        for (var index = 0; index < characters.Count; index++)
        {
            if (index == ownerIndex)
                continue;

            var percent = ResolvePercent(characters[index]);
            if (percent > 0f)
                yield return (index, percent);
        }
    }

    private static float ReadPercent(BuffInstance instance)
    {
        if (instance.Params is null ||
            !instance.Params.TryGetValue("percent", out var value) ||
            value is null)
        {
            return DefaultPercent;
        }

        return float.TryParse(value.ToString(), out var parsed) && parsed > 0f ? parsed : DefaultPercent;
    }
}
