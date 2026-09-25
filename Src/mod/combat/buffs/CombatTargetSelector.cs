using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// 效果/钩子的目标选择器：按效果参数解析目标集。
/// <c>hookTargets</c> 支持 <c>self</c>（缺省）/ <c>team</c> / <c>allies</c> / <c>randomEnemy</c> / <c>allEnemies</c>；
/// <c>targetFilter</c>（<c>self</c> / <c>excludeSelf</c> / <c>elementAny</c> / <c>raceAny</c> / <c>raceAll</c> / <c>matchAll</c>）
/// 筛选玩家角色。
/// </summary>
/// <remarks>
/// <para>筛选维度之间的关系（2026-09-25 起）：**默认取"或"**——效果描述里的「·」表示或，
/// 只有显式写「且」时才用 <c>matchAll: true</c> 取"且"；<c>raceAll</c> 仍是列表内取"且"
/// （"同时具备多种族"的显式且）。<c>self</c> / <c>excludeSelf</c> 是硬约束，永远与筛选维度取"且"。</para>
/// <para><c>hookTargets: "team"</c> 解析为**队伍共享账本**（<see cref="CombatTargetRef.PlayerTeam"/>）：
/// 规格 §1.3 的玩家侧治疗只认账本目标（点名槽位会被软失败剔除），队伍级效果必须走这条；
/// <c>hookTargets: "allies"</c> 解析为全部玩家角色（逐槽位，"己方全体"的抽取/增益用它）。
/// 三处共用同一语义：buff 钩子分发、<c>ApplyBuff</c> 的"给自己/给队友"投放、充能球触发效果。
/// 参数缺省一律回退到 [来源]（self）。</para>
/// </remarks>
internal static class CombatTargetSelector
{
    public static IReadOnlyList<CombatTargetRef> Resolve(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return [source];

        if (parameters.TryGetValue("hookTargets", out var hookTargetsValue) && hookTargetsValue is not null)
        {
            switch (hookTargetsValue.ToString())
            {
                case "randomEnemy":
                    var alive = AliveEnemies(simulation);
                    if (alive.Count == 0)
                        return [];
                    var pick = alive[simulation.RetargetRng.NextInt(0, alive.Count)];
                    return [pick];
                case "allEnemies":
                    return AliveEnemies(simulation);
                case "allies":
                    // 全部玩家角色（逐个槽位，不是队伍账本）："己方全体"的抽取/增益类效果用它。
                    // 与 team 的区别：team = 共享账本（治疗 / 队伍级效果），allies = 每个角色。
                    return ResolveAllAllies(simulation);
                case "team":
                    // 队伍共享账本：玩家侧治疗 / 队伍级效果的唯一合法落点（规格 §1.3）。
                    return [CombatTargetRef.PlayerTeam];
            }
        }

        if (parameters.TryGetValue("targetFilter", out var filterValue) && filterValue is not null)
        {
            var filter = ParseTargetFilter(filterValue);
            if (filter is null)
                return [source];

            var matched = new List<CombatTargetRef>();
            for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
            {
                var candidate = simulation.PlayerTeam.Characters[i];

                // self / excludeSelf 是硬约束（永远取"且"）。
                if (filter.SelfOnly && i != source.Index)
                    continue;
                if (filter.ExcludeSelf && i == source.Index)
                    continue;

                // 描述约定（2026-09-25）：效果描述里的「·」= 或——筛选维度默认取"或"，
                // 只有显式写「且」时才用 matchAll: true（raceAll 仍是"同时具备多种族"的显式且）。
                var dimensions = new List<bool>(3);
                if (filter.ElementAny is { Count: > 0 })
                    dimensions.Add(filter.ElementAny.Any(flag => (candidate.Element & flag) != 0));
                if (filter.RaceAny is { Count: > 0 })
                    dimensions.Add(filter.RaceAny.Any(flag => (candidate.Race & flag) != 0));
                if (filter.RaceAll is { Count: > 0 })
                    dimensions.Add(filter.RaceAll.All(flag => (candidate.Race & flag) != 0));

                var dimensionMatched = dimensions.Count == 0 ||
                    (filter.MatchAll ? dimensions.All(value => value) : dimensions.Any(value => value));
                if (!dimensionMatched)
                    continue;

                matched.Add(new CombatTargetRef(ECombatSide.Player, i));
            }

            return matched;
        }

        return [source];
    }

    private static List<CombatTargetRef> AliveEnemies(CombatSimulation simulation)
    {
        var alive = new List<CombatTargetRef>();
        for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
        {
            if (simulation.EnemyTeam.Enemies[i].IsAlive)
                alive.Add(new CombatTargetRef(ECombatSide.Enemy, i));
        }

        return alive;
    }

    /// <summary>全部玩家角色（逐槽位引用；"己方全体"效果用，<c>hookTargets: "allies"</c>）。</summary>
    private static List<CombatTargetRef> ResolveAllAllies(CombatSimulation simulation)
    {
        var allies = new List<CombatTargetRef>(simulation.PlayerTeam.Characters.Count);
        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
            allies.Add(new CombatTargetRef(ECombatSide.Player, i));

        return allies;
    }

    private sealed record TargetFilter(
        bool SelfOnly,
        bool ExcludeSelf,
        bool MatchAll,
        List<EElement>? ElementAny,
        List<ERace>? RaceAny,
        List<ERace>? RaceAll);

    private static TargetFilter? ParseTargetFilter(object value)
    {
        Dictionary<string, object>? dict = value switch
        {
            Dictionary<string, object> direct => direct,
            IReadOnlyDictionary<string, object> readOnly => new Dictionary<string, object>(readOnly),
            JsonElement { ValueKind: JsonValueKind.Object } element =>
                element.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => (object)property.Value),
            _ => null,
        };
        if (dict is null)
            return null;

        var selfOnly = dict.TryGetValue("self", out var selfValue) &&
            string.Equals(selfValue.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var excludeSelf = dict.TryGetValue("excludeSelf", out var excludeValue) &&
            string.Equals(excludeValue.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var matchAll = dict.TryGetValue("matchAll", out var matchAllValue) &&
            string.Equals(matchAllValue.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        List<EElement>? elementAny = ParseEnumList<EElement>(dict, "elementAny");
        List<ERace>? raceAny = ParseEnumList<ERace>(dict, "raceAny");
        List<ERace>? raceAll = ParseEnumList<ERace>(dict, "raceAll");
        return new TargetFilter(selfOnly, excludeSelf, matchAll, elementAny, raceAny, raceAll);
    }

    private static List<TEnum>? ParseEnumList<TEnum>(Dictionary<string, object> dict, string key)
        where TEnum : struct, Enum
    {
        if (!dict.TryGetValue(key, out var value) || value is null)
            return null;

        var raw = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } element =>
                element.EnumerateArray().Select(item => item.ToString()).ToList(),
            IEnumerable<object> list => list.Select(item => item.ToString()!).ToList(),
            _ => null,
        };
        if (raw is null)
            return null;

        var parsed = new List<TEnum>();
        foreach (var entry in raw)
        {
            if (Enum.TryParse<TEnum>(entry, ignoreCase: true, out var flag))
                parsed.Add(flag);
        }

        return parsed.Count > 0 ? parsed : null;
    }
}