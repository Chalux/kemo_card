using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;

namespace KemoCard.Frame.Content;

public sealed class ContentDefinitionValidationError
{
	public ContentDefinitionValidationError(
		EContentCategory category,
		string definitionId,
		string message)
	{
		Category = category;
		DefinitionId = definitionId;
		Message = message;
	}

	public EContentCategory Category { get; }

	public string DefinitionId { get; }

	public string Message { get; }
}

public sealed class ContentDefinitionValidator
{
	public List<ContentDefinitionValidationError> Validate(GameDefinitionStore store)
	{
		var errors = new List<ContentDefinitionValidationError>();
		ValidateCardGroups(store, errors);
		ValidateCharacters(store, errors);
		ValidateEnemies(store, errors);
		ValidateBattles(store, errors);
		ValidateEvents(store, errors);
		ValidateItems(store, errors);
		ValidateCards(store, errors);
		ValidateSkills(store, errors);
		ValidateBuffs(store, errors);
		ValidateEffects(store, errors);
		ValidateSkillActions(store, errors);
		ValidateGameplayTags(store, errors);
		ValidateGameplayEffects(store, errors);
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
					EContentCategory.Card,
					card.Id,
					$"Duplicate upgradeTier {card.UpgradeTier} in cardGroup '{card.CardGroupId}'."));
			}

			tiers.Add(card.UpgradeTier);
			tierByGroup[card.CardGroupId] = tiers;
		}
	}

	private static void ValidateCharacters(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var character in store.Characters.Values)
		{
			ValidateSkillRefs(EContentCategory.Character, character.Id, character.SkillRefs, store, errors);
			ValidateBuffRefs(EContentCategory.Character, character.Id, character.BuffRefs, store, errors);
			foreach (var cardId in character.Cards)
			{
				if (!store.TryGetCard(cardId, out _))
				{
					errors.Add(new ContentDefinitionValidationError(
						EContentCategory.Character,
						character.Id,
						$"Unknown cardId '{cardId}'."));
				}
			}
		}
	}

	private static void ValidateEnemies(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var enemy in store.Enemies.Values)
		{
			var maxHealth = enemy.BaseAttributes.TryGetValue(AttributeIds.MaxHealth, out var value)
				? value
				: enemy.MaxHp;
			if (maxHealth <= 0f)
			{
				errors.Add(new ContentDefinitionValidationError(
					EContentCategory.Enemy,
					enemy.Id,
					"baseAttributes.MaxHealth/maxHp must be greater than 0."));
			}

			ValidateSkillRefs(EContentCategory.Enemy, enemy.Id, enemy.SkillRefs, store, errors);
			ValidateBuffRefs(EContentCategory.Enemy, enemy.Id, enemy.BuffRefs, store, errors);
		}
	}

	private static void ValidateBattles(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var battle in store.Battles.Values)
		{
			if (battle.Waves.Count == 0)
			{
				errors.Add(new ContentDefinitionValidationError(
					EContentCategory.Battle,
					battle.Id,
					"waves must not be empty."));
			}

			for (var waveIndex = 0; waveIndex < battle.Waves.Count; waveIndex++)
			{
				var wave = battle.Waves[waveIndex];
				if (wave.EnemySpawns.Count == 0)
				{
					errors.Add(new ContentDefinitionValidationError(
						EContentCategory.Battle,
						battle.Id,
						$"waves[{waveIndex}].enemySpawns must not be empty."));
				}

				foreach (var spawn in wave.EnemySpawns)
				{
					if (!store.TryGetEnemy(spawn.EnemyId, out _))
					{
						errors.Add(new ContentDefinitionValidationError(
							EContentCategory.Battle,
							battle.Id,
							$"Unknown enemyId '{spawn.EnemyId}'."));
					}

					if (spawn.SkillOverrides is not null)
					{
						ValidateSkillRefs(
							EContentCategory.Battle,
							battle.Id,
							spawn.SkillOverrides,
							store,
							errors);
					}
				}
			}

			ValidateBattleRewards(battle, store, errors);
		}
	}

	private static void ValidateBattleRewards(
		BattleDto battle,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		foreach (var entry in battle.Rewards.Entries)
		{
			switch (entry.Kind)
			{
				case ERewardKind.ItemGrant:
					ValidateRewardItemId(battle.Id, entry, store, errors);
					break;
				case ERewardKind.Effect:
					ValidateRewardEffectId(battle.Id, entry, store, errors);
					break;
				case ERewardKind.CardChoice:
					ValidateRewardCardIds(battle.Id, entry, store, errors);
					break;
			}
		}
	}

	private static void ValidateRewardItemId(
		string battleId,
		RewardEntryDto entry,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		var itemId = GetStringParam(entry.Params, "itemId");
		if (itemId is null)
		{
			errors.Add(new ContentDefinitionValidationError(
				EContentCategory.Battle,
				battleId,
				"ItemGrant reward requires params.itemId."));
			return;
		}

		if (!store.TryGetItem(itemId, out _))
		{
			errors.Add(new ContentDefinitionValidationError(
				EContentCategory.Battle,
				battleId,
				$"Unknown itemId '{itemId}' in reward."));
		}
	}

	private static void ValidateRewardEffectId(
		string battleId,
		RewardEntryDto entry,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		var effectId = GetStringParam(entry.Params, "effectId");
		if (effectId is null)
		{
			errors.Add(new ContentDefinitionValidationError(
				EContentCategory.Battle,
				battleId,
				"Effect reward requires params.effectId."));
			return;
		}

		if (!store.TryGetEffect(effectId, out _))
		{
			errors.Add(new ContentDefinitionValidationError(
				EContentCategory.Battle,
				battleId,
				$"Unknown effectId '{effectId}' in reward."));
		}
	}

	private static void ValidateRewardCardIds(
		string battleId,
		RewardEntryDto entry,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		var cardIds = GetStringListParam(entry.Params, "cardIds");
		if (cardIds is null)
		{
			return;
		}

		foreach (var cardId in cardIds)
		{
			if (!store.TryGetCard(cardId, out _))
			{
				errors.Add(new ContentDefinitionValidationError(
					EContentCategory.Battle,
					battleId,
					$"Unknown cardId '{cardId}' in reward."));
			}
		}
	}

	private static void ValidateEvents(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var eventDef in store.Events.Values)
		{
			switch (eventDef.EventKind)
			{
				case EEventKind.Data:
					if (eventDef.Options.Count == 0)
					{
						errors.Add(new ContentDefinitionValidationError(
							EContentCategory.Event,
							eventDef.Id,
							"Data event requires at least one option."));
					}

					foreach (var option in eventDef.Options)
					{
						ValidateEffectRefs(
							EContentCategory.Event,
							eventDef.Id,
							option.EffectRefs,
							store,
							errors);
					}

					break;
				case EEventKind.Script:
					if (string.IsNullOrWhiteSpace(eventDef.ScriptPath))
					{
						errors.Add(new ContentDefinitionValidationError(
							EContentCategory.Event,
							eventDef.Id,
							"Script event requires scriptPath."));
					}

					break;
			}
		}
	}

	private static void ValidateItems(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var item in store.Items.Values)
		{
			if (item.UseSkillRefs.Count == 0)
			{
				errors.Add(new ContentDefinitionValidationError(
					EContentCategory.Item,
					item.Id,
					"useSkillRefs must not be empty."));
			}

			ValidateSkillRefs(EContentCategory.Item, item.Id, item.UseSkillRefs, store, errors);
		}
	}

	private static void ValidateCards(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var card in store.Cards.Values)
		{
			ValidateSkillRefs(EContentCategory.Card, card.Id, card.SkillRefs, store, errors);
		}
	}

	private static void ValidateSkills(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var skill in store.Skills.Values)
		{
			if (skill.ActionRefs.Count > 0)
			{
				ValidateActionRefs(EContentCategory.Skill, skill.Id, skill.ActionRefs, store, errors);
			}
			else
			{
				ValidateEffectRefs(
					EContentCategory.Skill,
					skill.Id,
					skill.EffectRefs,
					store,
					errors);
			}
		}
	}

	private static void ValidateBuffs(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var buff in store.Buffs.Values)
		{
			ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnApply, store, errors);
			ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnTurnStart, store, errors);
			ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnTurnEnd, store, errors);
			ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnStackChanged, store, errors);
			ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnRemove, store, errors);
		}
	}

	private static void ValidateEffects(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var effect in store.Effects.Values)
		{
			if (effect.Kind == EEffectKind.ExecuteScript && string.IsNullOrWhiteSpace(effect.ScriptPath))
			{
				errors.Add(new ContentDefinitionValidationError(
					EContentCategory.Effect,
					effect.Id,
					"ExecuteScript requires scriptPath."));
			}

			if (effect.Kind == EEffectKind.ChainEffects)
			{
				ValidateEffectRefs(EContentCategory.Effect, effect.Id, effect.EffectRefs, store, errors);
			}

			if (effect.Kind == EEffectKind.ApplyBuff)
			{
				ValidateBuffIdInParams(effect, store, errors);
			}
		}
	}

	private static void ValidateSkillActions(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var action in store.SkillActions.Values)
		{
			if (action.Kind == ESkillActionKind.ExecuteScript && string.IsNullOrWhiteSpace(action.ScriptPath))
			{
				errors.Add(new ContentDefinitionValidationError(
					EContentCategory.SkillAction,
					action.Id,
					"ExecuteScript requires scriptPath."));
			}

			if (action.Kind == ESkillActionKind.ChainActions)
			{
				ValidateActionRefs(EContentCategory.SkillAction, action.Id, action.ActionRefs, store, errors);
			}

			if (action.Kind is ESkillActionKind.ApplyGameplayEffect or ESkillActionKind.RemoveGameplayEffect)
			{
				var gameplayEffectId = GetStringParam(action.Params, "gameplayEffectId");
				if (string.IsNullOrWhiteSpace(gameplayEffectId))
				{
					errors.Add(new ContentDefinitionValidationError(
						EContentCategory.SkillAction,
						action.Id,
						$"{action.Kind} requires params.gameplayEffectId."));
				}
			}
		}
	}

	private static void ValidateGameplayTags(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var gameplayTag in store.GameplayTags.Values)
		{
			if (!IsValidGameplayTagString(gameplayTag.Id))
			{
				errors.Add(new ContentDefinitionValidationError(
					EContentCategory.GameplayTag,
					gameplayTag.Id,
					$"Invalid gameplay tag id '{gameplayTag.Id}'."));
			}

			if (!string.IsNullOrWhiteSpace(gameplayTag.Parent))
			{
				if (!IsValidGameplayTagString(gameplayTag.Parent!))
				{
					errors.Add(new ContentDefinitionValidationError(
						EContentCategory.GameplayTag,
						gameplayTag.Id,
						$"Invalid gameplay tag parent '{gameplayTag.Parent}'."));
				}
				else if (!store.TryGetGameplayTag(gameplayTag.Parent!, out _))
				{
					errors.Add(new ContentDefinitionValidationError(
						EContentCategory.GameplayTag,
						gameplayTag.Id,
						$"Unknown gameplay tag parent '{gameplayTag.Parent}'."));
				}
			}
		}
	}

	private static void ValidateGameplayEffects(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
	{
		foreach (var gameplayEffect in store.GameplayEffects.Values)
		{
			foreach (var modifier in gameplayEffect.Modifiers)
			{
				if (!store.TryGetAttribute(modifier.AttributeId, out _))
				{
					errors.Add(new ContentDefinitionValidationError(
						EContentCategory.GameplayEffect,
						gameplayEffect.Id,
						$"Unknown attributeId '{modifier.AttributeId}' in modifier."));
				}
			}

			if (store.GameplayTags.Count > 0)
			{
				ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.GrantedTags, store, errors);
				ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.ApplicationRequiredTags, store, errors);
				ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.ApplicationBlockedTags, store, errors);
				ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.OngoingRequiredTags, store, errors);
				ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.ImmunityTags, store, errors);
				ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.RemoveEffectsWithTags, store, errors);
			}
		}
	}

	private static void ValidateSkillRefs(
		EContentCategory category,
		string definitionId,
		IReadOnlyList<SkillRefDto> skillRefs,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		foreach (var skillRef in skillRefs)
		{
			if (!store.TryGetSkill(skillRef.SkillId, out _))
			{
				errors.Add(new ContentDefinitionValidationError(
					category,
					definitionId,
					$"Unknown skillId '{skillRef.SkillId}'."));
			}
		}
	}

	private static void ValidateBuffRefs(
		EContentCategory category,
		string definitionId,
		IReadOnlyList<BuffRefDto> buffRefs,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		foreach (var buffRef in buffRefs)
		{
			if (!store.TryGetBuff(buffRef.BuffId, out _))
			{
				errors.Add(new ContentDefinitionValidationError(
					category,
					definitionId,
					$"Unknown buffId '{buffRef.BuffId}'."));
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
				EContentCategory.Effect,
				effect.Id,
				"ApplyBuff requires params.buffId."));
			return;
		}

		if (!store.TryGetBuff(buffId, out _))
		{
			errors.Add(new ContentDefinitionValidationError(
				EContentCategory.Effect,
				effect.Id,
				$"Unknown buffId '{buffId}'."));
		}
	}

	private static void ValidateEffectRefs(
		EContentCategory category,
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

	private static void ValidateActionRefs(
		EContentCategory category,
		string definitionId,
		IReadOnlyList<SkillActionRefDto> actionRefs,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		foreach (var actionRef in actionRefs)
		{
			if (!store.TryGetSkillAction(actionRef.ActionId, out _))
			{
				errors.Add(new ContentDefinitionValidationError(
					category,
					definitionId,
					$"Unknown actionId '{actionRef.ActionId}'."));
			}
		}
	}

	private static void ValidateGameplayTagRefs(
		EContentCategory category,
		string definitionId,
		IReadOnlyList<string> tags,
		GameDefinitionStore store,
		List<ContentDefinitionValidationError> errors)
	{
		foreach (var gameplayTag in tags)
		{
			if (!IsValidGameplayTagString(gameplayTag))
			{
				errors.Add(new ContentDefinitionValidationError(
					category,
					definitionId,
					$"Invalid gameplay tag '{gameplayTag}'."));
				continue;
			}

			if (!store.TryGetGameplayTag(gameplayTag, out _))
			{
				errors.Add(new ContentDefinitionValidationError(
					category,
					definitionId,
					$"Unknown gameplay tag '{gameplayTag}'."));
			}
		}
	}

	private static bool IsValidGameplayTagString(string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
		{
			return false;
		}

		if (tag.StartsWith(".", StringComparison.Ordinal) || tag.EndsWith(".", StringComparison.Ordinal))
		{
			return false;
		}

		if (tag.Contains("..", StringComparison.Ordinal))
		{
			return false;
		}

		foreach (var ch in tag)
		{
			if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
			{
				continue;
			}

			return false;
		}

		return true;
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

	private static IReadOnlyList<string>? GetStringListParam(Dictionary<string, object>? parameters, string key)
	{
		if (parameters is null || !parameters.TryGetValue(key, out var value))
		{
			return null;
		}

		if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array)
		{
			var result = new List<string>();
			foreach (var item in element.EnumerateArray())
			{
				if (item.ValueKind == System.Text.Json.JsonValueKind.String)
				{
					var text = item.GetString();
					if (!string.IsNullOrWhiteSpace(text))
					{
						result.Add(text);
					}
				}
			}

			return result;
		}

		return null;
	}
}
