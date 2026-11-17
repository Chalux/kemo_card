using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global;

namespace KemoCard.Mod.Run.Ui.Items;

internal static class ItemArtLoader
{
    public static Texture2D? TryLoad(ItemDto? item)
    {
        if (string.IsNullOrWhiteSpace(item?.ArtPath)) return null;
        var path = item.ArtPath.Replace('\\', '/');
        if (path.StartsWith("res://", StringComparison.Ordinal))
            return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        var resource = "res://Resource/Assets/" + path;
        if (ResourceLoader.Exists(resource)) return ResourceLoader.Load<Texture2D>(resource);
        try
        {
            var pipeline = AppRoot.Services.ContentModPipeline;
            if (!pipeline.Registry.TryGetOwnerModId(EContentCategory.Item, item.Id, out var owner) ||
                !pipeline.ScriptCatalog.TryGetContentRootPath(owner, out var root)) return null;
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path));
            var prefix = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || !System.IO.File.Exists(full)) return null;
            return ImageTexture.CreateFromImage(Image.LoadFromFile(full));
        }
        catch (InvalidOperationException) { return null; }
    }
}