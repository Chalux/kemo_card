using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat;

public readonly record struct HandCardChoice(int CharacterIndex, string RuntimeInstanceId);

/// <summary>主动技能的选牌弃置要求；随机弃牌必须显式声明 random: true。</summary>
public sealed record DiscardSelection(int Minimum, int Maximum)
{
    #region 选牌要求

    public static DiscardSelection? Resolve(CombatSimulation simulation, SkillDto skill, int characterIndex,
        IReadOnlyList<CombatTargetRef>? targets = null)
    {
        var resolved = CombatTargeting.ResolveActiveSkillTargets(simulation, skill, characterIndex, targets ?? [], out _);
        return resolved is null ? null : BuildPlan(simulation, skill, characterIndex, resolved)?.Limits;
    }

    internal static DiscardPlan? BuildPlan(CombatSimulation simulation, SkillDto skill, int characterIndex,
        IReadOnlyList<CombatTargetRef> targets)
    {
        if (simulation.Phase == StateMachine.ECombatPhase.Marble)
            return null;
        var source = new CombatTargetRef(ECombatSide.Player, characterIndex);
        var groups = new List<DiscardPoolRequirement>();
        foreach (var (reference, parameters) in EnumerateDiscardParams(simulation, skill, source))
        {
            var count = ContentParameters.ReadInt(parameters, "count", 1);
            if (count <= 0 || ReadFlag(parameters, "random"))
                continue;

            foreach (var owners in ResolvePools(simulation, source, targets, parameters))
                groups.Add(new DiscardPoolRequirement(reference, owners, count, !ReadFlag(parameters, "allowFewer")));
        }

        return groups.Count == 0 ? null : new DiscardPlan(groups, simulation.PlayerTeam.Characters);
    }

    internal static IEnumerable<int[]> ResolvePools(CombatSimulation simulation, CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets, IReadOnlyDictionary<string, object> parameters)
    {
        var owners = ResolveOwners(simulation, source, targets, parameters, out var shared);
        return shared ? [owners] : owners.Select(owner => new[] { owner });
    }

    internal static int[] ResolveOwners(CombatSimulation simulation, CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets, IReadOnlyDictionary<string, object> parameters, out bool shared)
    {
        // 敌人没有玩家手牌。选牌预览也无需为这个空池消耗目标随机流。
        if (parameters.TryGetValue("hookTargets", out var selector) && selector?.ToString() is "randomEnemy" or "allEnemies")
        {
            shared = false;
            return [];
        }

        var hasSelector = BuffActionParams.HasTargetSelector(parameters);
        var resolved = hasSelector ? CombatTargetSelector.Resolve(simulation, source, parameters) : targets;
        var count = simulation.PlayerTeam.Characters.Count;
        shared = resolved.Contains(CombatTargetRef.PlayerTeam);
        if (shared)
            return Enumerable.Range(0, count).ToArray();

        var owners = resolved.Where(target => target.Side == ECombatSide.Player && target.Index >= 0 && target.Index < count)
            .Select(target => target.Index).Distinct().Order().ToArray();
        return owners.Length == 0 && !hasSelector && source.Side == ECombatSide.Player && source.Index >= 0 && source.Index < count
            ? [source.Index]
            : owners;
    }

    internal static bool ReadFlag(IReadOnlyDictionary<string, object> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && bool.TryParse(value?.ToString(), out var parsed) && parsed;

    #endregion

    #region 引用链展开

    private static IEnumerable<(object Reference, IReadOnlyDictionary<string, object> Parameters)> EnumerateDiscardParams(
        CombatSimulation simulation, SkillDto skill, CombatTargetRef source)
    {
        var path = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in skill.ActionRefs)
            foreach (var parameters in EnumerateActionDiscards(simulation.Definitions.Store, reference, path, 0))
                yield return parameters;
        if (skill.ActionRefs.Count > 0)
            yield break;
        foreach (var reference in skill.EffectRefs)
            foreach (var parameters in EnumerateEffectDiscards(simulation, source, reference, path, 0))
                yield return parameters;
    }

    // 与执行器一样只合并当前引用参数；父级 Chain 的参数不会传给子引用。
    // 路径守卫仅截断递归环，重复的兄弟引用仍按实际执行次数计入选牌要求。
    private static IEnumerable<(object Reference, IReadOnlyDictionary<string, object> Parameters)> EnumerateActionDiscards(
        GameDefinitionStore store, SkillActionRefDto reference, HashSet<string> path, int depth)
    {
        if (depth > 32 || !store.TryGetSkillAction(reference.ActionId, out var action) || !path.Add(reference.ActionId))
            yield break;
        try
        {
            if (action.Kind is ESkillActionKind.Discard or ESkillActionKind.DiscardAndRecord)
                yield return (reference, ContentParameters.Merge(action.Params, reference.Params));
            else if (action.Kind == ESkillActionKind.ChainActions)
                foreach (var child in action.ActionRefs)
                    foreach (var parameters in EnumerateActionDiscards(store, child, path, depth + 1))
                        yield return parameters;
        }
        finally { path.Remove(reference.ActionId); }
    }

    private static IEnumerable<(object Reference, IReadOnlyDictionary<string, object> Parameters)> EnumerateEffectDiscards(
        CombatSimulation simulation, CombatTargetRef source, EffectRefDto reference, HashSet<string> path, int depth)
    {
        if (depth > 32 || !simulation.Definitions.Store.TryGetEffect(reference.EffectId, out var effect) ||
            effect.Kind is not (EEffectKind.Discard or EEffectKind.ChainEffects) ||
            !CombatEffectExecutor.ConditionsPass(effect, simulation, source) || !path.Add(reference.EffectId))
            yield break;
        try
        {
            if (effect.Kind == EEffectKind.Discard)
                yield return (reference, ContentParameters.Merge(effect.Params, reference.Params));
            else if (effect.Kind == EEffectKind.ChainEffects)
                foreach (var child in effect.EffectRefs)
                    foreach (var parameters in EnumerateEffectDiscards(simulation, source, child, path, depth + 1))
                        yield return parameters;
        }
        finally { path.Remove(reference.EffectId); }
    }

    #endregion
}
