using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptCatalog
{
	private readonly Dictionary<string, string> _folderByModId = new(StringComparer.Ordinal);

	public void Rebuild(IReadOnlyList<DiscoveredModEntry> activeMods)
	{
		_folderByModId.Clear();
		foreach (var entry in activeMods)
		{
			_folderByModId[entry.Manifest.ModId] = entry.FolderPath;
		}
	}

	public bool TryGetFolderPath(string modId, out string folderPath) =>
		_folderByModId.TryGetValue(modId, out folderPath!);
}
