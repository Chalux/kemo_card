using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Effects;

public sealed class SkillActionExecutor
{
	private readonly GameDefinitionRegistry _registry;
	private readonly GameplayEffectApplicator _gameplayEffectApplicator;
	private readonly Action<EffectRefDto, CombatSimulation, CombatTargetRef, IReadOnlyList<CombatTargetRef>> _executeLegacyEffectRef;

	public SkillActionExecutor(
		GameDefinitionRegistry registry,
		GameplayEffectApplicator gameplayEffectApplicator,
		Action<EffectRefDto, CombatSimulation, CombatTargetRef, IReadOnlyList<CombatTargetRef>> executeLegacyEffectRef)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(gameplayEffectApplicator);
		ArgumentNullException.ThrowIfNull(executeLegacyEffectRef);
		_registry = registry;
		_gameplayEffectApplicator = gameplayEffectApplicator;
		_executeLegacyEffectRef = executeLegacyEffectRef;
	}

	public void ExecuteSkillActionRef(
		SkillActionRefDto actionRef,
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets)
	{
		ArgumentNullException.ThrowIfNull(actionRef);
		ArgumentNullException.ThrowIfNull(simulation);
		ArgumentNullException.ThrowIfNull(targets);

		if (!_registry.Store.TryGetSkillAction(actionRef.ActionId, out var action))
			return;

		var mergedParams = MergeParams(action.Params, actionRef.Params);
		ExecuteAction(action, mergedParams, simulation, source, targets);
	}

	public void ExecuteLegacyAction(
		EEffectKind kind,
		EffectDto effect,
		IReadOnlyDictionary<string, object> mergedParams,
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets)
	{
		var legacyAction = new SkillActionDto
		{
			Id = effect.Id,
			Kind = kind switch
			{
				EEffectKind.Draw => ESkillActionKind.Draw,
				EEffectKind.Discard => ESkillActionKind.Discard,
				EEffectKind.GainResource => ESkillActionKind.GainResource,
				EEffectKind.ExecuteScript => ESkillActionKind.ExecuteScript,
				EEffectKind.ChainEffects => ESkillActionKind.ChainActions,
				_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
			},
			ScriptPath = effect.ScriptPath,
			ScriptEntry = effect.ScriptEntry,
			ActionRefs = kind == EEffectKind.ChainEffects
				? []
				: [],
		};

		if (kind == EEffectKind.ChainEffects)
		{
			foreach (var child in effect.EffectRefs)
				_executeLegacyEffectRef(child, simulation, source, targets);
			return;
		}

		ExecuteAction(legacyAction, mergedParams, simulation, source, targets);
	}

	private void ExecuteAction(
		SkillActionDto action,
		IReadOnlyDictionary<string, object> mergedParams,
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets)
	{
		switch (action.Kind)
		{
			case ESkillActionKind.Draw:
				ApplyDraw(simulation, source, targets, ReadInt(mergedParams, "count", 1));
				break;
			case ESkillActionKind.Discard:
				ApplyDiscard(simulation, source, targets, ReadInt(mergedParams, "count", 1));
				break;
			case ESkillActionKind.GainResource:
				ApplyGainResource(simulation, source, targets, mergedParams);
				break;
			case ESkillActionKind.ExecuteScript:
				ApplyExecuteScript(action, mergedParams, simulation, source, targets);
				break;
			case ESkillActionKind.ChainActions:
				foreach (var child in action.ActionRefs)
					ExecuteSkillActionRef(child, simulation, source, targets);
				break;
			case ESkillActionKind.ApplyGameplayEffect:
				ApplyGameplayEffect(simulation, source, targets, mergedParams);
				break;
			case ESkillActionKind.RemoveGameplayEffect:
				RemoveGameplayEffect(simulation, targets, mergedParams);
				break;
		}
	}

	private void ApplyExecuteScript(
		SkillActionDto action,
		IReadOnlyDictionary<string, object> mergedParams,
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets)
	{
		if (string.IsNullOrWhiteSpace(action.ScriptPath))
			return;

		var context = BuildScriptContext(mergedParams, simulation, source);
		var entry = string.IsNullOrWhiteSpace(action.ScriptEntry) ? "execute" : action.ScriptEntry;
		if (!simulation.ScriptHost.TryExecute(
				simulation.ModId,
				action.ScriptPath,
				entry,
				context,
				out var proposedEffects))
		{
			return;
		}

		ExecuteProposedEffects(proposedEffects, simulation, source, targets);
	}

	private void ExecuteProposedEffects(
		IReadOnlyList<Dictionary<string, object>> proposedEffects,
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets)
	{
		foreach (var proposed in proposedEffects)
		{
			if (TryGetString(proposed, "effectId", out var effectId))
			{
				_executeLegacyEffectRef(
					new EffectRefDto
					{
						EffectId = effectId,
						Params = ExtractParamsDictionary(proposed),
					},
					simulation,
					source,
					targets);
				continue;
			}

			if (TryGetString(proposed, "actionId", out var actionId))
			{
				ExecuteSkillActionRef(
					new SkillActionRefDto
					{
						ActionId = actionId,
						Params = ExtractParamsDictionary(proposed),
					},
					simulation,
					source,
					targets);
				continue;
			}

			if (!TryGetString(proposed, "kind", out var kindText) ||
				!Enum.TryParse<ESkillActionKind>(kindText, ignoreCase: true, out var kind))
			{
				continue;
			}

			var inlineParams = ExtractParamsDictionary(proposed) ?? new Dictionary<string, object>(StringComparer.Ordinal);
			var inlineAction = new SkillActionDto
			{
				Id = kindText,
				Kind = kind,
				Params = inlineParams,
				ScriptPath = TryGetString(proposed, "scriptPath", out var scriptPath) ? scriptPath : null,
				ScriptEntry = TryGetString(proposed, "scriptEntry", out var scriptEntry) ? scriptEntry : null,
			};
			ExecuteAction(inlineAction, inlineParams, simulation, source, targets);
		}
	}

	private void ApplyGameplayEffect(
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets,
		IReadOnlyDictionary<string, object> parameters)
	{
		if (!TryGetString(parameters, "gameplayEffectId", out var gameplayEffectId))
			return;

		_gameplayEffectApplicator.ApplyToTargets(simulation, source, targets, gameplayEffectId, parameters);
	}

	private void RemoveGameplayEffect(
		CombatSimulation simulation,
		IReadOnlyList<CombatTargetRef> targets,
		IReadOnlyDictionary<string, object> parameters)
	{
		if (!TryGetString(parameters, "gameplayEffectId", out var gameplayEffectId))
			return;

		_gameplayEffectApplicator.RemoveFromTargets(simulation, targets, gameplayEffectId);
	}

	private static Dictionary<string, object>? BuildScriptContext(
		IReadOnlyDictionary<string, object> mergedParams,
		CombatSimulation simulation,
		CombatTargetRef source)
	{
		var context = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["runSeed"] = simulation.RunSeed,
			["sourceSide"] = source.Side.ToString(),
			["sourceIndex"] = source.Index,
		};

		foreach (var (key, value) in mergedParams)
			context[key] = value;

		return context;
	}

	private static void ApplyDraw(
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets,
		int count)
	{
		foreach (var character in ResolvePlayerCharacters(simulation, source, targets))
			character.DrawCards(count);
	}

	private static void ApplyDiscard(
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets,
		int count)
	{
		foreach (var character in ResolvePlayerCharacters(simulation, source, targets))
			character.DiscardFromHand(count);
	}

	private static void ApplyGainResource(
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets,
		IReadOnlyDictionary<string, object> parameters)
	{
		if (!IsEnergyResource(parameters))
			return;

		var amount = ReadInt(parameters, "amount", 0);
		if (amount <= 0)
			return;

		foreach (var character in ResolvePlayerCharacters(simulation, source, targets))
			character.GainEnergy(amount);
	}

	private static IEnumerable<CharacterBattleInstance> ResolvePlayerCharacters(
		CombatSimulation simulation,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets)
	{
		var indices = new SortedSet<int>();
		foreach (var target in targets)
		{
			if (target.Side == ECombatSide.Player && target.Index >= 0)
				indices.Add(target.Index);
		}

		if (indices.Count == 0 && source.Side == ECombatSide.Player && source.Index >= 0)
			indices.Add(source.Index);

		foreach (var index in indices)
		{
			if (index < simulation.PlayerTeam.Characters.Count)
				yield return simulation.PlayerTeam.Characters[index];
		}
	}

	private static bool IsEnergyResource(IReadOnlyDictionary<string, object> parameters)
	{
		if (!parameters.TryGetValue("resource", out var value) || value is null)
			return true;

		return string.Equals(value.ToString(), "Energy", StringComparison.OrdinalIgnoreCase);
	}

	private static Dictionary<string, object>? ExtractParamsDictionary(IReadOnlyDictionary<string, object> proposed)
	{
		if (!proposed.TryGetValue("params", out var value) || value is null)
			return null;

		return value switch
		{
			Dictionary<string, object> dict => new Dictionary<string, object>(dict, StringComparer.Ordinal),
			IReadOnlyDictionary<string, object> readOnly =>
				new Dictionary<string, object>(readOnly, StringComparer.Ordinal),
			_ => null,
		};
	}

	private static bool TryGetString(IReadOnlyDictionary<string, object> values, string key, out string result)
	{
		result = string.Empty;
		if (!values.TryGetValue(key, out var value) || value is null)
			return false;

		result = value.ToString() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(result);
	}

	private static IReadOnlyDictionary<string, object> MergeParams(
		IReadOnlyDictionary<string, object>? baseParams,
		IReadOnlyDictionary<string, object>? overrideParams)
	{
		if (baseParams is null || baseParams.Count == 0)
			return overrideParams ?? new Dictionary<string, object>(StringComparer.Ordinal);

		if (overrideParams is null || overrideParams.Count == 0)
			return baseParams;

		var merged = new Dictionary<string, object>(baseParams, StringComparer.Ordinal);
		foreach (var (key, value) in overrideParams)
			merged[key] = value;
		return merged;
	}

	private static int ReadInt(IReadOnlyDictionary<string, object> parameters, string key, int defaultValue)
	{
		if (!parameters.TryGetValue(key, out var value) || value is null)
			return defaultValue;

		return value switch
		{
			int i => i,
			long l => (int)l,
			short s => s,
			byte b => b,
			JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetInt32(),
			_ => int.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue,
		};
	}
}
