using System.Text.Json;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed class ContentModLoader
{
    /// <summary>
    /// 预解析（重写注入 id）阶段使用的宽松选项，必须与 <see cref="ContentDefinitionJson.Options"/> 保持一致：
    /// <see cref="JsonDocument.Parse(string)"/> 不继承 <c>JsonSerializerOptions</c>，
    /// 不显式传入会让允许尾逗号 / 跳过注释的配置形同虚设。
    /// </summary>
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

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

            result[id] = LoadFile<T>(filePath, id);
        }

        return result;
    }

    private static T LoadFile<T>(string filePath, string id)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return DeserializeWithId<T>(json, id);
        }
        catch (Exception ex) when (ex is not ContentModLoadException)
        {
            // 带上文件路径：JsonException 默认只报行列号，多文件 mod 下无法定位是哪个定义写错。
            throw new ContentModLoadException(
                id,
                $"definition file '{filePath}' is invalid: {ex.Message}",
                ex);
        }
    }

    private static T DeserializeWithId<T>(string json, string id)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (IsIdProperty(property))
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

    /// <summary>
    /// 内容定义 id 一律由文件名推导（content-mod-manager 规格 §4.3）。
    /// 这里必须大小写无关地跳过：反序列化使用 <c>PropertyNameCaseInsensitive</c>，
    /// 若只跳过小写 <c>id</c>，文件中写成 <c>"Id"</c> 就会覆盖文件名推导出的 id，
    /// 同一字段出现两种大小写两种行为。
    /// </summary>
    private static bool IsIdProperty(JsonProperty property) =>
        string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase);
}