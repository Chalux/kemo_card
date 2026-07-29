using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModTranslationLoaderTests
{
    [Test]
    public void Rebuild_clears_and_loads_translations_for_each_active_mod()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.CreateModFolder(root, "addon", "addon.mod", required: ["base.game"]);

        var loader = new RecordingContentModTranslationLoader();
        var registry = new GameDefinitionRegistry();
        var pipeline = new ContentModPipeline(
            root,
            registry,
            new NullContentModLogger(),
            new NullContentModUserNotifier(),
            NullScriptRuntimeResetter.Instance,
            new ModScriptCatalog(),
            loader);

        pipeline.Rebuild(["base.game", "addon.mod"]);
        Assert.That(loader.ClearCount, Is.EqualTo(1));
        Assert.That(loader.LoadedModIds, Is.EqualTo(new[] { "base.game", "addon.mod" }));

        pipeline.Rebuild(["base.game"]);
        Assert.That(loader.ClearCount, Is.EqualTo(2));
        Assert.That(loader.LoadedModIds, Is.EqualTo(new[] { "base.game" }));
    }

    private sealed class RecordingContentModTranslationLoader : IContentModTranslationLoader
    {
        public int ClearCount { get; private set; }

        public List<string> LoadedModIds { get; } = [];

        public void ClearRegistered()
        {
            ClearCount++;
            LoadedModIds.Clear();
        }

        public void TryLoadModTranslations(DiscoveredModEntry entry)
        {
            LoadedModIds.Add(entry.Manifest.ModId);
        }
    }
}