using Puerts;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptLoader : ILoader
{
    /// <summary>内置 puerts JS 的内嵌资源名前缀（资源在 <c>kemo_card.csproj</c> 里声明）。</summary>
    private const string EmbeddedPrefix = "KemoCard.PuertsJs.";

    private readonly ModScriptCatalog _catalog;

    // NuGet 的 Puerts.Core.targets 只在 Build 之后把 contentFiles/puerts/ 拷到 OutDir：编辑器进程的
    // 程序集目录就是 OutDir，能直接读文件；**导出的 data 目录里没有这些 JS**，因此再兜一层内嵌资源
    // （见 ReadFile / TryGetEmbedded），否则导出后启动会在 esm_bootstrap.cjs 上抛
    // DirectoryNotFoundException。
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

        return _fallback.FileExists(filepath) || TryGetEmbedded(filepath, out _);
    }

    public string ReadFile(string filepath, out string debugpath)
    {
        if (TryResolveModScript(filepath, out var fullPath))
        {
            debugpath = fullPath;
            return File.ReadAllText(fullPath);
        }

        if (!_fallback.FileExists(filepath) && TryGetEmbedded(filepath, out var embedded))
        {
            // 模块 URL 保持「数据目录/puerts/<名>」的真实形状：JS 侧按它做相对路径解析，
            // 后续 import 仍会回到本 loader，再按文件名映射到内嵌资源。
            debugpath = Path.Combine(AppContext.BaseDirectory, "puerts", Path.GetFileName(filepath));
            return embedded;
        }

        return _fallback.ReadFile(filepath, out debugpath);
    }

    /// <summary>
    /// 按**文件名**在程序集内嵌资源里找 puerts 内置 JS（导出产物里没有 OutDir/puerts 的拷贝）；
    /// 找不到返回 <c>false</c>。按文件名而不是完整路径，是因为 JS 侧的相对 import 解析出来的
    /// 路径前缀取决于它拿到的模块 URL。
    /// </summary>
    internal static bool TryGetEmbedded(string filepath, out string content)
    {
        content = string.Empty;
        var name = Path.GetFileName(filepath);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        using var stream = typeof(ModScriptLoader).Assembly.GetManifestResourceStream(EmbeddedPrefix + name);
        if (stream is null)
        {
            return false;
        }

        using var reader = new StreamReader(stream);
        content = reader.ReadToEnd();
        return true;
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