using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ModScriptPrewarmerTests
{
    [Test]
    public void Warm_reports_invalid_script_module()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_script_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
        ContentModTestHelper.AddScript(modDir, "effects/broken.js", "export function execute( {");

        var effects = new Dictionary<string, EffectDto>(StringComparer.Ordinal)
        {
            ["fx_broken"] = new()
            {
                Id = "fx_broken",
                Kind = EEffectKind.ExecuteScript,
                ScriptPath = "effects/broken.js",
            },
        };
        var bundle = new ModContentBundle(
            "base.game",
            [], [], [], [], [], [], [], [], ["fx_broken"],
            ModDefinitionsBundle.Empty with { Effects = effects });

        var registry = new GameDefinitionRegistry();
        registry.Rebuild([bundle], out _);

        var catalog = new ModScriptCatalog();
        catalog.Rebuild(
        [
            new DiscoveredModEntry(
                modDir,
                new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }),
        ]);

        using var runtime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());
        var prewarmer = new ModScriptPrewarmer(runtime, registry);
        var errors = prewarmer.Warm();

        Assert.That(errors, Is.Not.Empty);
        Assert.That(errors[0].ScriptPath, Is.EqualTo("effects/broken.js"));
    }
}