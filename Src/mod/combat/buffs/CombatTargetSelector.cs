using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// 效果/钩子的目标选择器：按效果参数解析目标集。
/// <c>hookTargets</c> 支持 <c>self</c>（缺省）/ <c>randomEnemy</c> / <c>allEnemies</c>；
/// <c>targetFilter</c>（<c>self</c> / <c>elementAny</c> / <c>raceAny</c>）筛选玩家角色。
/// </summary>
/// <remarks>
/// 三处共用同一语义：buff 钩子分发、<c>ApplyBuff</c> 的"给自己/给队友"投放、充能球触发效果。
/// 参数缺省一律回退到 [来源]（self）。
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
                if (filter.SelfOnly && i != source.Index)
                    continue;
                if (filter.ElementAny is { Count: > 0 } &&
                    !filter.ElementAny.Any(flag => (candidate.Element & flag) != 0))
                    continue;
                if (filter.RaceAny is { Count: > 0 } &&
                    !filter.RaceAny.Any(flag => (candidate.Race & flag) != 0))
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

    private sealed record TargetFilter(bool SelfOnly, List<EElement>? ElementAny, List<ERace>? RaceAny);

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
        List<EElement>? elementAny = ParseEnumList<EElement>(dict, "elementAny");
        List<ERace>? raceAny = ParseEnumList<ERace>(dict, "raceAny");
        return new TargetFilter(selfOnly, elementAny, raceAny);
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
