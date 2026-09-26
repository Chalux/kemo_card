using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// 效果/钩子的目标选择器：按效果参数解析目标集。
/// <c>hookTargets</c> 支持 <c>self</c>（缺省）/ <c>team</c> / <c>allies</c> / <c>randomEnemy</c> / <c>allEnemies</c>；
/// <c>targetFilter</c>（<c>self</c> / <c>excludeSelf</c> / <c>condition</c>）筛选玩家角色。
/// </summary>
/// <remarks>
/// <para>筛选条件（2026-09-26 统一）：<c>condition</c> 是一条战斗条件，逐候选把主体设为该候选求值；
/// 身份/种族筛选走 <c>IdentityMatch</c>。旧的扁平字段（<c>elementAny</c> / <c>raceAny</c> / <c>raceAll</c> /
/// <c>matchAll</c>）已删除——出现未知键时<b>保守回退到 [来源]</b>，绝不静默全队命中；
/// 内容准入阶段由 <c>ContentDefinitionValidator</c> 直接报错。
/// <c>self</c> / <c>excludeSelf</c> 是硬约束，永远与条件取"且"。</para>
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
                // self / excludeSelf 是硬约束（永远取"且"）。
                if (filter.SelfOnly && i != source.Index)
                    continue;
                if (filter.ExcludeSelf && i == source.Index)
                    continue;

                var candidate = new CombatTargetRef(ECombatSide.Player, i);
                // 身份/其它条件（2026-09-26 统一）：主体 = 候选；不通过即落选。
                if (filter.Condition is not null)
                {
                    var (elementFlags, raceFlags) = CombatIdentity.Resolve(simulation, candidate);
                    var context = new CombatCondContext(simulation, source.Index, elementFlags, raceFlags);
                    if (!CombatConditionEvaluator.Pass(
                        filter.Condition,
                        context,
                        $"targetFilter.condition（{candidate.Side}:{candidate.Index}）"))
                    {
                        continue;
                    }
                }

                matched.Add(candidate);
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
        ConditionRefDto? Condition);

    /// <summary>
    /// 解析 <c>targetFilter</c>（2026-09-26 统一）：只认 <c>self</c> / <c>excludeSelf</c> / <c>condition</c>。
    /// 出现未知键（旧扁平写法）或非法 / 空 kind 的 <c>condition</c> 时返回 <c>null</c>——调用方回退到
    /// [来源]，宁可少投放也不静默命中全队；这类写法在内容准入阶段就会被 <c>ContentDefinitionValidator</c> 拒绝。
    /// </summary>
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
        if (dict.Keys.Any(key => key is not ("self" or "excludeSelf" or "condition")))
            return null;

        var selfOnly = ReadBool(dict, "self");
        var excludeSelf = ReadBool(dict, "excludeSelf");
        ConditionRefDto? condition = null;
        if (dict.TryGetValue("condition", out var conditionValue) && conditionValue is not null)
        {
            condition = ParseConditionRef(conditionValue);
            if (condition is null || string.IsNullOrWhiteSpace(condition.Kind))
                return null;
        }

        return new TargetFilter(selfOnly, excludeSelf, condition);
    }

    private static bool ReadBool(Dictionary<string, object> dict, string key) =>
        dict.TryGetValue(key, out var value) &&
        string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>把参数里的 <c>condition</c> 反序列化成 <see cref="ConditionRefDto"/>（对象形态原样序列化再读）。</summary>
    private static ConditionRefDto? ParseConditionRef(object value)
    {
        try
        {
            var json = value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<ConditionRefDto>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}