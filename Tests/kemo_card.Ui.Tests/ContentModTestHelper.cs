namespace KemoCard.Ui.Tests;

using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

internal static class ContentModTestHelper
{
    public static string CreateModFolder(
        string root,
        string folderName,
        string modId,
        int loadOrder = 0,
        string[]? required = null,
        string version = "1.0.0")
    {
        var dir = Path.Combine(root, folderName);
        Directory.CreateDirectory(Path.Combine(dir, "content", "characters"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "enemies"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "battles"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "events"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "items"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "cards"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "skills"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "buffs"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "effects"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "skill_actions"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "attributes"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "gameplay_effects"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "tags"));
        Directory.CreateDirectory(Path.Combine(dir, "content", "stories"));
        var requiredJson = required is { Length: > 0 }
            ? string.Join(", ", required.Select(static r => $"\"{r}\""))
            : "";
        var manifest = $$"""
		{
		  "modId": "{{modId}}",
		  "displayName": "{{modId}}",
		  "version": "{{version}}",
		  "loadOrder": {{loadOrder}},
		  "dependencies": { "required": [{{requiredJson}}], "optional": [] },
		  "contentRoot": "content"
		}
		""";
        File.WriteAllText(Path.Combine(dir, "mod.json"), manifest);
        return dir;
    }

    public static void AddCharacter(string modDir, string characterId, string json = "{}")
    {
        WriteJson(modDir, "characters", characterId, json);
    }

    public static void AddEnemy(string modDir, string enemyId, string json = "{}")
    {
        WriteJson(modDir, "enemies", enemyId, json);
    }

    public static void AddBattle(string modDir, string battleId, string json = "{}")
    {
        WriteJson(modDir, "battles", battleId, json);
    }

    public static void AddEvent(string modDir, string eventId, string json = "{}")
    {
        WriteJson(modDir, "events", eventId, json);
    }

    public static void AddItem(string modDir, string itemId, string json = "{}")
    {
        WriteJson(modDir, "items", itemId, json);
    }

    public static void AddCard(string modDir, string cardId, string json = "{}")
    {
        WriteJson(modDir, "cards", cardId, json);
    }

    public static void AddSkill(string modDir, string skillId, string json = "{}")
    {
        WriteJson(modDir, "skills", skillId, json);
    }

    public static void AddEffect(string modDir, string effectId, string json = "{}")
    {
        WriteJson(modDir, "effects", effectId, json);
    }

    public static void AddBuff(string modDir, string buffId, string json = "{}")
    {
        WriteJson(modDir, "buffs", buffId, json);
    }

    public static void AddSkillAction(string modDir, string actionId, string json = "{}")
    {
        WriteJson(modDir, "skill_actions", actionId, json);
    }

    public static void AddAttribute(string modDir, string attributeId, string json = "{}")
    {
        WriteJson(modDir, "attributes", attributeId, json);
    }

    public static void AddGameplayEffect(string modDir, string gameplayEffectId, string json = "{}")
    {
        WriteJson(modDir, "gameplay_effects", gameplayEffectId, json);
    }

    public static void AddGameplayTag(string modDir, string gameplayTagId, string json = "{}")
    {
        WriteJson(modDir, "tags", gameplayTagId, json);
    }

    public static void AddStory(string modDir, string storyId, string json = "{}")
    {
        WriteJson(modDir, "stories", storyId, json);
    }

    /// <summary>从磁盘扫描并加载指定 mod 的 bundle（复用 ContentModLoader 加载路径）。</summary>
    public static ModContentBundle CreateBundleFromFolder(string root, string modId)
    {
        var discovery = new ContentModDiscovery();
        var scan = discovery.Scan(root);
        var entry = scan.ValidMods.First(m => m.Manifest.ModId == modId);
        return ContentModLoader.Load(entry);
    }

    public static void AddScript(string modDir, string relativePath, string jsSource)
    {
        var path = Path.Combine(modDir, "scripts", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, jsSource);
    }

    public static ModContentBundle EmptyBundle(string modId) => new(modId, ModDefinitionsBundle.Empty);

    public static ModContentBundle Bundle(string modId, ModDefinitionsBundle definitions) =>
        new(modId, definitions);

    private static void WriteJson(string modDir, string folder, string id, string json)
    {
        var path = Path.Combine(modDir, "content", folder, id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}