using System.Text.Json;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed class ContentModLoader
{
	public static ModContentBundle Load(DiscoveredModEntry entry)
	{
		var manifest = entry.Manifest;
		var contentRoot = Path.Combine(entry.FolderPath, manifest.ContentRoot);
		try
		{
			var characters = LoadDefinitions<CharacterDto>(contentRoot, "characters");
			var enemies = LoadDefinitions<EnemyDto>(contentRoot, "enemies");
			var battles = LoadDefinitions<BattleDto>(contentRoot, "battles");
			var events = LoadDefinitions<EventDto>(contentRoot, "events");
			var items = LoadDefinitions<ItemDto>(contentRoot, "items");
			var cards = LoadDefinitions<CardDto>(contentRoot, "cards");
			var skills = LoadDefinitions<SkillDto>(contentRoot, "skills");
			var buffs = LoadDefinitions<BuffDto>(contentRoot, "buffs");
			var effects = LoadDefinitions<EffectDto>(contentRoot, "effects");
			var skillActions = LoadDefinitions<SkillActionDto>(contentRoot, "skill_actions");
			var attributes = LoadDefinitions<AttributeDefDto>(contentRoot, "attributes");
			var gameplayEffects = LoadDefinitions<GameplayEffectDefDto>(contentRoot, "gameplay_effects");
			var gameplayTags = LoadDefinitions<GameplayTagDefDto>(contentRoot, "tags");

			return new ModContentBundle(
				manifest.ModId,
				[.. characters.Keys],
				[.. enemies.Keys],
				[.. battles.Keys],
				[.. events.Keys],
				[.. cards.Keys],
				[.. items.Keys],
				[.. skills.Keys],
				[.. buffs.Keys],
				[.. effects.Keys],
				new ModDefinitionsBundle(
					characters,
					enemies,
					battles,
					events,
					items,
					cards,
					skills,
					buffs,
					effects)
				{
					Attributes = attributes,
					GameplayEffects = gameplayEffects,
					GameplayTags = gameplayTags,
					SkillActions = skillActions,
				})
			{
				Attributes = [.. attributes.Keys],
				GameplayEffects = [.. gameplayEffects.Keys],
				GameplayTags = [.. gameplayTags.Keys],
				SkillActions = [.. skillActions.Keys],
			};
		}
		catch (Exception ex)
		{
			throw new ContentModLoadException(manifest.ModId, ex.Message, ex);
		}
	}

	private static Dictionary<string, T> LoadDefinitions<T>(string contentRoot, string folderName)
	{
		var dir = Path.Combine(contentRoot, folderName);
		if (!Directory.Exists(dir))
		{
			return new Dictionary<string, T>(StringComparer.Ordinal);
		}

		var result = new Dictionary<string, T>(StringComparer.Ordinal);
		foreach (var filePath in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
		{
			var id = Path.GetFileNameWithoutExtension(filePath);
			if (string.IsNullOrWhiteSpace(id))
			{
				continue;
			}

			var json = File.ReadAllText(filePath);
			var dto = DeserializeWithId<T>(json, id);
			result[id] = dto;
		}

		return result;
	}

	private static T DeserializeWithId<T>(string json, string id)
	{
		using var document = JsonDocument.Parse(json);
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("id", id);
			foreach (var property in document.RootElement.EnumerateObject())
			{
				if (property.NameEquals("id"))
				{
					continue;
				}

				property.WriteTo(writer);
			}

			writer.WriteEndObject();
		}

		return JsonSerializer.Deserialize<T>(stream.ToArray(), ContentDefinitionJson.Options)
			?? throw new JsonException($"Failed to deserialize definition '{id}'.");
	}
}
