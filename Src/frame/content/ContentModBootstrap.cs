using System.Text.Json;

namespace KemoCard.Frame.Content;

public static class ContentModBootstrap
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 把随包分发的 Mod 拷贝到用户可写目录。<b>版本号相同即跳过</b>（发布语义：不每次启动都重拷）。
    /// </summary>
    /// <param name="forceRefresh">
    /// 调试构建用：忽略版本号，总是重新拷贝。开发期改内容（新增角色/卡牌/翻译……）常常忘了抬
    /// <c>mod.json</c> 版本号，导致游戏里看不到——打开它即可让每次启动都同步最新内容。
    /// 由调用方注入（<c>ModStartupContext.ForceContentModRefresh</c>），本类<b>不</b>直接读 Godot 的构建标记，
    /// 以便逻辑层测试不依赖引擎。
    /// </param>
    public static void EnsureDefaultModsCopied(
        string modRootDirectory,
        string bundledModsSourceDirectory,
        bool forceRefresh = false)
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
            EnsureModCopied(sourceDir, destDir, forceRefresh);
        }
    }

    private static void EnsureModCopied(string sourceDir, string destDir, bool forceRefresh)
    {
        if (!TryReadManifestVersion(sourceDir, out var bundledVersion))
        {
            return;
        }

        if (Directory.Exists(destDir))
        {
            if (!forceRefresh &&
                TryReadManifestVersion(destDir, out var installedVersion) &&
                string.Equals(installedVersion, bundledVersion, StringComparison.Ordinal))
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