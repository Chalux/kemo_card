using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ModScriptCatalogTests
{
	[Test]
	public void TryGetContentRootPath_combines_folder_and_content_root()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_catalog_tests", Guid.NewGuid().ToString("N"));
		var modDir = Path.Combine(root, "base-game");
		Directory.CreateDirectory(modDir);

		var catalog = new ModScriptCatalog();
		catalog.Rebuild(
		[
			new DiscoveredModEntry(
				modDir,
				new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }),
		]);

		Assert.That(catalog.TryGetFolderPath("base.game", out var folder), Is.True);
		Assert.That(folder, Is.EqualTo(modDir));

		Assert.That(catalog.TryGetContentRootPath("base.game", out var contentRoot), Is.True);
		Assert.That(Path.GetFullPath(contentRoot), Is.EqualTo(Path.GetFullPath(Path.Combine(modDir, "content"))));
	}

	[Test]
	public void TryGetContentRootPath_unknown_mod_returns_false()
	{
		var catalog = new ModScriptCatalog();
		Assert.That(catalog.TryGetContentRootPath("missing", out _), Is.False);
	}
}
