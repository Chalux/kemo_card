using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModTranslationPathsTests
{
    [Test]
    public void GetDirectory_resolves_content_translations_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        var entry = new DiscoveredModEntry(modDir, new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" });

        var dir = ContentModTranslationPaths.GetDirectory(entry);

        Assert.That(dir, Is.EqualTo(Path.Combine(modDir, "content", "translations")));
    }
}