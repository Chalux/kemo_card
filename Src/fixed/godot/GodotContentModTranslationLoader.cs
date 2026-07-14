using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Logging;

namespace KemoCard.Fixed.Godot;

public sealed class GodotContentModTranslationLoader : IContentModTranslationLoader
{
	private readonly List<Translation> _registered = [];

	public void ClearRegistered()
	{
		foreach (var translation in _registered)
		{
			TranslationServer.RemoveTranslation(translation);
		}

		_registered.Clear();
	}

	public void TryLoadModTranslations(DiscoveredModEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);

		var translationsDir = ContentModTranslationPaths.GetDirectory(entry);
		if (!Directory.Exists(translationsDir))
		{
			return;
		}

		foreach (var filePath in Directory.EnumerateFiles(translationsDir, "*.translation", SearchOption.TopDirectoryOnly)
			         .OrderBy(static p => p, StringComparer.Ordinal))
		{
			TryLoadTranslationFile(entry.Manifest.ModId, filePath);
		}
	}

	private void TryLoadTranslationFile(string modId, string absolutePath)
	{
		var resourcePath = ProjectSettings.LocalizePath(absolutePath);
		if (string.IsNullOrEmpty(resourcePath))
		{
			AppLog.Warning($"Mod '{modId}': cannot localize translation path '{absolutePath}'.", "ContentMod");
			return;
		}

		var translation = ResourceLoader.Load<Translation>(resourcePath);
		if (translation is null)
		{
			AppLog.Warning($"Mod '{modId}': failed to load translation '{resourcePath}'.", "ContentMod");
			return;
		}

		TranslationServer.AddTranslation(translation);
		_registered.Add(translation);
	}
}
