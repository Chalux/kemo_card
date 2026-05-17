namespace KemoCard.Frame.Content;

public sealed class ContentModLoader
{
	public ModContentBundle Load(DiscoveredModEntry entry)
	{
		var manifest = entry.Manifest;
		var contentRoot = Path.Combine(entry.FolderPath, manifest.ContentRoot);
		try
		{
			return new ModContentBundle(
				manifest.ModId,
				CollectIds(contentRoot, "characters"),
				CollectIds(contentRoot, "battles"),
				CollectIds(contentRoot, "events"),
				CollectIds(contentRoot, "cards"),
				CollectIds(contentRoot, "items"),
				CollectIds(contentRoot, "skills"),
				CollectIds(contentRoot, "buffs"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			throw new ContentModLoadException(manifest.ModId, ex.Message, ex);
		}
	}

	private static IReadOnlyList<string> CollectIds(string contentRoot, string folderName)
	{
		var dir = Path.Combine(contentRoot, folderName);
		if (!Directory.Exists(dir))
		{
			return Array.Empty<string>();
		}

		return Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
			.Select(Path.GetFileNameWithoutExtension)
			.Where(static id => !string.IsNullOrWhiteSpace(id))
			.Select(static id => id!)
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToList();
	}
}
