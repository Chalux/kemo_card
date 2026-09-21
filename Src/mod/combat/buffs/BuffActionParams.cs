using System.Text.Json;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// buff 类效果 / 技能动作的公共参数解析：<c>buffId</c>、实例参数裁剪（剥离投放控制键）、
/// 驱散 tag 列表。<see cref="Effects.CombatEffectExecutor"/> 与
/// <see cref="Effects.SkillActionExecutor"/> 两条入口共用，保证 JSON 与内存构造的参数行为一致。
/// </summary>
internal static class BuffActionParams
{
    /// <summary>取 <c>params.buffId</c>；缺失或空白返回 false（out 为空串）。</summary>
    public static bool TryGetBuffId(IReadOnlyDictionary<string, object> parameters, out string buffId)
    {
        buffId = string.Empty;
        if (!parameters.TryGetValue("buffId", out var value) || value is null)
            return false;

        buffId = value.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(buffId);
    }

    /// <summary>取任意字符串参数；缺失或空白返回 false（out 为空串）。</summary>
    public static bool TryGetString(
        IReadOnlyDictionary<string, object> parameters,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
            return false;

        value = raw.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>取整数参数（JSON 数字或字符串皆可）；缺失/非法返回 <paramref name="fallback"/>。</summary>
    public static int ReadInt(IReadOnlyDictionary<string, object> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
            return fallback;

        return raw switch
        {
            int i => i,
            long l => l is >= int.MinValue and <= int.MaxValue ? (int)l : fallback,
            float f => (int)f,
            double d => (int)d,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
            _ => int.TryParse(raw.ToString(), out var parsed) ? parsed : fallback,
        };
    }

    /// <summary>
    /// 参数里是否带目标选择器（<c>hookTargets</c> / <c>targetFilter</c>）：
    /// 带了就按选择器重解析目标，覆盖调用方传入的目标集（"伤害敌方 + 增益自身"这类卡依赖此语义）。
    /// </summary>
    public static bool HasTargetSelector(IReadOnlyDictionary<string, object> parameters) =>
        parameters.ContainsKey("hookTargets") || parameters.ContainsKey("targetFilter");

    /// <summary>产球者槽位：来源是玩家槽位时取其索引，其余（队伍账本 / 敌方）视为无产球者（-1）。</summary>
    public static int ResolveProducerIndex(Runtime.CombatTargetRef source) =>
        source.Side == Runtime.ECombatSide.Player && source.Index >= 0 ? source.Index : -1;

    /// <summary>
    /// 把投放参数裁剪成 buff 实例参数：剥离 <paramref name="controlKeys"/>（buffId / slotIndex 等
    /// 控制键不进入实例）。空结果返回 null（实例无参数）。
    /// </summary>
    public static Dictionary<string, object>? BuildInstanceParams(
        IReadOnlyDictionary<string, object> parameters,
        params string[] controlKeys)
    {
        if (parameters.Count == 0)
            return null;

        Dictionary<string, object>? instanceParams = null;
        foreach (var (key, value) in parameters)
        {
            var isControlKey = false;
            foreach (var controlKey in controlKeys)
            {
                if (string.Equals(key, controlKey, StringComparison.Ordinal))
                {
                    isControlKey = true;
                    break;
                }
            }

            if (isControlKey)
                continue;

            instanceParams ??= new Dictionary<string, object>(StringComparer.Ordinal);
            instanceParams[key] = value;
        }

        return instanceParams;
    }

    /// <summary>
    /// 取字符串列表参数（如 <c>params.withTags</c>）：支持 JSON 数组与内存集合两种形态；
    /// 缺失或空列表返回 null。
    /// </summary>
    public static IReadOnlyList<string>? TryGetTagList(
        IReadOnlyDictionary<string, object> parameters,
        string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return null;

        List<string>? result = null;
        switch (value)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                foreach (var item in element.EnumerateArray())
                {
                    var text = item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        (result ??= []).Add(text);
                }

                break;
            case IEnumerable<object> list:
                foreach (var item in list)
                {
                    var text = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        (result ??= []).Add(text!);
                }

                break;
        }

        return result is { Count: > 0 } ? result : null;
    }
}
