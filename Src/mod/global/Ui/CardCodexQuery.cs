using System;
using System.Collections.Generic;
using System.Linq;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

public enum ECardFilterField
{
    CardType,
    Cost,
    Element,
    Role,
    CostType,
    Tag,
}

public enum ECardFilterOp
{
    Equal,
    NotEqual,
    LessOrEqual,
    GreaterOrEqual,
    Contains,
    Exact,
}

public readonly record struct CardFilterCondition(
    ECardFilterField Field,
    ECardFilterOp Op,
    string ValueId,
    string DisplayText);

public static class CardCodexQuery
{
    public const int PageSize = 8;
    public const int MaxCostOption = 10;

    public static IReadOnlyList<ECardFilterOp> OpsForField(ECardFilterField field) => field switch
    {
        ECardFilterField.Cost => [ECardFilterOp.LessOrEqual, ECardFilterOp.Equal, ECardFilterOp.GreaterOrEqual],
        ECardFilterField.CardType or ECardFilterField.Role or ECardFilterField.CostType =>
            [ECardFilterOp.Equal, ECardFilterOp.NotEqual],
        ECardFilterField.Element or ECardFilterField.Tag =>
            [ECardFilterOp.Contains, ECardFilterOp.Exact],
        _ => [ECardFilterOp.Equal],
    };

    public static bool MatchesCondition(CardDto card, CardFilterCondition condition)
    {
        return condition.Field switch
        {
            ECardFilterField.CardType => MatchEnum(card.CardType, condition),
            ECardFilterField.Role => MatchEnum(card.Role, condition),
            ECardFilterField.CostType => MatchEnum(card.CostType, condition),
            ECardFilterField.Cost => MatchCost(card.Cost, condition),
            ECardFilterField.Element => MatchElement(card.Element, condition),
            ECardFilterField.Tag => MatchTag(card.Tags, condition),
            _ => false,
        };
    }

    public static IReadOnlyList<CardDto> Filter(
        IEnumerable<CardDto> cards,
        IReadOnlyList<CardFilterCondition> conditions,
        string textQuery,
        Func<string, string> translate,
        Func<string, SkillDto?> tryGetSkill)
    {
        var query = textQuery?.Trim() ?? "";
        return cards
            .Where(c => !c.HideInDex)
            .Where(c => conditions.All(cond => MatchesCondition(c, cond)))
            .Where(c => string.IsNullOrEmpty(query) || MatchesText(c, query, translate, tryGetSkill))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static bool MatchesText(
        CardDto card,
        string query,
        Func<string, string> translate,
        Func<string, SkillDto?> tryGetSkill)
    {
        if (ContainsIgnoreCase(translate(card.DisplayNameId), query)
            || ContainsIgnoreCase(card.DisplayNameId, query))
        {
            return true;
        }

        foreach (var skillRef in card.SkillRefs)
        {
            var skill = tryGetSkill(skillRef.SkillId);
            if (skill == null || string.IsNullOrEmpty(skill.DescId))
            {
                continue;
            }

            if (ContainsIgnoreCase(translate(skill.DescId), query)
                || ContainsIgnoreCase(skill.DescId, query))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<CardDto> SlicePage(IReadOnlyList<CardDto> cards, int page, int pageSize)
    {
        if (cards.Count == 0 || pageSize <= 0 || page < 0)
        {
            return Array.Empty<CardDto>();
        }

        var start = page * pageSize;
        if (start >= cards.Count)
        {
            return Array.Empty<CardDto>();
        }

        var count = Math.Min(pageSize, cards.Count - start);
        var slice = new CardDto[count];
        for (var i = 0; i < count; i++)
        {
            slice[i] = cards[start + i];
        }

        return slice;
    }

    public static int TotalPages(int itemCount, int pageSize)
    {
        if (itemCount <= 0 || pageSize <= 0)
        {
            return 0;
        }

        return (itemCount + pageSize - 1) / pageSize;
    }

    public static IReadOnlyList<string> CollectTags(IEnumerable<CardDto> cards) =>
        cards
            .Where(c => !c.HideInDex)
            .SelectMany(c => c.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

    private static bool MatchEnum<T>(T actual, CardFilterCondition condition) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(condition.ValueId, ignoreCase: false, out var expected))
        {
            return false;
        }

        return condition.Op switch
        {
            ECardFilterOp.Equal => EqualityComparer<T>.Default.Equals(actual, expected),
            ECardFilterOp.NotEqual => !EqualityComparer<T>.Default.Equals(actual, expected),
            _ => false,
        };
    }

    private static bool MatchCost(int cost, CardFilterCondition condition)
    {
        if (!int.TryParse(condition.ValueId, out var expected))
        {
            return false;
        }

        return condition.Op switch
        {
            ECardFilterOp.Equal => cost == expected,
            ECardFilterOp.LessOrEqual => cost <= expected,
            ECardFilterOp.GreaterOrEqual => cost >= expected,
            _ => false,
        };
    }

    private static bool MatchElement(int flags, CardFilterCondition condition)
    {
        if (!Enum.TryParse<EElement>(condition.ValueId, out var element) || element == EElement.None)
        {
            return false;
        }

        var bit = (int)element;
        return condition.Op switch
        {
            ECardFilterOp.Contains => (flags & bit) != 0,
            ECardFilterOp.Exact => flags == bit,
            _ => false,
        };
    }

    private static bool MatchTag(IReadOnlyList<string> tags, CardFilterCondition condition)
    {
        var value = condition.ValueId;
        return condition.Op switch
        {
            ECardFilterOp.Contains => tags.Any(t => string.Equals(t, value, StringComparison.Ordinal)),
            ECardFilterOp.Exact => tags.Count == 1 && string.Equals(tags[0], value, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool ContainsIgnoreCase(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}