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
			var cards = LoadDefinitions<CardDto>(contentRoot, "cards");
			var skills = LoadDefinitions<SkillDto>(contentRoot, "skills");
			var buffs = LoadDefinitions<BuffDto>(contentRoot, "buffs");
			var effects = LoadDefinitions<EffectDto>(contentRoot, "effects");

			return new ModContentBundle(
				manifest.ModId,
				CollectIds(contentRoot, "characters"),
				CollectIds(contentRoot, "battles"),
				CollectIds(contentRoot, "events"),
				[.. cards.Keys],
				CollectIds(contentRoot, "items"),
				[.. skills.Keys],
				[.. buffs.Keys],
				[.. effects.Keys],
				new ModDefinitionsBundle(cards, skills, buffs, effects));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
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

	private static IReadOnlyList<string> CollectIds(string contentRoot, string folderName)
	{
		var dir = Path.Combine(contentRoot, folderName);
		if (!Directory.Exists(dir))
		{
			return [];
		}

		return [.. Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
			.Select(Path.GetFileNameWithoutExtension)
			.Where(static id => !string.IsNullOrWhiteSpace(id))
			.Select(static id => id!)
			.OrderBy(static id => id, StringComparer.Ordinal)];
	}
}
