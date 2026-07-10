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
}
