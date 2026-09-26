using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat.Condition;

/// <summary>
/// 属性/种族身份筛选（通用，2026-09-26）：<c>IdentityMatch</c> 条件、钩子/技能动作的目标筛选、
/// buff 持有者条件共用同一份判定。
/// </summary>
/// <remarks>
/// 判定口径（与内容描述约定一致）：
/// <list type="bullet">
/// <item>列表内取"或"（<c>elementAny</c> / <c>raceAny</c>），<c>raceAll</c> 是列表内取"且"；</item>
/// <item>跨列表默认取"或"，只有显式 <c>matchAll: true</c> 才取"且"；</item>
/// <item>只把**已配置**的维度纳入判定：没有任何维度时返回 <c>false</c>（保守失败）。</item>
/// </list>
/// </remarks>
public sealed record IdentityFilter(
    IReadOnlyList<EElement> ElementAny,
    IReadOnlyList<ERace> RaceAny,
    IReadOnlyList<ERace> RaceAll,
    bool MatchAll)
{
    public static readonly IdentityFilter Empty = new([], [], [], false);

    /// <summary>未配置任何身份维度。</summary>
    public bool IsEmpty => ElementAny.Count == 0 && RaceAny.Count == 0 && RaceAll.Count == 0;

    /// <summary>属性掩码（供队伍人数查询用；空列表为 0）。</summary>
    public int ElementFlags
    {
        get
        {
            var flags = 0;
            foreach (var element in ElementAny)
                flags |= (int)element;
            return flags;
        }
    }

    /// <summary>种族掩码（供队伍人数查询用；空列表为 0）。</summary>
    public int RaceFlags
    {
        get
        {
            var flags = 0;
            foreach (var race in RaceAny)
                flags |= (int)race;
            foreach (var race in RaceAll)
                flags |= (int)race;
            return flags;
        }
    }

    /// <summary>判定主体是否命中；未配置任何维度时返回 false。</summary>
    public bool Matches(EElement element, ERace race)
    {
        var dimensions = new List<bool>(3);
        if (ElementAny.Count > 0)
            dimensions.Add(ElementAny.Any(flag => flag != EElement.None && (element & flag) != 0));
        if (RaceAny.Count > 0)
            dimensions.Add(RaceAny.Any(flag => flag != ERace.None && (race & flag) != 0));
        if (RaceAll.Count > 0)
            dimensions.Add(RaceAll.All(flag => flag != ERace.None && (race & flag) != 0));

        if (dimensions.Count == 0)
            return false;

        return MatchAll
            ? dimensions.All(matched => matched)
            : dimensions.Any(matched => matched);
    }
}
