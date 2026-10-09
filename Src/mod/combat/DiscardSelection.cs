using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat;

/// <summary>主动技能的选牌弃置要求；随机弃牌必须显式声明 random: true。</summary>
public sealed record DiscardSelection(int Minimum, int Maximum)
{
    #region 选牌要求

    public static DiscardSelection? Resolve(CombatSimulation simulation, SkillDto skill, int characterIndex)
    {
        var required = 0;
        var maximum = 0;
        foreach (var parameters in EnumerateDiscardParams(simulation.Definitions.Store, skill))
        {
            if (ReadFlag(parameters, "random"))
                continue;
            var count = Math.Max(0, ContentParameters.ReadInt(parameters, "count", 1));
            maximum = (int)Math.Min(int.MaxValue, (long)maximum + count);
            if (!ReadFlag(parameters, "allowFewer"))
                required = (int)Math.Min(int.MaxValue, (long)required + count);
        }
        if (maximum == 0)
            return null;
        var available = simulation.PlayerTeam.Characters[characterIndex].HandSlots.Count(slot => !slot.IsEmpty);
        return new(Math.Min(required, available), Math.Min(maximum, available));
    }

    public bool Validate(CharacterBattleInstance character, IReadOnlyList<int>? slots) =>
        (slots?.Count ?? 0) >= Minimum && (slots?.Count ?? 0) <= Maximum &&
        (slots is null || slots.Distinct().Count() == slots.Count && slots.All(index =>
            index >= 0 && index < character.HandSlots.Count && !character.HandSlots[index].IsEmpty));

    internal static bool ReadFlag(IReadOnlyDictionary<string, object> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && bool.TryParse(value?.ToString(), out var parsed) && parsed;

    #endregion

    #region 引用链展开

    private static IEnumerable<IReadOnlyDictionary<string, object>> EnumerateDiscardParams(GameDefinitionStore store, SkillDto skill)
    {
        var path = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in skill.ActionRefs)
            foreach (var parameters in EnumerateActionDiscards(store, reference, path, 0))
                yield return parameters;
        if (skill.ActionRefs.Count > 0)
            yield break;
        foreach (var reference in skill.EffectRefs)
            foreach (var parameters in EnumerateEffectDiscards(store, reference, path, 0))
                yield return parameters;
    }

    // 与执行器一样只合并当前引用参数；父级 Chain 的参数不会传给子引用。
    // 路径守卫仅截断递归环，重复的兄弟引用仍按实际执行次数计入选牌要求。
    private static IEnumerable<IReadOnlyDictionary<string, object>> EnumerateActionDiscards(
        GameDefinitionStore store, SkillActionRefDto reference, HashSet<string> path, int depth)
    {
        if (depth > 32 || !store.TryGetSkillAction(reference.ActionId, out var action) || !path.Add(reference.ActionId))
            yield break;
        try
        {
            if (action.Kind is ESkillActionKind.Discard or ESkillActionKind.DiscardAndRecord)
                yield return ContentParameters.Merge(action.Params, reference.Params);
            else if (action.Kind == ESkillActionKind.ChainActions)
                foreach (var child in action.ActionRefs)
                    foreach (var parameters in EnumerateActionDiscards(store, child, path, depth + 1))
                        yield return parameters;
        }
        finally { path.Remove(reference.ActionId); }
    }

    private static IEnumerable<IReadOnlyDictionary<string, object>> EnumerateEffectDiscards(
        GameDefinitionStore store, EffectRefDto reference, HashSet<string> path, int depth)
    {
        if (depth > 32 || !store.TryGetEffect(reference.EffectId, out var effect) || !path.Add(reference.EffectId))
            yield break;
        try
        {
            if (effect.Kind == EEffectKind.Discard)
                yield return ContentParameters.Merge(effect.Params, reference.Params);
            else if (effect.Kind == EEffectKind.ChainEffects)
                foreach (var child in effect.EffectRefs)
                    foreach (var parameters in EnumerateEffectDiscards(store, child, path, depth + 1))
                        yield return parameters;
        }
        finally { path.Remove(reference.EffectId); }
    }

    #endregion
}