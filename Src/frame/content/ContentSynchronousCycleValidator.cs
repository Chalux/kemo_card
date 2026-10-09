using System.Text.Json;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

/// <summary>只分析同步触发边；回合/波次等延迟钩子不能被当成递归调用。</summary>
internal static class ContentSynchronousCycleValidator
{
    private readonly record struct Node(EContentCategory Category, string Id, string Hook = "");

    public static void Validate(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        var graph = new Dictionary<Node, HashSet<Node>>();
        var creators = new HashSet<Node>();
        var repeatableCreators = new HashSet<Node>();
        void Edge(Node from, Node to)
        {
            if (!graph.TryGetValue(from, out var edges))
                graph[from] = edges = [];
            edges.Add(to);
        }

        void BuffApply(Node from, string id)
        {
            if (!store.TryGetBuff(id, out var buff))
                return;
            creators.Add(from);
            Edge(from, new(EContentCategory.Buff, id, "apply"));
            if (buff.StackRule == EBuffStackRule.Add && buff.MaxStacks > 1)
                Edge(from, new(EContentCategory.Buff, id, "stack"));
            if (buff.StackRule == EBuffStackRule.Replace || !string.IsNullOrEmpty(buff.ExclusiveGroup) ||
                buff.EffectiveTags.Contains("slot.charge", StringComparer.Ordinal))
            {
                repeatableCreators.Add(from);
                // 其他组成员是否仍存在取决于此前的移除顺序，不能把所有成员的 onRemove 当成必然重发。
                // 跨定义的动态替换由共享执行预算兜底。
                Edge(from, new(EContentCategory.Buff, id, "remove"));
            }
        }

        void GameplayEffectApply(Node from, string id, bool domain)
        {
            if (!store.TryGetGameplayEffect(id, out var ge))
                return;
            creators.Add(from);
            Edge(from, new(EContentCategory.GameplayEffect, id, "apply"));
            if (ge.StackingPolicy != EStackingPolicy.None && ge.MaxStacks > 1)
                Edge(from, new(EContentCategory.GameplayEffect, id, "stack"));
            if (domain || ge.DurationPolicy == EDurationPolicy.Instant || ge.StackingPolicy == EStackingPolicy.None)
                repeatableCreators.Add(from);
        }

        void DebuffRemoveEdges(Node from)
        {
            foreach (var buff in store.Buffs.Values.Where(buff =>
                !buff.EffectiveTags.Contains(BuiltinBuffTags.Undispellable) && buff.EffectiveTags.Any(BuiltinBuffTags.IsDebuffTag)))
                Edge(from, new(EContentCategory.Buff, buff.Id, "remove"));
            foreach (var ge in store.GameplayEffects.Values.Where(ge =>
                !ge.GrantedTags.Contains(BuiltinBuffTags.Undispellable) && ge.GrantedTags.Any(BuiltinBuffTags.IsDebuffTag)))
                Edge(from, new(EContentCategory.GameplayEffect, ge.Id, "remove"));
        }

        void EffectEdges(Node from, EffectDto effect, IReadOnlyDictionary<string, object>? overrides = null)
        {
            var parameters = ContentParameters.Merge(effect.Params, overrides);
            if (effect.Kind == EEffectKind.ChainEffects)
                foreach (var child in effect.EffectRefs)
                {
                    Edge(from, new(EContentCategory.Effect, child.EffectId));
                    if (store.TryGetEffect(child.EffectId, out var def) && def.Kind != EEffectKind.ChainEffects && child.Params is not null)
                        EffectEdges(new(EContentCategory.Effect, child.EffectId), def, child.Params);
                }
            if (effect.Kind is EEffectKind.ApplyBuff or EEffectKind.AttachSlotBuff && TryId(parameters, "buffId", out var buffId))
                BuffApply(from, buffId);
            if (effect.Kind == EEffectKind.RemoveBuff && TryId(parameters, "buffId", out var removedId))
                Edge(from, new(EContentCategory.Buff, removedId, "remove"));
            if (effect.Kind == EEffectKind.DispelDebuffs)
                DebuffRemoveEdges(from);
            if (effect.Kind == EEffectKind.SetDomain && TryId(parameters, "gameplayEffectId", out var geId))
                GameplayEffectApply(from, geId, domain: true);
        }

        void ActionEdges(Node from, SkillActionDto action, IReadOnlyDictionary<string, object>? overrides = null)
        {
            var parameters = ContentParameters.Merge(action.Params, overrides);
            if (action.Kind == ESkillActionKind.ChainActions)
                foreach (var child in action.ActionRefs)
                {
                    Edge(from, new(EContentCategory.SkillAction, child.ActionId));
                    if (store.TryGetSkillAction(child.ActionId, out var childAction) && childAction.Kind != ESkillActionKind.ChainActions && child.Params is not null)
                        ActionEdges(new(EContentCategory.SkillAction, child.ActionId), childAction, child.Params);
                }
            if (action.Kind is ESkillActionKind.ApplyBuff or ESkillActionKind.AttachSlotBuff && TryId(parameters, "buffId", out var buffId))
                BuffApply(from, buffId);
            if (action.Kind == ESkillActionKind.RemoveBuff && TryId(parameters, "buffId", out var removedId))
                Edge(from, new(EContentCategory.Buff, removedId, "remove"));
            if (action.Kind == ESkillActionKind.DispelDebuffs)
                DebuffRemoveEdges(from);
            if (action.Kind is ESkillActionKind.ApplyGameplayEffect or ESkillActionKind.SetDomain && TryId(parameters, "gameplayEffectId", out var geId))
                GameplayEffectApply(from, geId, action.Kind == ESkillActionKind.SetDomain);
            if (action.Kind == ESkillActionKind.RemoveGameplayEffect && TryId(parameters, "gameplayEffectId", out var removedGe))
                Edge(from, new(EContentCategory.GameplayEffect, removedGe, "remove"));
        }

        foreach (var effect in store.Effects.Values)
            EffectEdges(new(EContentCategory.Effect, effect.Id), effect);
        foreach (var action in store.SkillActions.Values)
            ActionEdges(new(EContentCategory.SkillAction, action.Id), action);
        foreach (var buff in store.Buffs.Values)
        {
            foreach (var (hook, refs) in new[] { ("apply", buff.Hooks.OnApply), ("remove", buff.Hooks.OnRemove), ("stack", buff.Hooks.OnStackChanged) })
                foreach (var reference in refs)
                {
                    Edge(new(EContentCategory.Buff, buff.Id, hook), new(EContentCategory.Effect, reference.EffectId));
                    if (store.TryGetEffect(reference.EffectId, out var effect) && reference.Params is not null)
                        EffectEdges(new(EContentCategory.Effect, effect.Id), effect, reference.Params);
                }
        }
        foreach (var ge in store.GameplayEffects.Values)
        {
            foreach (var reference in ge.DomainBuffRefs)
            {
                BuffApply(new(EContentCategory.GameplayEffect, ge.Id, "apply"), reference.BuffId);
                Edge(new(EContentCategory.GameplayEffect, ge.Id, "remove"), new(EContentCategory.Buff, reference.BuffId, "remove"));
            }
            foreach (var (hook, refs) in new[] { ("apply", ge.Hooks.OnApply), ("remove", ge.Hooks.OnRemove), ("stack", ge.Hooks.OnStackChanged) })
                foreach (var reference in refs)
                {
                    Edge(new(EContentCategory.GameplayEffect, ge.Id, hook), new(EContentCategory.SkillAction, reference.ActionId));
                    if (store.TryGetSkillAction(reference.ActionId, out var action) && reference.Params is not null)
                        ActionEdges(new(EContentCategory.SkillAction, action.Id), action, reference.Params);
                }
        }

        // 每个有同步出边的定义都检查，令准入能同时剔除环及进入环的定义。
        foreach (var root in graph.Keys.ToArray())
        {
            var visiting = new HashSet<Node>();
            var visited = new HashSet<Node>();
            var path = new List<Node>();
            if (FindCycle(root, graph, creators, repeatableCreators, visiting, visited, path) &&
                !errors.Any(error => error.Category == root.Category && error.DefinitionId == root.Id && error.Message.Contains("cycle", StringComparison.Ordinal)))
                errors.Add(new(root.Category, root.Id, $"Synchronous reference cycle: {string.Join(" -> ", path.Select(node => $"{node.Category}:{node.Id}:{node.Hook}"))}."));
        }
    }

    private static bool FindCycle(Node node, Dictionary<Node, HashSet<Node>> graph, HashSet<Node> creators, HashSet<Node> repeatableCreators, HashSet<Node> visiting,
        HashSet<Node> visited, List<Node> path)
    {
        if (visited.Contains(node))
            return false;
        if (!visiting.Add(node))
        {
            var cycle = path.Skip(path.IndexOf(node)).ToArray();
            // Refresh/Add/聚合 GE 只触发一次 onApply，叠层有上限；移除后重新创建才会重开这些钩子。
            // 纯移除反馈则会耗尽有限实例。
            if (cycle.Any(item => item.Hook.Length > 0) && !cycle.Any(repeatableCreators.Contains) &&
                !(cycle.Any(creators.Contains) && cycle.Any(item => item.Hook == "remove")))
                return false;
            path.Add(node);
            return true;
        }
        path.Add(node);
        if (graph.TryGetValue(node, out var edges))
            foreach (var child in edges)
                if (FindCycle(child, graph, creators, repeatableCreators, visiting, visited, path))
                    return true;
        visiting.Remove(node);
        visited.Add(node);
        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static bool TryId(IReadOnlyDictionary<string, object>? parameters, string key, out string id)
    {
        id = "";
        if (parameters is null || !parameters.TryGetValue(key, out var value))
            return false;
        id = value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() ?? "" : value?.ToString() ?? "";
        return !string.IsNullOrWhiteSpace(id);
    }
}