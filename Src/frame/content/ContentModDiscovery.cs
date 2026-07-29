using System.Text.Json;

namespace KemoCard.Frame.Content;

public sealed class ContentModDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ContentModDiscoveryResult Scan(string modRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modRootDirectory);

        var validMods = new List<DiscoveredModEntry>();
        var skippedMods = new List<ModSkipEntry>();
        var seenModIds = new HashSet<string>(StringComparer.Ordinal);

        if (!Directory.Exists(modRootDirectory))
        {
            return new ContentModDiscoveryResult(validMods, skippedMods);
        }

        foreach (var folderPath in Directory.EnumerateDirectories(modRootDirectory).OrderBy(static p => p, StringComparer.Ordinal))
        {
            var folderName = Path.GetFileName(folderPath);
            var manifestPath = Path.Combine(folderPath, "mod.json");
            if (!File.Exists(manifestPath))
            {
                skippedMods.Add(new ModSkipEntry(folderName, ModSkipReason.InvalidManifest, "mod.json missing"));
                continue;
            }

            ContentModManifestDto? manifest;
            try
            {
                var json = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize<ContentModManifestDto>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                skippedMods.Add(new ModSkipEntry(folderName, ModSkipReason.InvalidManifest, ex.Message));
                continue;
            }

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ModId))
            {
                skippedMods.Add(new ModSkipEntry(folderName, ModSkipReason.InvalidManifest, "modId missing"));
                continue;
            }

            if (!seenModIds.Add(manifest.ModId))
            {
                skippedMods.Add(new ModSkipEntry(manifest.ModId, ModSkipReason.DuplicateModId, folderName));
                continue;
            }

            validMods.Add(new DiscoveredModEntry(folderPath, manifest));
        }

        return new ContentModDiscoveryResult(validMods, skippedMods);
    }
}