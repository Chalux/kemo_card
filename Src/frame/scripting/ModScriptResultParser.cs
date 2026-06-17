using System.Collections;
using Puerts;

namespace KemoCard.Frame.Scripting;

public static class ModScriptResultParser
{
	public static bool TryParseProposedEffects(
		object? raw,
		out IReadOnlyList<Dictionary<string, object>> proposedEffects,
		out string? error)
	{
		proposedEffects = Array.Empty<Dictionary<string, object>>();
		error = null;
		if (raw is not ScriptObject scriptObject)
		{
			error = raw is null ? "Script returned null." : $"Unexpected return type: {raw.GetType().FullName}";
			return false;
		}

		if (!TryGetEffectsEnumerable(scriptObject, out var effectsEnumerable))
		{
			error = "Missing proposedEffects array.";
			return false;
		}

		var parsed = new List<Dictionary<string, object>>();
		foreach (var item in effectsEnumerable!)
		{
			if (item is not ScriptObject effectObject)
			{
				error = "proposedEffects item is not an object.";
				return false;
			}

			if (!TryGetMember(effectObject, "kind", out var kindValue) || kindValue is not string kind)
			{
				error = "proposedEffects item missing kind.";
				return false;
			}

			var dict = new Dictionary<string, object>(StringComparer.Ordinal) { ["kind"] = kind };
			if (TryGetMember(effectObject, "params", out var paramsValue) && paramsValue is not null)
			{
				dict["params"] = ConvertToDictionary(paramsValue);
			}

			parsed.Add(dict);
		}

		proposedEffects = parsed;
		return true;
	}

	public static bool TryParseStoryOptions(
		object? raw,
		out IReadOnlyList<StoryScriptOption> options,
		out string? error)
	{
		options = Array.Empty<StoryScriptOption>();
		error = null;
		if (raw is not ScriptObject scriptObject)
		{
			error = "Script return is not an object.";
			return false;
		}

		if (!TryGetMember(scriptObject, "options", out var optionsValue) || optionsValue is not IList optionsList)
		{
			error = "Missing options array.";
			return false;
		}

		var parsed = new List<StoryScriptOption>();
		foreach (var item in optionsList)
		{
			if (item is not ScriptObject optionObject)
			{
				error = "options item is not an object.";
				return false;
			}

			if (!TryGetMember(optionObject, "optionId", out var optionIdValue) || optionIdValue is not string optionId)
			{
				error = "options item missing optionId.";
				return false;
			}

			if (!TryGetMember(optionObject, "labelId", out var labelIdValue) || labelIdValue is not string labelId)
			{
				error = "options item missing labelId.";
				return false;
			}

			string? nextNodeType = null;
			string? nextNodeId = null;
			if (TryGetMember(optionObject, "next", out var nextValue) && nextValue is ScriptObject nextObject)
			{
				if (TryGetMember(nextObject, "nodeType", out var nodeTypeValue))
				{
					nextNodeType = nodeTypeValue?.ToString();
				}

				if (TryGetMember(nextObject, "nodeId", out var nodeIdValue))
				{
					nextNodeId = nodeIdValue?.ToString();
				}
			}

			parsed.Add(new StoryScriptOption(optionId, labelId, nextNodeType, nextNodeId));
		}

		options = parsed;
		return true;
	}

	public static bool TryParseEventResult(
		object? raw,
		out EventScriptResult result,
		out string? error)
	{
		result = EventScriptResult.Empty;
		error = null;
		if (raw is not ScriptObject scriptObject)
		{
			error = "Script return is not an object.";
			return false;
		}

		var pages = ReadStringList(scriptObject, "pages");
		var options = new List<EventScriptOption>();
		if (TryGetMember(scriptObject, "options", out var optionsValue) && optionsValue is IList optionsList)
		{
			foreach (var item in optionsList)
			{
				if (item is not ScriptObject optionObject)
				{
					continue;
				}

				var optionId = TryGetMember(optionObject, "optionId", out var idValue) ? idValue?.ToString() ?? "" : "";
				var labelId = TryGetMember(optionObject, "labelId", out var labelValue) ? labelValue?.ToString() ?? "" : "";
				if (string.IsNullOrWhiteSpace(optionId) || string.IsNullOrWhiteSpace(labelId))
				{
					continue;
				}

				options.Add(new EventScriptOption(optionId, labelId));
			}
		}

		result = new EventScriptResult(pages, options);
		return true;
	}

	public static bool TryParseBattleResult(
		object? raw,
		out BattleScriptResult result,
		out string? error)
	{
		result = BattleScriptResult.Empty;
		error = null;
		if (raw is not ScriptObject scriptObject)
		{
			error = "Script return is not an object.";
			return false;
		}

		var phase = TryGetMember(scriptObject, "phase", out var phaseValue) ? phaseValue?.ToString() : null;
		var hooks = ReadStringList(scriptObject, "hooks");
		result = new BattleScriptResult(phase, hooks);
		return true;
	}

	public static bool TryParseEnemyAiResult(
		object? raw,
		out EnemyAiScriptResult result,
		out string? error)
	{
		result = EnemyAiScriptResult.Empty;
		error = null;
		if (raw is not ScriptObject scriptObject)
		{
			error = "Script return is not an object.";
			return false;
		}

		if (TryGetMember(scriptObject, "skillId", out var skillIdValue) && skillIdValue is string skillId)
		{
			result = new EnemyAiScriptResult(skillId, null);
			return true;
		}

		if (TryGetMember(scriptObject, "weights", out var weightsValue) && weightsValue is not null)
		{
			var weights = ConvertToDictionary(weightsValue);
			var numericWeights = weights.ToDictionary(
				static pair => pair.Key,
				static pair => Convert.ToInt32(pair.Value),
				StringComparer.Ordinal);
			result = new EnemyAiScriptResult(null, numericWeights);
			return numericWeights.Count > 0;
		}

		error = "Missing skillId or weights.";
		return false;
	}

	private static bool TryGetEffectsEnumerable(ScriptObject scriptObject, out IEnumerable? enumerable)
	{
		enumerable = null;
		if (!TryGetMember(scriptObject, "proposedEffects", out var effectsValue) || effectsValue is null)
		{
			return false;
		}

		if (effectsValue is IEnumerable effects && effectsValue is not string)
		{
			enumerable = effects;
			return true;
		}

		if (effectsValue is ScriptObject arrayObject)
		{
			if (TryGetIndexedObjects(arrayObject, out var indexedItems))
			{
				enumerable = indexedItems;
				return true;
			}
		}

		try
		{
			var arr = scriptObject.Get<object[]>("proposedEffects");
			if (arr is not null)
			{
				enumerable = arr;
				return true;
			}
		}
		catch
		{
			// fall through
		}

		return false;
	}

	private static bool TryGetIndexedObjects(ScriptObject arrayObject, out List<object> items)
	{
		items = [];
		try
		{
			var length = arrayObject.Get<int>("length");
			for (var i = 0; i < length; i++)
			{
				items.Add(arrayObject.Get<object>(i.ToString()));
			}

			return items.Count > 0;
		}
		catch
		{
			for (var i = 0; i < 32; i++)
			{
				try
				{
					items.Add(arrayObject.Get<object>(i.ToString()));
				}
				catch
				{
					break;
				}
			}

			return items.Count > 0;
		}
	}

	private static bool TryGetMember(ScriptObject scriptObject, string key, out object? value)
	{
		value = null;
		try
		{
			value = scriptObject.Get<object>(key);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static IReadOnlyList<string> ReadStringList(ScriptObject scriptObject, string key)
	{
		if (!TryGetMember(scriptObject, key, out var value) || value is not IList list)
		{
			return Array.Empty<string>();
		}

		var result = new List<string>();
		foreach (var item in list)
		{
			if (item is string text)
			{
				result.Add(text);
			}
		}

		return result;
	}

	private static Dictionary<string, object> ConvertToDictionary(object value)
	{
		if (value is IDictionary<string, object> dictionary)
		{
			return new Dictionary<string, object>(dictionary, StringComparer.Ordinal);
		}

		return new Dictionary<string, object>(StringComparer.Ordinal);
	}
}

public sealed record StoryScriptOption(
	string OptionId,
	string LabelId,
	string? NextNodeType,
	string? NextNodeId);

public sealed record EventScriptOption(string OptionId, string LabelId);

public sealed record EventScriptResult(
	IReadOnlyList<string> Pages,
	IReadOnlyList<EventScriptOption> Options)
{
	public static EventScriptResult Empty { get; } = new([], []);
}

public sealed record BattleScriptResult(string? Phase, IReadOnlyList<string> Hooks)
{
	public static BattleScriptResult Empty { get; } = new(null, []);
}

public sealed record EnemyAiScriptResult(
	string? SkillId,
	IReadOnlyDictionary<string, int>? Weights)
{
	public static EnemyAiScriptResult Empty { get; } = new(null, null);
}
