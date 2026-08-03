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
            var definitions = new ModDefinitionsBundle(
                LoadDefinitions<CharacterDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Character)),
                LoadDefinitions<EnemyDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Enemy)),
                LoadDefinitions<BattleDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Battle)),
                LoadDefinitions<EventDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Event)),
                LoadDefinitions<ItemDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Item)),
                LoadDefinitions<CardDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Card)),
                LoadDefinitions<SkillDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Skill)),
                LoadDefinitions<BuffDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Buff)),
                LoadDefinitions<EffectDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Effect)),
                LoadDefinitions<AttributeDefDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Attribute)),
                LoadDefinitions<GameplayEffectDefDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.GameplayEffect)),
                LoadDefinitions<GameplayTagDefDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.GameplayTag)),
                LoadDefinitions<SkillActionDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.SkillAction)),
                LoadDefinitions<StoryDto>(contentRoot, ContentCategoryPaths.Folder(EContentCategory.Story)));

            return new ModContentBundle(manifest.ModId, definitions);
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