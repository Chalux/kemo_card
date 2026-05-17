namespace KemoCard.Ui.Tests;

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

	public static void AddCard(string modDir, string cardId)
	{
		var path = Path.Combine(modDir, "content", "cards", cardId + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{}");
	}
}
