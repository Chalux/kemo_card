using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ModScriptRuntimeConcurrencyTests
{
	[Test]
	public void Invoke_during_BeginRebuild_returns_rebuilding_error()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_runtime_tests", Guid.NewGuid().ToString("N"));
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

		var registry = new GameDefinitionRegistry();
		using var runtime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());
		runtime.BeginRebuild();

		var result = runtime.Invoke(
			"base.game",
			"effects/demo.js",
			"execute",
			new ScriptCallContext
			{
				RunSeed = 0,
				StreamKey = "test",
				Registry = registry,
			});

		Assert.That(result.Success, Is.False);
		Assert.That(result.Error, Does.Contain("rebuilding"));
	}
}
