using System;
using System.IO;
using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 角色美术路径解析与加载（Mod content 根 → <c>res://Resource/Assets/</c>），对齐 <see cref="Comp.BaseCardItem"/>。
/// </summary>
public static class CharacterArtLoader
{
    public static bool PathExists(string relativePath, string? characterId = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(characterId)
            && TryResolveModFile(characterId, relativePath, out var modFile)
            && File.Exists(modFile))
        {
            return true;
        }

        var resPath = ToResPath(relativePath);
        return ResourceLoader.Exists(resPath);
    }

    public static Texture2D? TryLoadTexture(string relativePath, string? characterId = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(characterId)
                && TryResolveModFile(characterId, relativePath, out var modFile)
                && File.Exists(modFile))
            {
                var image = Image.LoadFromFile(modFile);
                if (image != null)
                {
                    return ImageTexture.CreateFromImage(image);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"CharacterArtLoader: mod texture load failed: {ex.Message}", "CharacterArtLoader");
        }

        var resPath = ToResPath(relativePath);
        try
        {
            if (ResourceLoader.Exists(resPath))
            {
                return ResourceLoader.Load<Texture2D>(resPath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"CharacterArtLoader: resource texture load failed: {ex.Message}", "CharacterArtLoader");
        }

        return null;
    }

    public static SpriteFrames? TryLoadSpriteFrames(string relativePath, string? characterId = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var resPath = ToResPath(relativePath);
        try
        {
            if (ResourceLoader.Exists(resPath))
            {
                return ResourceLoader.Load<SpriteFrames>(resPath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"CharacterArtLoader: resource SpriteFrames load failed: {ex.Message}", "CharacterArtLoader");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(characterId)
                && TryResolveModFile(characterId, relativePath, out var modFile)
                && File.Exists(modFile))
            {
                return ResourceLoader.Load<SpriteFrames>(modFile);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"CharacterArtLoader: mod SpriteFrames load failed: {ex.Message}", "CharacterArtLoader");
        }

        return null;
    }

    private static string ToResPath(string relativePath) =>
        $"res://Resource/Assets/{relativePath.Replace('\\', '/')}";

    private static bool TryResolveModFile(string characterId, string relativePath, out string fullPath)
    {
        fullPath = "";
        try
        {
            var pipeline = AppRoot.Services.ContentModPipeline;
            if (!pipeline.Registry.TryGetOwnerModId(EContentCategory.Character, characterId, out var modId))
            {
                return false;
            }

            if (!pipeline.ScriptCatalog.TryGetContentRootPath(modId, out var contentRoot))
            {
                return false;
            }

            var relative = relativePath.Replace('/', Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(Path.Combine(contentRoot, relative));
            var rootFull = Path.GetFullPath(contentRoot);
            if (!fullPath.StartsWith(rootFull, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
            {
                fullPath = "";
                return false;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // AppRoot 未初始化
            return false;
        }
    }
}