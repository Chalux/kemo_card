using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModPipelineTests
{
	[Test]
	public void Rebuild_merges_enabled_mods_and_reports_conflict()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
		var baseDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
		ContentModTestHelper.AddCard(baseDir, "strike");
		var addonDir = ContentModTestHelper.CreateModFolder(root, "addon", "addon.mod", required: new[] { "base.game" });
		ContentModTestHelper.AddCard(addonDir, "strike");

		var registry = new GameDefinitionRegistry();
		var pipeline = new ContentModPipeline(
			root,
			registry,
			new NullContentModLogger(),
			new NullContentModUserNotifier(),
			NullScriptRuntimeResetter.Instance,
			new ModScriptCatalog());
		var report = pipeline.Rebuild(new[] { "base.game", "addon.mod" });

		Assert.That(registry.Contains(EContentCategory.Card, "strike"), Is.True);
		Assert.That(report.IdConflicts, Has.Count.EqualTo(1));
	}
}
