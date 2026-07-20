using System;
using System.Collections.Generic;
using System.Linq;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

public enum ECharFilterField
{
	Element,
	Role,
	Race,
	Tag,
}

public readonly record struct CharFilterCondition(
	ECharFilterField Field,
	ECardFilterOp Op,
	string ValueId,
	string DisplayText);

public static class CharacterCodexQuery
{
	public const int PageSize = 8;

	public static IReadOnlyList<ECardFilterOp> OpsForField(ECharFilterField field) => field switch
	{
		ECharFilterField.Role => [ECardFilterOp.Equal, ECardFilterOp.NotEqual],
		ECharFilterField.Element or ECharFilterField.Race or ECharFilterField.Tag =>
			[ECardFilterOp.Contains, ECardFilterOp.Exact],
		_ => [ECardFilterOp.Equal],
	};

	public static bool MatchesCondition(CharacterDto character, CharFilterCondition condition)
	{
		return condition.Field switch
		{
			ECharFilterField.Element => MatchFlags((int)character.Element, condition, EElement.None),
			ECharFilterField.Race => MatchFlags((int)character.Race, condition, ERace.None),
			ECharFilterField.Role => MatchEnum(character.Role, condition),
			ECharFilterField.Tag => MatchTag(character.Tags, condition),
			_ => false,
		};
	}

	public static IReadOnlyList<CharacterDto> Filter(
		IEnumerable<CharacterDto> characters,
		IReadOnlyList<CharFilterCondition> conditions,
		string textQuery,
		Func<string, string> translate,
		Func<string, SkillDto?> tryGetSkill)
	{
		var query = textQuery?.Trim() ?? "";
		return characters
			.Where(c => conditions.All(cond => MatchesCondition(c, cond)))
			.Where(c => string.IsNullOrEmpty(query) || MatchesText(c, query, translate, tryGetSkill))
			.OrderBy(c => c.Id, StringComparer.Ordinal)
			.ToList();
	}

	public static bool MatchesText(
		CharacterDto character,
		string query,
		Func<string, string> translate,
		Func<string, SkillDto?> tryGetSkill)
	{
		if (ContainsIgnoreCase(translate(character.DisplayNameId), query)
			|| ContainsIgnoreCase(character.DisplayNameId, query))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(character.DescId)
			&& (ContainsIgnoreCase(translate(character.DescId), query)
				|| ContainsIgnoreCase(character.DescId, query)))
		{
			return true;
		}

		foreach (var skillRef in character.SkillRefs)
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

	public static IReadOnlyList<CharacterDto> SlicePage(IReadOnlyList<CharacterDto> characters, int page, int pageSize)
	{
		if (characters.Count == 0 || pageSize <= 0 || page < 0)
		{
			return Array.Empty<CharacterDto>();
		}

		var start = page * pageSize;
		if (start >= characters.Count)
		{
			return Array.Empty<CharacterDto>();
		}

		var count = Math.Min(pageSize, characters.Count - start);
		var slice = new CharacterDto[count];
		for (var i = 0; i < count; i++)
		{
			slice[i] = characters[start + i];
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

	public static IReadOnlyList<string> CollectTags(IEnumerable<CharacterDto> characters) =>
		characters
			.SelectMany(c => c.Tags)
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(t => t, StringComparer.Ordinal)
			.ToList();

	private static bool MatchEnum<T>(T actual, CharFilterCondition condition) where T : struct, Enum
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

	private static bool MatchFlags<T>(int flags, CharFilterCondition condition, T noneValue) where T : struct, Enum
	{
		if (!Enum.TryParse<T>(condition.ValueId, out var value) || EqualityComparer<T>.Default.Equals(value, noneValue))
		{
			return false;
		}

		var bit = (int)(object)value;
		return condition.Op switch
		{
			ECardFilterOp.Contains => (flags & bit) != 0,
			ECardFilterOp.Exact => flags == bit,
			_ => false,
		};
	}

	private static bool MatchTag(IReadOnlyList<string> tags, CharFilterCondition condition)
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
