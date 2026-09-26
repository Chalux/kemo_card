using System.Text.Json;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat.Condition;

/// <summary>
/// 战斗条件求值入口（2026-09-26 抽共用）：效果 <c>conditions</c>、钩子/技能动作的
/// <c>targetFilter.condition</c>、buff 持有者条件都走这里，避免各写一套解析/判定。
/// </summary>
/// <remarks>
/// 未知类型 / 参数非法一律视为<b>不通过</b>（运行期保守失败）；内容准入阶段由
/// <c>ContentDefinitionValidator</c> 提前报错。
/// </remarks>
internal static class CombatConditionEvaluator
{
    /// <summary>求值一组条件（AND）。空列表视为通过。</summary>
    public static bool Pass(
        IReadOnlyList<ConditionRefDto> conditions,
        ICombatCondContext context,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var condition in conditions)
        {
            if (!Pass(condition, context, $"{sourcePath}.{condition.Kind}"))
                return false;
        }

        return true;
    }

    /// <summary>求值单条条件。</summary>
    public static bool Pass(ConditionRefDto condition, ICombatCondContext context, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(context);

        if (!ConditionDomains.Combat.TryGet(condition.Kind, out var handler) || handler is null)
            return false;

        var args = JsonSerializer.SerializeToElement(
            condition.Params ?? new Dictionary<string, object>(StringComparer.Ordinal));
        if (!handler.TryParse(args, sourcePath, out var parsedArgs, out _) || parsedArgs is null)
            return false;

        return handler.Check(parsedArgs, context).Passed;
    }
}
