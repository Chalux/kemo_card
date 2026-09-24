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
}