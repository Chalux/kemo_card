using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Condition;
using KemoCard.Mod.Global.Save;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class GlobalPersistentCondContextTests
{
    [Test]
    public void HasFlag_maps_to_global_unlocks()
    {
        var model = new GlobalMod();
        var controller = new GlobalModController(model, new GlobalSaveService(Path.GetTempPath()));
        var context = new GlobalPersistentCondContext(controller);

        Assert.That(context.HasFlag("story.x.clear"), Is.False);

        controller.UnlockContent("story.x.clear");

        Assert.That(context.HasFlag("story.x.clear"), Is.True);
    }
}