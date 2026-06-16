using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed class ContentDefinitionValidationError
{
	public ContentDefinitionValidationError(
		ContentCategory category,
		string definitionId,
		string message)
	{
		Category = category;
		DefinitionId = definitionId;
		Message = message;
	}

	public ContentCategory Category { get; }

	public string DefinitionId { get; }

	public string Message { get; }
}

public sealed class ContentDefinitionValidator
{
	public List<ContentDefinitionValidationError> Validate(GameDefinitionStore store)
	{
		var errors = new List<ContentDefinitionValidationError>();
		ValidateCardGroups(store, errors);
		ValidateCards(store, errors);
		ValidateSkills(store, errors);
		ValidateBuffs(store, errors);
		ValidateEffects(store, errors);
		return errors;
	}

	private static void ValidateCardGroups(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		var tierByGroup = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
		foreach (var card in store.Cards.Values)
		{
			if (string.IsNullOrWhiteSpace(card.CardGroupId))
			{
				continue;
			}

			var tiers = tierByGroup.GetValueOrDefault(card.CardGroupId)
				?? new HashSet<int>();
			if (tiers.Contains(card.UpgradeTier))
			{
				errors.Add(new ContentDefinitionValidationError(
					ContentCategory.Card,
					card.Id,
					$"Duplicate upgradeTier {card.UpgradeTier} in cardGroup '{card.CardGroupId}'."));
			}

			tiers.Add(card.UpgradeTier);
			tierByGroup[card.CardGroupId] = tiers;
		}
	}

	private static void ValidateCards(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var card in store.Cards.Values)
		{
			foreach (var skillRef in card.SkillRefs)
			{
				if (!store.TryGetSkill(skillRef.SkillId, out _))
				{
					errors.Add(new ContentDefinitionValidationError(
						ContentCategory.Card,
						card.Id,
						$"Unknown skillId '{skillRef.SkillId}'."));
				}
			}
		}
	}

	private static void ValidateSkills(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var skill in store.Skills.Values)
		{
			ValidateEffectRefs(
				ContentCategory.Skill,
				skill.Id,
				skill.EffectRefs,
				store,
				errors);
		}
	}

	private static void ValidateBuffs(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var buff in store.Buffs.Values)
		{
			ValidateEffectRefs(ContentCategory.Buff, buff.Id, buff.Hooks.OnApply, store, errors);
			ValidateEffectRefs(ContentCategory.Buff, buff.Id, buff.Hooks.OnTurnStart, store, errors);
			ValidateEffectRefs(ContentCategory.Buff, buff.Id, buff.Hooks.OnTurnEnd, store, errors);
			ValidateEffectRefs(ContentCategory.Buff, buff.Id, buff.Hooks.OnStackChanged, store, errors);
			ValidateEffectRefs(ContentCategory.Buff, buff.Id, buff.Hooks.OnRemove, store, errors);
		}
	}

	private static void ValidateEffects(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var effect in store.Effects.Values)
		{
			if (effect.Kind == EEffectKind.ExecuteScript && string.IsNullOrWhiteSpace(effect.ScriptPath))
			{
				errors.Add(new ContentDefinitionValidationError(
					ContentCategory.Effect,
					effect.Id,
					"ExecuteScript requires scriptPath."));
			}

			if (effect.Kind == EEffectKind.ChainEffects)
			{
				ValidateEffectRefs(ContentCategory.Effect, effect.Id, effect.EffectRefs, store, errors);
			}

			if (effect.Kind == EEffectKind.ApplyBuff)
			{
				ValidateBuffIdInParams(effect, store, errors);
			}
		}
	}

	private static void ValidateBuffIdInParams(
		EffectDto effect,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		var buffId = GetStringParam(effect.Params, "buffId");
		if (buffId is null)
		{
			errors.Add(new ContentDefinitionValidationError(
				ContentCategory.Effect,
				effect.Id,
				"ApplyBuff requires params.buffId."));
			return;
		}

		if (!store.TryGetBuff(buffId, out _))
		{
			errors.Add(new ContentDefinitionValidationError(
				ContentCategory.Effect,
				effect.Id,
				$"Unknown buffId '{buffId}'."));
		}
	}

	private static void ValidateEffectRefs(
		ContentCategory category,
		string definitionId,
		IReadOnlyList<EffectRefDto> effectRefs,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		foreach (var effectRef in effectRefs)
		{
			if (!store.TryGetEffect(effectRef.EffectId, out _))
			{
				errors.Add(new ContentDefinitionValidationError(
					category,
					definitionId,
					$"Unknown effectId '{effectRef.EffectId}'."));
			}
		}
	}

	private static string? GetStringParam(Dictionary<string, object>? parameters, string key)
	{
		if (parameters is null || !parameters.TryGetValue(key, out var value))
		{
			return null;
		}

		if (value is string text)
		{
			return text;
		}

		if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.String)
		{
			return element.GetString();
		}

		return value.ToString();
	}
}
