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
}
