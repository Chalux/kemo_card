using Puerts;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptLoader : ILoader
{
    private readonly ModScriptCatalog _catalog;
    // NuGet 将内置脚本拷到 OutDir/puerts/；Godot 进程 CWD 是项目根，须用程序集目录作 root。
    private readonly DefaultLoader _fallback = new(AppContext.BaseDirectory);

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

        var scriptsRoot = Path.GetFullPath(Path.Combine(modFolder, "scripts"));
        var relativePath = scriptPath.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(scriptsRoot, relativePath));

        // 前缀必须带目录分隔符：否则同级目录 "<mod>/scripts-evil/x.js" 也以
        // "<mod>/scripts" 为前缀，会被误判为合法脚本路径而越出 scripts/ 根。
        var rootPrefix = scriptsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? scriptsRoot
            : scriptsRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            return false;
        }

        fullPath = candidate;
        return File.Exists(fullPath);
    }
}