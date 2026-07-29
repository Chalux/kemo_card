using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModLoaderTests
{
    [Test]
    public void Load_collects_card_ids_from_filenames()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(modDir, "strike");

        var discovery = new ContentModDiscovery();
        var entry = discovery.Scan(root).ValidMods[0];
        // var loader = new ContentModLoader();
        var bundle = ContentModLoader.Load(entry);

        Assert.That(bundle.Cards, Does.Contain("strike"));
    }
}