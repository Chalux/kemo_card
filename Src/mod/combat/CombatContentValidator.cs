using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

/// <summary>
/// 战斗相关内容的形状校验。与 <c>ContentDefinitionValidator</c> 的引用完整性校验互补：
/// 这里只管战斗规格附加的约束（如主动链的档位配置）。
/// </summary>
public static class CombatContentValidator
{
    /// <summary>
    /// 规格 §5.1：主动链每项须有 skillId 且 <c>cooldown >= 1</c>。
    /// 空链视为「该角色没有主动技」，直接通过。
    /// </summary>
    public static bool TryValidateActiveSkillChain(CharacterDto character, out string? error)
    {
        ArgumentNullException.ThrowIfNull(character);

        for (var i = 0; i < character.ActiveSkillChain.Count; i++)
        {
            var entry = character.ActiveSkillChain[i];
            if (string.IsNullOrWhiteSpace(entry.SkillId))
            {
                error = $"角色 {character.Id} 的 activeSkillChain[{i}] 缺少 skillId。";
                return false;
            }

            if (entry.Cooldown < 1)
            {
                error = $"角色 {character.Id} 的 activeSkillChain[{i}] cooldown 必须大于等于 1。";
                return false;
            }
        }

        error = null;
        return true;
    }

    #region Heal 目标校验（规格 §1.3）

    /// <summary>
    /// 规格 §1.3：玩家侧治疗只能回队伍共享账本，内容必须写 <c>scope: Team</c>；
    /// 写成 <c>Single</c> / <c>All</c> 会被运行期软失败丢弃，这里在进战斗前先报出来。
    /// </summary>
    /// <remarks>
    /// 只检查「持有者一定是玩家角色」的两类内容：卡牌，以及角色主动链各档的技能。
    /// 独立技能的 <c>targetOverride.side</c> 是相对施法者的，敌人自愈同样写 <c>Self</c>，无法在此判定敌我，故跳过。
    /// </remarks>
    public static bool TryValidateHealTargeting(GameDefinitionRegistry definitions, out string? error)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (var (cardId, card) in definitions.Store.Cards)
        {
            if (IsLegalPlayerHealTargeting(card.TargetSide, card.TargetScope))
                continue;
            if (!card.SkillRefs.Any(skillRef => SkillYieldsHeal(definitions, skillRef.SkillId)))
                continue;

            error = $"卡牌 {cardId} 含治疗效果，玩家侧治疗的 targetScope 必须为 Team。";
            return false;
        }

        foreach (var (characterId, character) in definitions.Store.Characters)
        {
            foreach (var tier in character.ActiveSkillChain)
            {
                if (!definitions.Store.TryGetSkill(tier.SkillId, out var skill))
                    continue;

                // 缺省 targetOverride 视作 Self 单体（2026-07-28 决议），治疗会打到施法者自己的槽位上。
                var side = skill.TargetOverride?.Side ?? ETargetSide.Self;
                var scope = skill.TargetOverride?.Scope ?? ETargetScope.Self;
                if (IsLegalPlayerHealTargeting(side, scope))
                    continue;
                if (!SkillYieldsHeal(definitions, tier.SkillId))
                    continue;

                error = $"角色 {characterId} 的主动技 {tier.SkillId} 含治疗效果，targetOverride.scope 必须为 Team。";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool IsLegalPlayerHealTargeting(ETargetSide side, ETargetScope scope) =>
        side is not (ETargetSide.Self or ETargetSide.Ally) || scope is ETargetScope.Team;

    private static bool SkillYieldsHeal(GameDefinitionRegistry definitions, string skillId)
    {
        if (!definitions.Store.TryGetSkill(skillId, out var skill))
            return false;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        return skill.EffectRefs.Any(effectRef => EffectYieldsHeal(definitions, effectRef.EffectId, visited));
    }

    private static bool EffectYieldsHeal(GameDefinitionRegistry definitions, string effectId, HashSet<string> visited)
    {
        if (!visited.Add(effectId) || !definitions.Store.TryGetEffect(effectId, out var effect))
            return false;
        if (effect.Kind is EEffectKind.Heal)
            return true;

        return effect.EffectRefs.Any(child => EffectYieldsHeal(definitions, child.EffectId, visited));
    }

    #endregion
}