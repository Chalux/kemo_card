using Puerts;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptLoader : ILoader
{
	private readonly ModScriptCatalog _catalog;
	private readonly DefaultLoader _fallback = new();

	public ModScriptLoader(ModScriptCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		_catalog = catalog;
	}

	public bool FileExists(string filepath)
	{
		if (TryResolveModScript(filepath, out _))
		{
			return true;
		}

		return _fallback.FileExists(filepath);
	}

	public string ReadFile(string filepath, out string debugpath)
	{
		if (TryResolveModScript(filepath, out var fullPath))
		{
			debugpath = fullPath;
			return File.ReadAllText(fullPath);
		}

		return _fallback.ReadFile(filepath, out debugpath);
	}

	private bool TryResolveModScript(string specifier, out string fullPath)
	{
		fullPath = string.Empty;
		var slash = specifier.IndexOf('/');
		if (slash <= 0 || slash >= specifier.Length - 1)
		{
			return false;
		}

		var modId = specifier[..slash];
		var scriptPath = specifier[(slash + 1)..];
		if (!_catalog.TryGetFolderPath(modId, out var modFolder))
		{
			return false;
		}

		fullPath = Path.Combine(
			modFolder,
			"scripts",
			scriptPath.Replace('/', Path.DirectorySeparatorChar));
		return File.Exists(fullPath);
	}
}
