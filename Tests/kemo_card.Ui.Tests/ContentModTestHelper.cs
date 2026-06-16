namespace KemoCard.Ui.Tests;

using KemoCard.Frame.Content;

internal static class ContentModTestHelper
{
	public static string CreateModFolder(
		string root,
		string folderName,
		string modId,
		int loadOrder = 0,
		string[]? required = null)
	{
		var dir = Path.Combine(root, folderName);
		Directory.CreateDirectory(Path.Combine(dir, "content", "cards"));
		Directory.CreateDirectory(Path.Combine(dir, "content", "skills"));
		Directory.CreateDirectory(Path.Combine(dir, "content", "buffs"));
		Directory.CreateDirectory(Path.Combine(dir, "content", "effects"));
		var requiredJson = required is { Length: > 0 }
			? string.Join(", ", required.Select(static r => $"\"{r}\""))
			: "";
		var manifest = $$"""
		{
		  "modId": "{{modId}}",
		  "displayName": "{{modId}}",
		  "version": "1.0.0",
		  "loadOrder": {{loadOrder}},
		  "dependencies": { "required": [{{requiredJson}}], "optional": [] },
		  "contentRoot": "content"
		}
		""";
		File.WriteAllText(Path.Combine(dir, "mod.json"), manifest);
		return dir;
	}

	public static void AddCard(string modDir, string cardId, string json = "{}")
	{
		var path = Path.Combine(modDir, "content", "cards", cardId + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
	}

	public static void AddSkill(string modDir, string skillId, string json = "{}")
	{
		var path = Path.Combine(modDir, "content", "skills", skillId + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
	}

	public static void AddEffect(string modDir, string effectId, string json = "{}")
	{
		var path = Path.Combine(modDir, "content", "effects", effectId + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
	}

	public static void AddBuff(string modDir, string buffId, string json = "{}")
	{
		var path = Path.Combine(modDir, "content", "buffs", buffId + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
	}

	public static ModContentBundle EmptyBundle(string modId) => new(
		modId,
		[],
		[],
		[],
		[],
		[],
		[],
		[],
		[],
		ModDefinitionsBundle.Empty);
}
