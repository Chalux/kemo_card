using System;
using System.IO;
using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 卡牌美术路径解析与加载（Mod content 根 → <c>res://Resource/Assets/</c>），
/// 与 <see cref="CharacterArtLoader"/> 同构：<see cref="Comp.BaseCardItem"/> 与战斗界面的
/// 已标记卡图标共用同一份加载逻辑（改一处即两处生效）。
/// </summary>
public static class CardArtLoader
{
    public static Texture2D? TryLoadTexture(string relativePath, string? cardId = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(cardId)
                && TryResolveModFile(cardId, relativePath, out var modFile)
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
            AppLog.Warning($"CardArtLoader: mod texture load failed: {ex.Message}", "CardArtLoader");
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
            AppLog.Warning($"CardArtLoader: resource texture load failed: {ex.Message}", "CardArtLoader");
        }

        return null;
    }

    private static string ToResPath(string relativePath) =>
        $"res://Resource/Assets/{relativePath.Replace('\\', '/')}";

    private static bool TryResolveModFile(string cardId, string relativePath, out string fullPath)
    {
        fullPath = "";
        try
        {
            var pipeline = AppRoot.Services.ContentModPipeline;
            if (!pipeline.Registry.TryGetOwnerModId(EContentCategory.Card, cardId, out var modId))
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
