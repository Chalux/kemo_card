using System.Text.Json;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat.Condition;

/// <summary>
/// 战斗域内置条件类型（2026-09-21 启用；此前 v1 刻意留空）。
/// </summary>
/// <remarks>
/// 效果（<c>EffectDto.conditions</c>）在战斗内求值，用于「本回合打出过 N 张某属性卡」这类门闩——
/// 莱因哈特被动2 即 <c>CardPlayedThisTurn</c>。
/// 未知类型/参数非法一律视为<b>不通过</b>（运行期保守失败），内容准入阶段由
/// <c>ContentDefinitionValidator</c> 提前报错。
/// </remarks>
public static class BuiltinCombatConditions
{
    /// <summary>「本回合打出过 N 张指定属性的卡」：参数 <c>{ count, elementAny? }</c>。</summary>
    public const string CardPlayedThisTurn = "CardPlayedThisTurn";

    /// <summary>
    /// 「本回合连携达到 N 档」：参数 <c>{ tier, elementAny? }</c>。
    /// <c>tier</c> = 该属性的参与人数下限（2/3/4 = 二/三/四连携档）；
    /// <c>elementAny</c> 缺省时按<b>当前正在结算的卡牌</b>的属性判定（冯·诺依曼被动2）。
    /// </summary>
    public const string ChainTierAtLeast = "ChainTierAtLeast";

    /// <summary>
    /// 「属性/种族身份匹配」（2026-09-26 统一）：参数
    /// <c>{ elementAny?, raceAny?, raceAll?, matchAll?, partyMinCount?, partyElementAny?, partyRaceAny? }</c>。
    /// 判定主体 = <see cref="ICombatCondContext.SubjectElementFlags"/> /
    /// <see cref="ICombatCondContext.SubjectRaceFlags"/>（效果条件缺省为来源角色；目标筛选逐候选；
    /// buff 持有者条件为持有者）。队伍人数门闩与主体维度取"且"，只配人数时完全由人数决定。
    /// 所有「某元素/某种族特殊加成」一律走这条，不再为每个效果单写条件。
    /// </summary>
    public const string IdentityMatch = "IdentityMatch";

    public static void RegisterAll(ConditionRegistry<ICombatCondContext> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(CondTypeHandler.Create<ICombatCondContext, CardPlayedArgs>(
            CardPlayedThisTurn,
            "COND_CARD_PLAYED_THIS_TURN_SHORT",
            "COND_CARD_PLAYED_THIS_TURN_LONG",
            TryParseCardPlayed,
            CheckCardPlayed));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, ChainTierArgs>(
            ChainTierAtLeast,
            "COND_CHAIN_TIER_SHORT",
            "COND_CHAIN_TIER_LONG",
            TryParseChainTier,
            CheckChainTier));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, IdentityArgs>(
            IdentityMatch,
            "COND_IDENTITY_SHORT",
            "COND_IDENTITY_LONG",
            TryParseIdentity,
            CheckIdentity));
    }

    private sealed record CardPlayedArgs(int Count, int ElementFlags);

    private static bool TryParseCardPlayed(
        JsonElement args,
        string sourcePath,
        out CardPlayedArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 参数须为对象，例如 {{ \"count\": 2, \"elementAny\": [\"Yellow\"] }}";
            return false;
        }

        var count = 0;
        var elementFlags = 0;
        foreach (var property in args.EnumerateObject())
        {
            switch (property.Name)
            {
                case "count":
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out count) || count < 1)
                    {
                        error = $"{sourcePath}.count: 须为 ≥ 1 的整数";
                        return false;
                    }

                    break;
                case "elementAny":
                    if (!TryParseElementFlags(property.Value, sourcePath, out elementFlags, out error))
                        return false;
                    break;
            }
        }

        if (count == 0)
        {
            error = $"{sourcePath}.count: 缺失（须为 ≥ 1 的整数）";
            return false;
        }

        parsed = new CardPlayedArgs(count, elementFlags);
        return true;
    }

    private static bool TryParseElementFlags(
        JsonElement value,
        string sourcePath,
        out int flags,
        out string? error)
    {
        flags = 0;
        error = null;

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = $"{sourcePath}.elementAny: 须为属性名数组";
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) ||
                !Enum.TryParse<EElement>(name, ignoreCase: true, out var element) ||
                element == EElement.None)
            {
                error = $"{sourcePath}.elementAny: 未知属性 '{name}'";
                return false;
            }

            flags |= (int)element;
        }

        return true;
    }

    private static LeafEvalData CheckCardPlayed(CardPlayedArgs args, ICombatCondContext context)
    {
        var played = context.CountCardsPlayedThisTurn(context.SourceCharacterIndex, args.ElementFlags);
        return new LeafEvalData
        {
            Passed = played >= args.Count,
            Fill = [played, args.Count],
            Progress = new ConditionProgress(Math.Min(played, args.Count), args.Count),
        };
    }

    private sealed record ChainTierArgs(int Tier, int ElementFlags);

    private static bool TryParseChainTier(
        JsonElement args,
        string sourcePath,
        out ChainTierArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 参数须为对象，例如 {{ \"tier\": 2, \"elementAny\": [\"Blue\"] }}";
            return false;
        }

        var tier = 0;
        var elementFlags = 0;
        foreach (var property in args.EnumerateObject())
        {
            switch (property.Name)
            {
                case "tier":
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetInt32(out tier) ||
                        tier < 2)
                    {
                        error = $"{sourcePath}.tier: 须为 ≥ 2 的整数（连携档位由参与人数决定）";
                        return false;
                    }

                    break;
                case "elementAny":
                    if (!TryParseElementFlags(property.Value, sourcePath, out elementFlags, out error))
                        return false;
                    break;
            }
        }

        if (tier == 0)
        {
            error = $"{sourcePath}.tier: 缺失（须为 ≥ 2 的整数）";
            return false;
        }

        parsed = new ChainTierArgs(tier, elementFlags);
        return true;
    }

    private static LeafEvalData CheckChainTier(ChainTierArgs args, ICombatCondContext context)
    {
        var participants = context.CountChainParticipants(args.ElementFlags);
        return new LeafEvalData
        {
            Passed = participants >= args.Tier,
            Fill = [participants, args.Tier],
            Progress = new ConditionProgress(Math.Min(participants, args.Tier), args.Tier),
        };
    }

    #region IdentityMatch（属性/种族身份 + 队伍人数门闩）

    private sealed record IdentityArgs(IdentityFilter Filter, int PartyMinCount, IdentityFilter PartyFilter);

    private static bool TryParseIdentity(
        JsonElement args,
        string sourcePath,
        out IdentityArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 参数须为对象，例如 {{ \"elementAny\": [\"Green\"], \"raceAny\": [\"Human\"] }}";
            return false;
        }

        List<EElement>? elementAny = null;
        List<ERace>? raceAny = null;
        List<ERace>? raceAll = null;
        var matchAll = false;
        var partyMinCount = 0;
        List<EElement>? partyElementAny = null;
        List<ERace>? partyRaceAny = null;

        foreach (var property in args.EnumerateObject())
        {
            switch (property.Name)
            {
                case "elementAny":
                    if (!TryParseEnumList<EElement>(property.Value, $"{sourcePath}.elementAny", out elementAny, out error))
                        return false;
                    break;
                case "raceAny":
                    if (!TryParseEnumList<ERace>(property.Value, $"{sourcePath}.raceAny", out raceAny, out error))
                        return false;
                    break;
                case "raceAll":
                    if (!TryParseEnumList<ERace>(property.Value, $"{sourcePath}.raceAll", out raceAll, out error))
                        return false;
                    break;
                case "matchAll":
                    if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        error = $"{sourcePath}.matchAll: 须为布尔值";
                        return false;
                    }

                    matchAll = property.Value.GetBoolean();
                    break;
                case "partyMinCount":
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetInt32(out partyMinCount) ||
                        partyMinCount < 1)
                    {
                        error = $"{sourcePath}.partyMinCount: 须为 ≥ 1 的整数";
                        return false;
                    }

                    break;
                case "partyElementAny":
                    if (!TryParseEnumList<EElement>(property.Value, $"{sourcePath}.partyElementAny", out partyElementAny, out error))
                        return false;
                    break;
                case "partyRaceAny":
                    if (!TryParseEnumList<ERace>(property.Value, $"{sourcePath}.partyRaceAny", out partyRaceAny, out error))
                        return false;
                    break;
                default:
                    error = $"{sourcePath}: 未知参数 '{property.Name}'"
                        + "（可用：elementAny / raceAny / raceAll / matchAll / partyMinCount / partyElementAny / partyRaceAny）";
                    return false;
            }
        }

        var filter = new IdentityFilter(elementAny ?? [], raceAny ?? [], raceAll ?? [], matchAll);
        if (filter.IsEmpty && partyMinCount <= 0)
        {
            error = $"{sourcePath}: 至少配置一个身份维度（elementAny / raceAny / raceAll）或队伍人数门闩（partyMinCount）";
            return false;
        }

        var partyFilter = new IdentityFilter(partyElementAny ?? [], partyRaceAny ?? [], [], matchAll);
        parsed = new IdentityArgs(filter, partyMinCount, partyFilter);
        return true;
    }

    private static LeafEvalData CheckIdentity(IdentityArgs args, ICombatCondContext context)
    {
        var element = (EElement)context.SubjectElementFlags;
        var race = (ERace)context.SubjectRaceFlags;
        var holderMatched = args.Filter.Matches(element, race);

        // 队伍人数门闩（2026-09-21 语义）：与主体维度取"且"；只配人数时完全由人数决定。
        if (args.PartyMinCount > 0)
        {
            var partyCount = context.CountPartyIdentityMatches(
                args.PartyFilter.ElementFlags,
                args.PartyFilter.RaceFlags,
                args.Filter.MatchAll);
            return new LeafEvalData
            {
                Passed = partyCount >= args.PartyMinCount && (args.Filter.IsEmpty || holderMatched),
                Fill = [partyCount, args.PartyMinCount],
                Progress = new ConditionProgress(Math.Min(partyCount, args.PartyMinCount), args.PartyMinCount),
            };
        }

        return new LeafEvalData { Passed = holderMatched };
    }

    /// <summary>解析枚举名数组（<c>["Green", "Blue"]</c>）；非数组/未知名/全空报错。</summary>
    private static bool TryParseEnumList<TEnum>(
        JsonElement value,
        string sourcePath,
        out List<TEnum>? parsed,
        out string? error)
        where TEnum : struct, Enum
    {
        parsed = null;
        error = null;

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = $"{sourcePath}: 须为枚举名数组，例如 [\"Green\"]";
            return false;
        }

        var list = new List<TEnum>();
        foreach (var item in value.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) ||
                !Enum.TryParse<TEnum>(name, ignoreCase: true, out var flag) ||
                Convert.ToInt64(flag) == 0)
            {
                error = $"{sourcePath}: 未知枚举名 '{name}'";
                return false;
            }

            list.Add(flag);
        }

        if (list.Count == 0)
        {
            error = $"{sourcePath}: 列表不能为空";
            return false;
        }

        parsed = list;
        return true;
    }

    #endregion
}