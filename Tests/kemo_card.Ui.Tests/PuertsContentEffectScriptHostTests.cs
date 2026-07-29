using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class PuertsContentEffectScriptHostTests
{
    [Test]
    public void TryExecute_returns_proposed_effects()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_script_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
        ContentModTestHelper.AddScript(
            modDir,
            "effects/demo.js",
            """
			export function execute(ctx) {
			  return { proposedEffects: [{ kind: "Damage", params: { amount: 3 } }] };
			}
			""");

        var catalog = new ModScriptCatalog();
        catalog.Rebuild(
        [
            new DiscoveredModEntry(
                modDir,
                new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }),
        ]);

        var registry = new GameDefinitionRegistry();
        using var runtime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());
        var host = new PuertsContentEffectScriptHost(runtime, registry);

        var ok = host.TryExecute(
            "base.game",
            "effects/demo.js",
            "execute",
            new Dictionary<string, object> { ["runSeed"] = 42 },
            out var proposedEffects);

        Assert.That(ok, Is.True);
        Assert.That(proposedEffects, Has.Count.EqualTo(1));
        Assert.That(proposedEffects[0]["kind"], Is.EqualTo("Damage"));
    }
}