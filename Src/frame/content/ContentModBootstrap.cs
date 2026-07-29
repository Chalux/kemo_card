using System.Text.Json;

namespace KemoCard.Frame.Content;

public static class ContentModBootstrap
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void EnsureDefaultModsCopied(string modRootDirectory, string bundledModsSourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modRootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundledModsSourceDirectory);

        if (!Directory.Exists(bundledModsSourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(modRootDirectory);
        foreach (var sourceDir in Directory.EnumerateDirectories(bundledModsSourceDirectory))
        {
            var folderName = Path.GetFileName(sourceDir);
            var destDir = Path.Combine(modRootDirectory, folderName);
            EnsureModCopied(sourceDir, destDir);
        }
    }

    private static void EnsureModCopied(string sourceDir, string destDir)
    {
        if (!TryReadManifestVersion(sourceDir, out var bundledVersion))
        {
            return;
        }

        if (Directory.Exists(destDir))
        {
            if (TryReadManifestVersion(destDir, out var installedVersion)
                && string.Equals(installedVersion, bundledVersion, StringComparison.Ordinal))
            {
                return;
            }

            DeleteDirectoryIfExists(destDir);
        }

        var stagingDir = destDir + ".staging";
        DeleteDirectoryIfExists(stagingDir);

        try
        {
            CopyDirectoryRecursive(sourceDir, stagingDir);
            if (Directory.Exists(destDir))
            {
                DeleteDirectoryIfExists(destDir);
            }

            Directory.Move(stagingDir, destDir);
        }
        catch
        {
            DeleteDirectoryIfExists(stagingDir);
            throw;
        }
    }

    private static bool TryReadManifestVersion(string modDir, out string version)
    {
        version = string.Empty;
        var manifestPath = Path.Combine(modDir, "mod.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ContentModManifestDto>(json, JsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return false;
            }

            version = manifest.Version;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSub = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSub);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}