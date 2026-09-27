using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ModScriptLoaderTests
{
    [Test]
    public void ReadFile_resolves_modId_to_folder_scripts_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_script_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
        ContentModTestHelper.AddScript(
            modDir,
            "effects/demo.js",
            "export function execute(ctx) { return { proposedEffects: [] }; }");

        var catalog = new ModScriptCatalog();
        catalog.Rebuild(
        [
            new DiscoveredModEntry(
                modDir,
                new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }),
        ]);

        var loader = new ModScriptLoader(catalog);
        Assert.That(loader.FileExists("base.game/effects/demo.js"), Is.True);
        var source = loader.ReadFile("base.game/effects/demo.js", out var debugPath);
        Assert.That(source, Does.Contain("proposedEffects"));
        Assert.That(debugPath, Does.Contain("effects"));
    }

    [Test]
    public void FileExists_rejects_path_traversal_outside_scripts_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_script_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
        var secretPath = Path.Combine(root, "secret.txt");
        File.WriteAllText(secretPath, "secret");

        var catalog = new ModScriptCatalog();
        catalog.Rebuild(
        [
            new DiscoveredModEntry(
                modDir,
                new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }),
        ]);

        var loader = new ModScriptLoader(catalog);
        Assert.That(loader.FileExists("base.game/../../secret.txt"), Is.False);
    }

    /// <summary>
    /// 导出产物里没有 <c>OutDir/puerts</c> 的 JS 拷贝（<c>Puerts.Core.targets</c> 只在 Build 之后拷 OutDir，
    /// 导出的 data 目录拿不到），所以内置脚本必须嵌在程序集里；清单少一个都会在导出后启动时抛
    /// <c>DirectoryNotFoundException</c>。
    /// </summary>
    [Test]
    public void Embedded_puerts_js_covers_the_runtime_files()
    {
        var names = typeof(ModScriptLoader).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith("KemoCard.PuertsJs.", StringComparison.Ordinal))
            .Select(name => name["KemoCard.PuertsJs.".Length..])
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(
            names,
            Is.SupersetOf(new[]
            {
                "esm_bootstrap.cjs", "init.mjs", "init_il2cpp.mjs", "csharp.mjs", "module.mjs",
                "events.mjs", "log.mjs", "nodepatch.mjs", "polyfill.mjs", "promises.mjs",
                "timer.mjs", "websocketpp.mjs", "dispose.mjs",
            }));
    }

    /// <summary>
    /// 内嵌查找按**文件名**兜底：JS 侧相对 import 解析出的路径前缀未必与包内布局一致
    /// （模块 URL 由 loader 返回的 debugpath 决定）。
    /// </summary>
    [Test]
    public void TryGetEmbedded_maps_by_file_name_and_serves_content()
    {
        Assert.That(ModScriptLoader.TryGetEmbedded("puerts/esm_bootstrap.cjs", out var bootstrap), Is.True);
        Assert.That(bootstrap, Does.Contain("puerts"));

        Assert.That(ModScriptLoader.TryGetEmbedded(@"C:\any\dir\init.mjs", out var init), Is.True);
        Assert.That(init, Is.Not.Empty);

        Assert.That(ModScriptLoader.TryGetEmbedded("puerts/not_a_real_file.js", out _), Is.False);
    }
}