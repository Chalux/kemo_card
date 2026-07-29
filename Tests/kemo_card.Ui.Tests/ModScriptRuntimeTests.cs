using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ModScriptRuntimeTests
{
    [Test]
    public void Invoke_executes_exported_function_and_returns_success()
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

        var call = new ScriptCallContext
        {
            RunSeed = 1,
            StreamKey = "effect:demo",
            Registry = registry,
        };
        var result = runtime.Invoke("base.game", "effects/demo.js", "execute", call);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RawReturn, Is.Not.Null);
    }

    [Test]
    public void Recreate_picks_up_changed_script()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_script_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
        var scriptPath = Path.Combine(modDir, "scripts", "effects", "demo.js");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(
            scriptPath,
            """
			export function execute(ctx) {
			  return { proposedEffects: [{ kind: "Damage", params: { amount: 1 } }] };
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
        var call = new ScriptCallContext
        {
            RunSeed = 1,
            StreamKey = "effect:demo",
            Registry = registry,
        };

        var first = runtime.Invoke("base.game", "effects/demo.js", "execute", call);
        Assert.That(first.Success, Is.True);

        File.WriteAllText(
            scriptPath,
            """
			export function execute(ctx) {
			  return { proposedEffects: [{ kind: "Damage", params: { amount: 99 } }] };
			}
			""");
        runtime.Recreate();

        var second = runtime.Invoke("base.game", "effects/demo.js", "execute", call);
        Assert.That(second.Success, Is.True);
        Assert.That(second.RawReturn, Is.Not.Null);
    }
}