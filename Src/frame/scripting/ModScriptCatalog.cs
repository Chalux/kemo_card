using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptCatalog
{
    private readonly Dictionary<string, (string FolderPath, string ContentRoot, string DisplayNameKey)> _byModId =
        new(StringComparer.Ordinal);

    public void Rebuild(IReadOnlyList<DiscoveredModEntry> activeMods)
    {
        _byModId.Clear();
        foreach (var entry in activeMods)
        {
            var contentRoot = string.IsNullOrWhiteSpace(entry.Manifest.ContentRoot)
                ? "content"
                : entry.Manifest.ContentRoot;
            _byModId[entry.Manifest.ModId] = (entry.FolderPath, contentRoot, entry.Manifest.DisplayName);
        }
    }

    public bool TryGetFolderPath(string modId, out string folderPath)
    {
        if (_byModId.TryGetValue(modId, out var entry))
        {
            folderPath = entry.FolderPath;
            return true;
        }

        folderPath = null!;
        return false;
    }

    public bool TryGetContentRootPath(string modId, out string contentRootPath)
    {
        if (_byModId.TryGetValue(modId, out var entry))
        {
            contentRootPath = Path.Combine(entry.FolderPath, entry.ContentRoot);
            return true;
        }

        contentRootPath = null!;
        return false;
    }

    public bool TryGetDisplayNameKey(string modId, out string displayNameKey)
    {
        if (_byModId.TryGetValue(modId, out var entry))
        {
            displayNameKey = entry.DisplayNameKey;
            return true;
        }

        displayNameKey = null!;
        return false;
    }
}
